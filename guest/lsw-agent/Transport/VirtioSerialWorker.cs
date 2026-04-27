using System.IO.Ports;
using System.Text;
using LswAgent.Rpc;

namespace LswAgent.Transport;

/// <summary>
/// Background worker that owns the virtio-serial channel.
///
/// Frame format (length-prefix):
///   4 bytes LE uint32 = payload length
///   N bytes UTF-8 JSON
///
/// The virtio-serial port appears in Windows as a named pipe / COM port
/// depending on driver version. We try the known pipe path first, then
/// fall back to a configurable COM port name via the LSW_SERIAL_PORT
/// environment variable.
/// </summary>
public sealed class VirtioSerialWorker : BackgroundService
{
    private const string DefaultPipePath = @"\\.\Global\org.lsw.agent";
    private static readonly Encoding Utf8 = new UTF8Encoding(false, false);

    private readonly RpcDispatcher _dispatcher;
    private readonly ILogger<VirtioSerialWorker> _logger;

    public VirtioSerialWorker(RpcDispatcher dispatcher, ILogger<VirtioSerialWorker> logger)
    {
        _dispatcher = dispatcher;
        _logger     = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VirtioSerialWorker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunChannelAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Channel error; reconnecting in 3 s");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }

        _logger.LogInformation("VirtioSerialWorker stopped");
    }

    private async Task RunChannelAsync(CancellationToken ct)
    {
        await using var stream = OpenChannel();
        _logger.LogInformation("virtio-serial channel open");

        while (!ct.IsCancellationRequested)
        {
            var request = await ReadFrameAsync(stream, ct);
            if (request == null) break; // EOF

            _logger.LogDebug("RX {Bytes} bytes", request.Length);
            string responseJson = await _dispatcher.DispatchAsync(request, ct);
            await WriteFrameAsync(stream, responseJson, ct);
        }
    }

    private static Stream OpenChannel()
    {
        // 1. Try named-pipe path used by newer virtio-serial drivers
        string pipePath = Environment.GetEnvironmentVariable("LSW_SERIAL_PORT") ?? DefaultPipePath;

        if (pipePath.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase))
        {
            // Open via FileStream (handles both named pipes and device files)
            return new FileStream(
                pipePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);
        }

        // 2. COM port path (e.g. "COM3")
        var sp = new SerialPort(pipePath, 115200, Parity.None, 8, StopBits.One)
        {
            ReadTimeout  = SerialPort.InfiniteTimeout,
            WriteTimeout = SerialPort.InfiniteTimeout,
        };
        sp.Open();
        return sp.BaseStream;
    }

    /// <summary>Reads one length-prefixed frame. Returns null on clean EOF.</summary>
    private static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var lenBuf = new byte[4];
        int read = 0;
        while (read < 4)
        {
            int n = await stream.ReadAsync(lenBuf.AsMemory(read, 4 - read), ct);
            if (n == 0) return null;
            read += n;
        }

        int length = (int)BitConverter.ToUInt32(lenBuf, 0);
        if (length <= 0 || length > 64 * 1024 * 1024)
            throw new InvalidDataException($"invalid frame length {length}");

        var payload = new byte[length];
        read = 0;
        while (read < length)
        {
            int n = await stream.ReadAsync(payload.AsMemory(read, length - read), ct);
            if (n == 0) throw new EndOfStreamException("truncated frame");
            read += n;
        }

        return Utf8.GetString(payload);
    }

    /// <summary>Writes one length-prefixed frame.</summary>
    private static async Task WriteFrameAsync(Stream stream, string json, CancellationToken ct)
    {
        byte[] payload = Utf8.GetBytes(json);
        byte[] lenBuf  = BitConverter.GetBytes((uint)payload.Length);
        await stream.WriteAsync(lenBuf.AsMemory(), ct);
        await stream.WriteAsync(payload.AsMemory(), ct);
        await stream.FlushAsync(ct);
    }
}
