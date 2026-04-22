using System.Text.Json;

namespace Lsw.Agent;

public sealed record JsonRpcRequest(
    string Jsonrpc,
    JsonElement Id,
    string Method,
    JsonElement Params);

public sealed record JsonRpcResponse(
    string Jsonrpc,
    JsonElement Id,
    JsonElement? Result,
    JsonRpcError? Error);

public sealed record JsonRpcError(
    int Code,
    string Message,
    object? Data = null);

public sealed record HandshakeParams(string Token);
public sealed record EnsureSshParams(string PublicKey, string Username, bool AllowPassword);
public sealed record MountShareParams(string Backend, string TagOrUnc, string GuestPath, string Mode, string Username);
public sealed record RunCmdShellParams(string Shell, string Cmd, string? Cwd, Dictionary<string, string>? Env);
