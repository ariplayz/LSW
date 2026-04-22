using System.Text.Json;

namespace Lsw.Agent;

public sealed class JsonRpcDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AgentConfig _config;
    private readonly SecuritySession _securitySession;
    private readonly CommandRunner _commandRunner;
    private readonly SshConfigurator _sshConfigurator;
    private readonly ShareMounter _shareMounter;

    public JsonRpcDispatcher(
        AgentConfig config,
        SecuritySession securitySession,
        CommandRunner commandRunner,
        SshConfigurator sshConfigurator,
        ShareMounter shareMounter)
    {
        _config = config;
        _securitySession = securitySession;
        _commandRunner = commandRunner;
        _sshConfigurator = sshConfigurator;
        _shareMounter = shareMounter;
    }

    public async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        try
        {
            object result = request.Method switch
            {
                "handshake" => Handshake(request.Params),
                "ensure_ssh" => await EnsureSshAsync(request.Params, cancellationToken),
                "mount_share" => await MountShareAsync(request.Params, cancellationToken),
                "run_cmd_shell" => await RunCmdShellAsync(request.Params, cancellationToken),
                "open_conpty_shell" => NotImplemented("conpty scaffolding only in MVP"),
                "conpty_write" => NotImplemented("conpty scaffolding only in MVP"),
                "conpty_read" => NotImplemented("conpty scaffolding only in MVP"),
                "conpty_resize" => NotImplemented("conpty scaffolding only in MVP"),
                "conpty_close" => NotImplemented("conpty scaffolding only in MVP"),
                _ => throw new InvalidOperationException($"unknown method: {request.Method}")
            };

            return new JsonRpcResponse("2.0", request.Id, JsonSerializer.SerializeToElement(result, JsonOptions), null);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Error(request.Id, -32001, ex.Message);
        }
        catch (Exception ex)
        {
            return Error(request.Id, -32000, ex.Message);
        }
    }

    private object Handshake(JsonElement @params)
    {
        var parsed = JsonSerializer.Deserialize<HandshakeParams>(@params.GetRawText(), JsonOptions)
                     ?? throw new InvalidOperationException("invalid handshake params");
        return _securitySession.Handshake(parsed.Token, _config.AgentVersion);
    }

    private async Task<object> EnsureSshAsync(JsonElement @params, CancellationToken cancellationToken)
    {
        _securitySession.RequireAuthenticated();
        var parsed = JsonSerializer.Deserialize<EnsureSshParams>(@params.GetRawText(), JsonOptions)
                     ?? throw new InvalidOperationException("invalid ensure_ssh params");
        return await _sshConfigurator.EnsureSshAsync(parsed, cancellationToken);
    }

    private async Task<object> MountShareAsync(JsonElement @params, CancellationToken cancellationToken)
    {
        _securitySession.RequireAuthenticated();
        var parsed = JsonSerializer.Deserialize<MountShareParams>(@params.GetRawText(), JsonOptions)
                     ?? throw new InvalidOperationException("invalid mount_share params");
        return await _shareMounter.MountAsync(parsed, cancellationToken);
    }

    private async Task<object> RunCmdShellAsync(JsonElement @params, CancellationToken cancellationToken)
    {
        _securitySession.RequireAuthenticated();
        var parsed = JsonSerializer.Deserialize<RunCmdShellParams>(@params.GetRawText(), JsonOptions)
                     ?? throw new InvalidOperationException("invalid run_cmd_shell params");
        return await _commandRunner.RunShellAsync(parsed, cancellationToken);
    }

    private static object NotImplemented(string details) => new { ok = false, details };

    private static JsonRpcResponse Error(JsonElement id, int code, string message)
        => new("2.0", id, null, new JsonRpcError(code, message));
}
