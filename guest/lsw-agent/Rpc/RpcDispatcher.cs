using System.Text.Json;
using System.Text.Json.Nodes;
using LswAgent.Capabilities;
using LswAgent.Service;

namespace LswAgent.Rpc;

/// <summary>
/// Routes incoming JSON-RPC 2.0 requests to capability handlers.
/// </summary>
public sealed class RpcDispatcher
{
    private readonly AgentState _state;
    private readonly SessionStore _sessions;
    private readonly ILogger<RpcDispatcher> _logger;

    public RpcDispatcher(AgentState state, SessionStore sessions, ILogger<RpcDispatcher> logger)
    {
        _state    = state;
        _sessions = sessions;
        _logger   = logger;
    }

    public async Task<string> DispatchAsync(string rawJson, CancellationToken ct = default)
    {
        JsonObject? req = null;
        object? id = null;
        try
        {
            req = JsonNode.Parse(rawJson)?.AsObject()
                  ?? throw new JsonException("null");
            id  = ParseId(req);

            var method = req["method"]?.GetValue<string>()
                         ?? throw new RpcException(-32600, "method required");
            var @params = req["params"]?.AsObject();

            // handshake is the only method allowed before auth
            if (method != "handshake" && !_state.IsAuthenticated)
                throw new RpcException(-32001, "not authenticated");

            var result = method switch
            {
                "handshake"         => HandshakeHandler.Handle(_state, @params),
                "ensure_ssh"        => await SshHandler.HandleAsync(@params, _logger, ct),
                "mount_share"       => await MountHandler.HandleAsync(@params, _logger, ct),
                "run_cmd_shell"     => await CmdHandler.HandleAsync(@params, _logger, ct),
                "open_conpty_shell" => ConPtyHandler.Open(_sessions, @params),
                "conpty_write"      => ConPtyHandler.Write(_sessions, @params),
                "conpty_read"       => ConPtyHandler.Read(_sessions, @params),
                "conpty_resize"     => ConPtyHandler.Resize(_sessions, @params),
                "conpty_close"      => ConPtyHandler.Close(_sessions, @params),
                _                   => throw new RpcException(-32601, $"unknown method '{method}'"),
            };

            return SuccessResponse(id, result);
        }
        catch (RpcException rex)
        {
            _logger.LogWarning("RPC error {Code}: {Message}", rex.Code, rex.Message);
            return ErrorResponse(id, rex.Code, rex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "unhandled dispatch error");
            return ErrorResponse(id, -32603, "internal error");
        }
    }

    // ---- helpers ----------------------------------------------------------------

    private static object? ParseId(JsonObject req)
    {
        var idNode = req["id"];
        if (idNode is null) return null;
        if (idNode is JsonValue jv)
        {
            if (jv.TryGetValue<long>(out var l)) return l;
            if (jv.TryGetValue<string>(out var s)) return s;
        }
        return null;
    }

    private static string SuccessResponse(object? id, object? result)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"]      = id is long l ? JsonValue.Create(l) : id is string s ? JsonValue.Create(s) : null,
            ["result"]  = result is null ? null : JsonSerializer.SerializeToNode(result),
        };
        return obj.ToJsonString();
    }

    private static string ErrorResponse(object? id, int code, string message)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"]      = id is long l ? JsonValue.Create(l) : id is string s ? JsonValue.Create(s) : null,
            ["error"]   = new JsonObject
            {
                ["code"]    = code,
                ["message"] = message,
            },
        };
        return obj.ToJsonString();
    }
}

public sealed class RpcException : Exception
{
    public int Code { get; }
    public RpcException(int code, string message) : base(message) => Code = code;
}
