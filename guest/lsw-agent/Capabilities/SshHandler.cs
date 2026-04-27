using System.Text.Json.Nodes;
using LswAgent.Rpc;

namespace LswAgent.Capabilities;

/// <summary>
/// ensure_ssh({ public_key, username, allow_password }) -> { ok, details }
///
/// Installs/enables OpenSSH Server on Windows 10/11, injects the host
/// public key into the correct authorized_keys file, and sets sshd to auto-start.
///
/// Windows 7: not supported automatically; method returns ok=false with guidance.
/// </summary>
public static class SshHandler
{
    private const string OpenSshCapability = "OpenSSH.Server~~~~0.0.1.0";

    public static async Task<object> HandleAsync(
        JsonObject? @params,
        ILogger logger,
        CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return new { ok = false, details = "Not running on Windows" };

        var publicKey = @params?["public_key"]?.GetValue<string>()
                        ?? throw new RpcException(-32602, "'public_key' param required");
        var username = @params?["username"]?.GetValue<string>() ?? "Administrator";
        bool allowPassword = @params?["allow_password"]?.GetValue<bool>() ?? false;

        // Windows 7 / very old builds: built-in OpenSSH is not available.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return new
            {
                ok      = false,
                details = "OpenSSH built-in capability requires Windows 10 1809 (build 17763) or later. "
                        + "For Windows 7/8/earlier Windows 10: install Win32-OpenSSH manually from "
                        + "https://github.com/PowerShell/Win32-OpenSSH/releases then re-run ensure_ssh.",
            };
        }

        var errors = new List<string>();

        // 1. Install OpenSSH Server capability if needed
        await EnsureOpenSshInstalledAsync(logger, errors, ct);

        // 2. Ensure sshd service is running and auto-starts
        EnsureSshdService(logger, errors);

        // 3. Ensure firewall rule exists (for guest NIC; host reaches via NAT)
        EnsureFirewallRule(logger, errors);

        // 4. Inject authorized key
        InjectAuthorizedKey(username, publicKey, allowPassword, logger, errors);

        if (errors.Count > 0)
            return new { ok = false, details = string.Join("; ", errors) };

