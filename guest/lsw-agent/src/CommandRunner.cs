using System.Diagnostics;
using System.Text;

namespace Lsw.Agent;

public sealed class CommandRunner
{
    private readonly AgentConfig _config;

    public CommandRunner(AgentConfig config)
    {
        _config = config;
    }

    public async Task<object> RunShellAsync(RunCmdShellParams request, CancellationToken cancellationToken)
    {
        var shellExe = request.Shell.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            ? "powershell.exe"
            : request.Shell;

        var psi = new ProcessStartInfo
        {
            FileName = shellExe,
            Arguments = request.Shell.Equals("powershell", StringComparison.OrdinalIgnoreCase)
                ? $"-NoProfile -NonInteractive -Command \"{request.Cmd.Replace("\"", "\\\"")}\""
                : request.Cmd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.Cwd) ? Environment.SystemDirectory : request.Cwd
        };

        if (request.Env is not null)
        {
            foreach (var (key, value) in request.Env)
            {
                psi.Environment[key] = value;
            }
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start process");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_config.CommandTimeout);

        var stdoutTask = ReadBoundedAsync(process.StandardOutput, timeoutCts.Token);
        var stderrTask = ReadBoundedAsync(process.StandardError, timeoutCts.Token);

        await process.WaitForExitAsync(timeoutCts.Token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new
        {
            exit_code = process.ExitCode,
            stdout,
            stderr,
            truncated = stdout.Length >= _config.MaxCommandBytes || stderr.Length >= _config.MaxCommandBytes
        };
    }

    private async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var buffer = new char[4096];
        while (!reader.EndOfStream && sb.Length < _config.MaxCommandBytes)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
            {
                break;
            }
            var remaining = _config.MaxCommandBytes - sb.Length;
            sb.Append(buffer, 0, Math.Min(read, remaining));
        }
        return sb.ToString();
    }
}
