using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO.Ports;

namespace Lsw.Agent;

public sealed class AgentWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AgentConfig _config;
    private readonly JsonRpcDispatcher _dispatcher;
    private readonly ILogger<AgentWorker> _logger;

    public AgentWorker(AgentConfig config, JsonRpcDispatcher dispatcher, ILogger<AgentWorker> logger)
    {
        _config = config;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var stream = await OpenTransportAsync(stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    var request = await ReadFrameAsync(stream, stoppingToken);
                    var response = await _dispatcher.DispatchAsync(request, stoppingToken);
                    await WriteFrameAsync(stream, response, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agent transport loop failed, retrying.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task<Stream> OpenTransportAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_config.SerialPortOverride))
        {
            var serial = new SerialPort(_config.SerialPortOverride, 115200)
            {
                ReadTimeout = 0,
                WriteTimeout = 0
            };
            serial.Open();
            return serial.BaseStream;
        }

        var pipeName = Environment.GetEnvironmentVariable("LSW_AGENT_PIPE") ?? "lsw-agent";
        var fullPipePath = $@"\\.\pipe\{pipeName}";
        _logger.LogInformation("Using named pipe transport {PipePath}", fullPipePath);
        return new FileStream(fullPipePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
    }

    private static async Task<JsonRpcRequest> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lenBuf = await ReadExactlyAsync(stream, 4, cancellationToken);
        var len = BinaryPrimitives.ReadUInt32LittleEndian(lenBuf);
        if (len == 0 || len > 16 * 1024 * 1024)
        {
            throw new InvalidDataException("invalid frame size");
        }

        var payload = await ReadExactlyAsync(stream, (int)len, cancellationToken);
        var json = Encoding.UTF8.GetString(payload);
        return JsonSerializer.Deserialize<JsonRpcRequest>(json, JsonOptions)
               ?? throw new InvalidDataException("invalid json-rpc payload");
    }

    private static async Task WriteFrameAsync(Stream stream, JsonRpcResponse response, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        var payload = Encoding.UTF8.GetBytes(json);
        var len = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(len, (uint)payload.Length);
        await stream.WriteAsync(len, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read <= 0)
            {
                throw new EndOfStreamException();
            }
            offset += read;
        }
        return buffer;
    }
}
