using System.Text.Json.Nodes;
using LswAgent.Rpc;

namespace LswAgent.Capabilities;

/// <summary>
/// mount_share({ backend, tag_or_unc, guest_path, mode }) -> { ok, details }
///
/// MVP: SMB network-drive mapping to D:\home\&lt;username&gt;\.
/// virtio-fs/9p documented as future work.
/// </summary>
public static class MountHandler
{
    public static async Task<object> HandleAsync(
        JsonObject? @params,
        ILogger logger,
        CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return new { ok = false, details = "Not running on Windows" };

        var backend   = @params?["backend"]?.GetValue<string>()?.ToLowerInvariant()
                        ?? throw new RpcException(-32602, "'backend' param required");
        var tagOrUnc  = @params?["tag_or_unc"]?.GetValue<string>()
                        ?? throw new RpcException(-32602, "'tag_or_unc' param required");
        var guestPath = @params?["guest_path"]?.GetValue<string>()
                        ?? throw new RpcException(-32602, "'guest_path' param required");
        var mode      = @params?["mode"]?.GetValue<string>() ?? "rw";
        var smbUser   = @params?["smb_user"]?.GetValue<string>();
        var smbPass   = @params?["smb_pass"]?.GetValue<string>();

        // Security: only allow paths under D:\home\
        if (!guestPath.StartsWith(@"D:\home\", StringComparison.OrdinalIgnoreCase))
            throw new RpcException(-32002, "guest_path must be under D:\\home\\");

        return backend switch
        {
            "smb"      => await MountSmbAsync(tagOrUnc, guestPath, mode, smbUser, smbPass, logger, ct),
            "virtio-fs"=> VirtioFsFuture(),
            "9p"       => NinePFuture(),
            _          => throw new RpcException(-32602, $"unsupported backend '{backend}'"),
        };
    }

    // ---- SMB -------------------------------------------------------------------

    private static async Task<object> MountSmbAsync(
        string unc, string guestPath, string mode,
        string? smbUser, string? smbPass,
        ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("Mounting SMB {Unc} -> {GuestPath}", unc, guestPath);

        // Create the target directory
        Directory.CreateDirectory(guestPath);

        // Build net use command
        // net use D:\home\alice \\10.0.2.2\lsw_home /user:... password /persistent:yes
        // We use the directory junction approach via mklink or subst if drive mapping is unavailable,
        // but net use to a path is not directly supported. We use a drive letter intermediary.

        // Try symbolic-link / junction based mount:
        //   1. Map \\unc to a temp drive letter
        //   2. Create a directory junction from guestPath -> that drive
        // Simpler: map unc to subpath using "net use * \\unc" then junction.
        // For MVP: directly use pushd / net use to assign and symlink.

        var errors = new List<string>();

        // Step 1: map UNC to a drive letter
        string? driveLetter = FindFreeDriveLetter();
        if (driveLetter == null)
            return new { ok = false, details = "No free drive letters available" };

        string netUseArgs = BuildNetUseArgs(driveLetter, unc, smbUser, smbPass,
            persistent: true, readOnly: mode == "ro");

        var (exit, _, stderr) = await RunCmdAsync("net", netUseArgs, ct);
        if (exit != 0)
        {
            // Maybe already mapped
            logger.LogWarning("net use failed ({Exit}): {Err}", exit, stderr);
            errors.Add($"net use: {stderr.Trim()}");
        }

        // Step 2: create junction from guestPath -> drive letter root
        // If guestPath already exists as a directory (created above), remove it
        // and replace with a junction.
        if (Directory.Exists(guestPath) && !IsJunction(guestPath))
        {
            try { Directory.Delete(guestPath); } catch { }
        }

        if (!IsJunction(guestPath))
        {
            var (jExit, _, jErr) = await RunCmdAsync(
                "cmd", $"/c mklink /J \"{guestPath}\" \"{driveLetter}\\\"", ct);
            if (jExit != 0)
                errors.Add($"mklink junction: {jErr.Trim()}");
        }

        if (errors.Count > 0)
            return new { ok = false, details = string.Join("; ", errors) };

        return new
        {
            ok      = true,
            details = $"SMB share {unc} mounted at {guestPath} (via {driveLetter})",
        };
    }

    private static string BuildNetUseArgs(
        string drive, string unc, string? user, string? pass,
        bool persistent, bool readOnly)
    {
        var sb = new System.Text.StringBuilder($"use {drive}: \"{unc}\"");
        if (!string.IsNullOrWhiteSpace(pass)) sb.Append($" \"{pass}\"");
        if (!string.IsNullOrWhiteSpace(user)) sb.Append($" /user:\"{user}\"");
        sb.Append(persistent ? " /persistent:yes" : " /persistent:no");
        return sb.ToString();
    }

    private static string? FindFreeDriveLetter()
    {
        var used = DriveInfo.GetDrives()
            .Select(d => d.Name[0])
            .ToHashSet();
        // Prefer letters E-Z
        for (char c = 'E'; c <= 'Z'; c++)
        {
            if (!used.Contains(c)) return c.ToString();
        }
        return null;
    }

    private static bool IsJunction(string path)
    {
        if (!Directory.Exists(path)) return false;
        var di = new System.IO.DirectoryInfo(path);
        return (di.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    // ---- not yet supported -----------------------------------------------------

    private static object VirtioFsFuture() =>
        new
        {
            ok      = false,
            details = "virtio-fs backend is not yet supported on Windows guests (MVP). "
                    + "A Windows virtio-fs driver exists experimentally at "
                    + "https://github.com/virtio-win/kvm-guest-drivers-windows but requires "
                    + "manual installation. Use backend='smb' for the MVP.",
        };

    private static object NinePFuture() =>
        new
        {
            ok      = false,
            details = "9p backend has no stable Windows support in the MVP. "
                    + "Use backend='smb'.",
        };

    // ---- process helpers -------------------------------------------------------

    private static async Task<(int Exit, string Out, string Err)> RunCmdAsync(
        string exe, string args, CancellationToken ct)
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
        string o = await p.StandardOutput.ReadToEndAsync();
        string e = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, o, e);
    }
}
