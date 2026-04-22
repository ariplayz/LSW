using System.Diagnostics;

namespace Lsw.Agent;

public sealed class ShareMounter
{
    public async Task<object> MountAsync(MountShareParams request, CancellationToken cancellationToken)
    {
        if (!request.Backend.Equals("smb", StringComparison.OrdinalIgnoreCase))
        {
            return new { ok = false, details = "MVP supports smb backend only" };
        }

        if (!request.GuestPath.StartsWith(@"D:\home\", StringComparison.OrdinalIgnoreCase))
        {
            return new { ok = false, details = "guest_path must be under D:\\home\\" };
        }

        Directory.CreateDirectory(request.GuestPath);

        var command = $"New-PSDrive -Name LSW_HOME -PSProvider FileSystem -Root '{request.TagOrUnc}' -Persist -ErrorAction Stop; " +
                      $"cmd /c mklink /d \"{request.GuestPath}\" \"\\\\localhost\\LSW_HOME\"";

        var result = await RunPsAsync(command, cancellationToken);
        if (result.exitCode != 0)
        {
            return new { ok = false, details = result.stderr };
        }

        return new { ok = true, details = "share mounted" };
    }

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
