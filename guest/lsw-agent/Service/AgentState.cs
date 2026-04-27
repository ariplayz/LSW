namespace LswAgent.Service;

/// <summary>
/// Shared mutable state for the agent: auth status and capability flags.
/// Thread-safe via volatile fields and a constant-time compare.
/// </summary>
public sealed class AgentState
{
    private volatile string? _sessionToken;
    private volatile bool _authenticated;

    public string AgentVersion { get; } = "0.1.0";

    public IReadOnlyList<string> Capabilities { get; } = new[]
    {
        "handshake",
        "ensure_ssh",
        "mount_share",
        "run_cmd_shell",
        "open_conpty_shell",
        "conpty_write",
        "conpty_read",
        "conpty_resize",
        "conpty_close",
    };

    /// <summary>True after a successful handshake.</summary>
    public bool IsAuthenticated => _authenticated;

    /// <summary>
    /// Validate the supplied token and establish a session.
    /// On first call (no prior token) any non-empty token is accepted and stored.
    /// On subsequent calls the token must match the stored value.
    /// </summary>
    public bool TryAuthenticate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (_sessionToken == null)
        {
            _sessionToken  = token;
            _authenticated = true;
            return true;
        }

        if (CryptographicEquals(token, _sessionToken))
        {
            _authenticated = true;
            return true;
        }

        return false;
    }

    /// <summary>Rotate token: next handshake with new token re-authenticates.</summary>
    public void RotateToken(string newToken)
    {
        _sessionToken  = newToken;
        _authenticated = false;
    }

    /// Constant-time string compare to avoid timing side-channels.
    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
