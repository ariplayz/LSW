using System.Diagnostics;

namespace Lsw.Agent;

public sealed class SshConfigurator
{
    public async Task<object> EnsureSshAsync(EnsureSshParams request, CancellationToken cancellationToken)
    {
        if (request.AllowPassword)
        {
            return new { ok = false, details = "allow_password=true is not supported by policy" };
        }

        var version = Environment.OSVersion.Version;
        if (version.Major < 10)
        {
            return new { ok = false, details = "Windows 7 requires manual Win32-OpenSSH installation" };
        }

        var capability = await RunPsAsync(
            "Get-WindowsCapability -Online -Name OpenSSH.Server* | Select-Object -First 1 -ExpandProperty State",
            cancellationToken);

        if (!capability.stdout.Contains("Installed", StringComparison.OrdinalIgnoreCase))
        {
            await RunPsAsync("Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0", cancellationToken);
        }

        await RunPsAsync("Set-Service -Name sshd -StartupType Automatic; Start-Service sshd", cancellationToken);
        await RunPsAsync("if (-not (Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue)) { New-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -DisplayName 'OpenSSH Server (TCP 22)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 }", cancellationToken);

        var userProfile = await RunPsAsync($"$u=Get-LocalUser -Name '{EscapeSingleQuote(request.Username)}' -ErrorAction SilentlyContinue; if (-not $u) {{ exit 4 }}; (Get-CimInstance Win32_UserProfile | Where-Object {{$_.LocalPath -like '*\\{EscapeSingleQuote(request.Username)}'}} | Select-Object -First 1 -ExpandProperty LocalPath)", cancellationToken);
        if (userProfile.exitCode == 4 || string.IsNullOrWhiteSpace(userProfile.stdout))
        {
            return new { ok = false, details = $"target user '{request.Username}' not found" };
        }

        var sshDir = Path.Combine(userProfile.stdout.Trim(), ".ssh");
        Directory.CreateDirectory(sshDir);
        var authPath = Path.Combine(sshDir, "authorized_keys");

        var key = request.PublicKey.Trim();
        var existing = File.Exists(authPath) ? await File.ReadAllTextAsync(authPath, cancellationToken) : string.Empty;
        if (!existing.Contains(key, StringComparison.Ordinal))
        {
            await File.AppendAllTextAsync(authPath, key + Environment.NewLine, cancellationToken);
        }

        await RunPsAsync($"icacls '{EscapeSingleQuote(authPath)}' /inheritance:r | Out-Null", cancellationToken);

        return new { ok = true, details = "sshd configured, key installed" };
    }

    private static string EscapeSingleQuote(string value) => value.Replace("'", "''");

    private static async Task<(int exitCode, string stdout, string stderr)> RunPsAsync(string command, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{command.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to launch powershell");
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(cancellationToken);
        return (p.ExitCode, await outTask, await errTask);
    }
}
