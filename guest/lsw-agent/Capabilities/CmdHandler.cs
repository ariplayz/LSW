using System.Text;
using System.Text.Json.Nodes;
using LswAgent.Rpc;

namespace LswAgent.Capabilities;

/// <summary>
/// run_cmd_shell({ shell, cmd, cwd, env }) -> { exit_code, stdout, stderr }
///
/// Executes a non-interactive command via the specified shell and returns
/// stdout/stderr and exit code. Large outputs are safely buffered with a
/// configurable cap to avoid memory exhaustion.
/// </summary>
public static class CmdHandler
{
    private const int MaxOutputBytes = 16 * 1024 * 1024; // 16 MiB per stream

    public static async Task<object> HandleAsync(
        JsonObject? @params,
        ILogger logger,
        CancellationToken ct)
    {
        var shell = @params?["shell"]?.GetValue<string>() ?? "powershell.exe";
        var cmd   = @params?["cmd"]?.GetValue<string>()
                    ?? throw new RpcException(-32602, "'cmd' param required");
        var cwd   = @params?["cwd"]?.GetValue<string>();
        var envNode = @params?["env"]?.AsObject();

        // Validate shell name (no path traversal)
        if (shell.Contains('/') || shell.Contains('\\'))
            throw new RpcException(-32602, "shell must be a binary name, not a path");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = shell,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };

        // For PowerShell pass cmd as -Command
        if (shell.StartsWith("powershell", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(cmd);
        }
        else if (shell.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(cmd);
        }
        else
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(cmd);
        }

        if (!string.IsNullOrWhiteSpace(cwd))
            psi.WorkingDirectory = cwd;

        if (envNode != null)
        {
            foreach (var kv in envNode)
            {
                if (kv.Value != null)
                    psi.Environment[kv.Key] = kv.Value.GetValue<string>();
            }
        }

        logger.LogInformation("run_cmd_shell [{Shell}] cwd={Cwd}", shell, cwd ?? "(default)");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Default timeout: 5 minutes
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        using var proc = System.Diagnostics.Process.Start(psi)
                         ?? throw new RpcException(-32603, "failed to start process");

        // Drain stdout/stderr concurrently using bounded buffers to avoid deadlock
        var stdoutTask = DrainAsync(proc.StandardOutput, MaxOutputBytes, cts.Token);
        var stderrTask = DrainAsync(proc.StandardError,  MaxOutputBytes, cts.Token);

        await proc.WaitForExitAsync(cts.Token);

        string stdoutText = await stdoutTask;
        string stderrText = await stderrTask;

        logger.LogInformation("run_cmd_shell exit={Exit}", proc.ExitCode);

        return new
        {
            exit_code = proc.ExitCode,
            stdout    = stdoutText,
            stderr    = stderrText,
        };
    }

    private static async Task<string> DrainAsync(
        System.IO.TextReader reader, int maxBytes, CancellationToken ct)
    {
        var sb   = new StringBuilder();
        var buf  = new char[4096];
        int total = 0;

        while (true)
        {
            int read = await reader.ReadAsync(buf.AsMemory(), ct);
            if (read == 0) break;

            total += read * 2; // UTF-16 char = 2 bytes in memory
            if (total > maxBytes)
            {
                sb.Append(buf, 0, read);
                sb.Append($"\n[LSW: output truncated at {maxBytes / 1024} KiB]");
                break;
            }

            sb.Append(buf, 0, read);
        }

        return sb.ToString();
    }
}
