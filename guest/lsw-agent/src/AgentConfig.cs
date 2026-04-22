namespace Lsw.Agent;

public sealed class AgentConfig
{
    public string AgentVersion { get; } = "0.1.0";
    public string ChannelName { get; } = "org.lsw.agent";
    public string SerialPortOverride => Environment.GetEnvironmentVariable("LSW_AGENT_SERIAL_PORT") ?? string.Empty;
    public int MaxCommandBytes { get; } = 1024 * 1024;
    public TimeSpan CommandTimeout { get; } = TimeSpan.FromMinutes(5);
}