        return new
        {
            ok      = true,
            details = $"OpenSSH configured for user '{username}'. "
                    + "Host can now connect via: ssh -p <forwarded-port> "
                    + $"{username}@127.0.0.1",
        };
    }

    // ---- helpers ---------------------------------------------------------------

    private static async Task EnsureOpenSshInstalledAsync(
        ILogger logger, List<string> errors, CancellationToken ct)
    {
        logger.LogInformation("Checking OpenSSH Server capability");
        // Use DISM via PowerShell to install capability (requires admin)
        var ps = await RunPsAsync(
            $"Get-WindowsCapability -Online -Name '{OpenSshCapability}' | Select-Object -ExpandProperty State",
            ct);

        if (ps.ExitCode != 0 || !ps.Stdout.Contains("Installed", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Installing OpenSSH Server capability");
            var install = await RunPsAsync(
                $"Add-WindowsCapability -Online -Name '{OpenSshCapability}'", ct);
            if (install.ExitCode != 0)
                errors.Add($"OpenSSH install failed: {install.Stderr}");
        }
    }

    private static void EnsureSshdService(ILogger logger, List<string> errors)
    {
        logger.LogInformation("Configuring sshd service");
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("sshd");
            if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
            {
                sc.Start();
                sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running,
                    TimeSpan.FromSeconds(30));
            }
            // Set to auto-start via registry (ServiceController doesn't expose StartType setter in .NET 6)
            SetServiceStartType("sshd", "Automatic", errors);
        }
        catch (Exception ex)
        {
            errors.Add($"sshd service error: {ex.Message}");
        }
    }

    private static void SetServiceStartType(string name, string startType, List<string> errors)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}", writable: true);
            if (key != null)
                key.SetValue("Start", startType == "Automatic" ? 2 : 3, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            errors.Add($"set service start type: {ex.Message}");
        }
    }

    private static void EnsureFirewallRule(ILogger logger, List<string> errors)
    {
        // Only needed when sshd binds to all interfaces; with QEMU NAT the host
        // reaches the guest on the internal NAT network but Windows Firewall may
        // still block inbound 22.
        logger.LogInformation("Ensuring firewall rule for SSH");
        // Check rule with netsh (available on all supported Windows versions)
        var result = RunCmd("netsh", "advfirewall firewall show rule name=\"LSW-SSH\"");
        if (result.ExitCode != 0)
        {
            var add = RunCmd("netsh",
                "advfirewall firewall add rule name=\"LSW-SSH\" " +
                "protocol=TCP dir=in localport=22 action=allow");
            if (add.ExitCode != 0)
                errors.Add($"firewall rule add failed: {add.Stderr}");
        }
    }

    private static void InjectAuthorizedKey(
        string username, string publicKey, bool allowPassword,
        ILogger logger, List<string> errors)
    {
        logger.LogInformation("Injecting authorized key for {User}", username);

        // For the Administrator account OpenSSH uses a global file;
        // for other users it's %USERPROFILE%\.ssh\authorized_keys
        string sshDir;
        string authKeysPath;

        if (string.Equals(username, "Administrator", StringComparison.OrdinalIgnoreCase))
        {
            sshDir       = @"C:\ProgramData\ssh";
            authKeysPath = Path.Combine(sshDir, "administrators_authorized_keys");
        }
        else
        {
            // Look up user profile via registry
            var profile = GetUserProfile(username) ?? $@"C:\Users\{username}";
            sshDir       = Path.Combine(profile, ".ssh");
            authKeysPath = Path.Combine(sshDir, "authorized_keys");
        }

        try
        {
            Directory.CreateDirectory(sshDir);

            // Append key if not already present
            var existing = File.Exists(authKeysPath) ? File.ReadAllText(authKeysPath) : "";
            if (!existing.Contains(publicKey.Trim()))
            {
                File.AppendAllText(authKeysPath, publicKey.Trim() + Environment.NewLine);
                logger.LogInformation("Key appended to {Path}", authKeysPath);
            }

            // Fix ACLs: OpenSSH requires the file to be owned by the user or Administrators
            // and not writable by other users.
            FixAuthorizedKeysAcl(authKeysPath, username, errors);

            // Optionally disable password auth in sshd_config
            if (!allowPassword)
                DisablePasswordAuth(errors);
        }
        catch (Exception ex)
        {
            errors.Add($"key injection failed: {ex.Message}");
        }
    }

    private static void FixAuthorizedKeysAcl(string path, string username, List<string> errors)
    {
        try
        {
            // Use icacls to set correct permissions (simpler than System.Security.AccessControl)
            var r = RunCmd("icacls",
                $"\"{path}\" /inheritance:r " +
                $"/grant:r \"{username}:(F)\" " +
                "/grant:r \"SYSTEM:(F)\"");
            if (r.ExitCode != 0)
                errors.Add($"icacls failed: {r.Stderr}");
        }
        catch (Exception ex)
        {
            errors.Add($"ACL fix error: {ex.Message}");
        }
    }

    private static void DisablePasswordAuth(List<string> errors)
    {
        const string sshdConfig = @"C:\ProgramData\ssh\sshd_config";
        try
        {
            if (!File.Exists(sshdConfig)) return;
            var lines = File.ReadAllLines(sshdConfig).ToList();
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith("PasswordAuthentication", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "PasswordAuthentication no";
                    found = true;
                }
            }
            if (!found)
                lines.Add("PasswordAuthentication no");
            File.WriteAllLines(sshdConfig, lines);

            // Restart sshd to pick up new config
            RunCmd("net", "stop sshd");
            RunCmd("net", "start sshd");
        }
        catch (Exception ex)
        {
            errors.Add($"disable password auth: {ex.Message}");
        }
    }

    private static string? GetUserProfile(string username)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            if (key == null) return null;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var profileKey = key.OpenSubKey(sub);
                var img = profileKey?.GetValue("ProfileImagePath") as string;
                if (img != null && img.EndsWith(username, StringComparison.OrdinalIgnoreCase))
                    return img;
            }
        }
        catch { }
        return null;
    }

    // ---- process helpers -------------------------------------------------------

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunPsAsync(
        string script, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = "powershell.exe",
            Arguments              = $"-NonInteractive -Command \"{script.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        string stdout = await p.StandardOutput.ReadToEndAsync();
        string stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, stdout, stderr);
    }

    private static (int ExitCode, string Stdout, string Stderr) RunCmd(string exe, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = exe,
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }
}
