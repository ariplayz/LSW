using LswAgent.Transport;
using LswAgent.Service;
using Microsoft.Extensions.Logging.EventLog;

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "LswAgent";
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        // On Windows 10/11, also log to Event Log when running as service
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            logging.AddEventLog(new EventLogSettings
            {
                SourceName = "LswAgent",
                LogName    = "Application",
            });
        }
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<SessionStore>();
        services.AddSingleton<AgentState>();
        services.AddSingleton<RpcDispatcher>();
        services.AddHostedService<VirtioSerialWorker>();
    })
    .Build();

await host.RunAsync();
