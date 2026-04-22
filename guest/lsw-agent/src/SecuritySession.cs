using Microsoft.Extensions.Logging;

namespace Lsw.Agent;

public sealed class SecuritySession
{
    private readonly ILogger<SecuritySession> _logger;
    private string? _currentToken;
    private bool _authenticated;

    public SecuritySession(ILogger<SecuritySession> logger)
    {
        _logger = logger;
    }

    public object Handshake(string token, string agentVersion)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new { ok = false, reason = "empty token" };
        }

        if (_currentToken is null)
        {
            _currentToken = token;
            _authenticated = true;
            return CapabilityResponse(agentVersion);
        }

        if (!string.Equals(_currentToken, token, StringComparison.Ordinal))
        {
            _logger.LogInformation("Token rotation detected; invalidating previous session.");
            _currentToken = token;
        }

        _authenticated = true;
        return CapabilityResponse(agentVersion);
    }

    public void RequireAuthenticated()
    {
        if (!_authenticated)
        {
            throw new UnauthorizedAccessException("handshake required");
        }
    }

    private static object CapabilityResponse(string version) => new
    {
        ok = true,
        agent_version = version,
        capabilities = new[]
        {
            "handshake",
            "ensure_ssh",
            "mount_share",
            "run_cmd_shell",
            "open_conpty_shell",
            "conpty_write",
            "conpty_read",
            "conpty_resize",
            "conpty_close"
        }
    };
}
