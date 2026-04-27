using System.Text;
using System.Text.Json.Nodes;
using LswAgent.Rpc;
using LswAgent.Service;

namespace LswAgent.Capabilities;

/// <summary>
/// ConPTY-backed interactive shell session methods.
///
/// open_conpty_shell({ shell, cwd, env, cols, rows }) -> { session_id }
/// conpty_write({ session_id, data_base64 }) -> { ok }
/// conpty_read({ session_id }) -> { data_base64, eof }
/// conpty_resize({ session_id, cols, rows }) -> { ok }
/// conpty_close({ session_id }) -> { exit_code }
///
/// ConPTY is available on Windows 10 1809+ / Server 2019+. On older versions
/// the open call returns ok=false with a human-readable explanation.
///
/// NOTE: For the lsw -d interactive SSH path the guest's built-in OpenSSH
/// sshd already provides a PTY; these methods are for advanced use and
/// future lsw --conpty mode.
/// </summary>
public static class ConPtyHandler
{
    public static object Open(SessionStore sessions, JsonObject? @params)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return new
            {
                ok      = false,
                details = "ConPTY requires Windows 10 1809 (build 17763) or later.",
            };

        var shell = @params?["shell"]?.GetValue<string>() ?? "powershell.exe";
        var cwd   = @params?["cwd"]?.GetValue<string>();
        short cols = (short)(@params?["cols"]?.GetValue<int>() ?? 80);
        short rows = (short)(@params?["rows"]?.GetValue<int>() ?? 24);

        var sessionId = Guid.NewGuid().ToString("N");
        var session   = new ConPtySession(sessionId, shell);

        // Build enriched command line: set working directory inside shell startup
        string commandLine = BuildCommandLine(shell, cwd, @params?["env"]?.AsObject());

        bool started = session.Start(commandLine, cwd, cols, rows);
        if (!started)
        {
            return new
            {
                ok      = false,
                details = "ConPTY session failed to start (PInvoke not yet fully wired; "
                        + "see guest/lsw-agent/Service/SessionStore.cs for implementation notes).",
            };
        }

        sessions.TryAdd(sessionId, session);

        return new { session_id = sessionId };
    }

    public static object Write(SessionStore sessions, JsonObject? @params)
    {
        var id   = GetSessionId(@params);
        var data = @params?["data_base64"]?.GetValue<string>()
                   ?? throw new RpcException(-32602, "'data_base64' required");

        if (!sessions.TryGet(id, out var session) || session == null)
            throw new RpcException(-32004, $"session '{id}' not found");

        byte[] bytes = Convert.FromBase64String(data);
        session.Write(bytes);
        return new { ok = true };
    }

    public static object Read(SessionStore sessions, JsonObject? @params)
    {
        var id = GetSessionId(@params);

        if (!sessions.TryGet(id, out var session) || session == null)
            throw new RpcException(-32004, $"session '{id}' not found");

        byte[] data = session.ReadAvailable();
        return new
        {
            data_base64 = Convert.ToBase64String(data),
            eof         = session.IsEof,
        };
    }

    public static object Resize(SessionStore sessions, JsonObject? @params)
    {
        var id   = GetSessionId(@params);
        short cols = (short)(@params?["cols"]?.GetValue<int>() ?? 80);
        short rows = (short)(@params?["rows"]?.GetValue<int>() ?? 24);

        if (!sessions.TryGet(id, out var session) || session == null)
            throw new RpcException(-32004, $"session '{id}' not found");

        session.Resize(cols, rows);
        return new { ok = true };
    }

    public static object Close(SessionStore sessions, JsonObject? @params)
    {
        var id = GetSessionId(@params);

        if (!sessions.TryRemove(id, out var session) || session == null)
            throw new RpcException(-32004, $"session '{id}' not found");

        int exitCode = session.Close();
        session.Dispose();
        return new { exit_code = exitCode };
    }

    // ---- helpers ---------------------------------------------------------------

    private static string GetSessionId(JsonObject? @params) =>
        @params?["session_id"]?.GetValue<string>()
        ?? throw new RpcException(-32602, "'session_id' required");

    private static string BuildCommandLine(string shell, string? cwd, JsonObject? env)
    {
        var sb = new StringBuilder(shell);

        if (shell.StartsWith("powershell", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" -NoLogo -NoExit");
            if (!string.IsNullOrWhiteSpace(cwd))
                sb.Append($" -Command \"Set-Location -LiteralPath '{cwd}'\"");
        }
        // cmd.exe needs no special args for interactive use

        return sb.ToString();
    }
}
