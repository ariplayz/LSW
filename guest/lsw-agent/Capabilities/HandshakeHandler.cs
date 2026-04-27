using System.Text.Json.Nodes;
using LswAgent.Rpc;
using LswAgent.Service;

namespace LswAgent.Capabilities;

/// <summary>
/// Handles the handshake({ token }) -> { ok, agent_version, capabilities } RPC method.
/// </summary>
public static class HandshakeHandler
{
    public static object Handle(AgentState state, JsonObject? @params)
    {
        var token = @params?["token"]?.GetValue<string>()
                    ?? throw new RpcException(-32602, "'token' param required");

        // Check for rotation request
        var rotateToken = @params?["new_token"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(rotateToken) && state.IsAuthenticated)
        {
            state.RotateToken(rotateToken);
            // Re-authenticate with new token immediately
            if (!state.TryAuthenticate(rotateToken))
                throw new RpcException(-32002, "token rotation failed");

            return new
            {
                ok            = true,
                rotated       = true,
                agent_version = state.AgentVersion,
                capabilities  = state.Capabilities,
            };
        }

        bool ok = state.TryAuthenticate(token);
        if (!ok)
            throw new RpcException(-32003, "authentication failed");

        return new
        {
            ok            = true,
            agent_version = state.AgentVersion,
            capabilities  = state.Capabilities,
        };
    }
}
