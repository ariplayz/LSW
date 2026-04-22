using Lsw.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "LswAgent";
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddEventLog(settings =>
        {
            settings.SourceName = "LswAgent";
        });
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<AgentConfig>();
        services.AddSingleton<SecuritySession>();
        services.AddSingleton<CommandRunner>();
        services.AddSingleton<SshConfigurator>();
        services.AddSingleton<ShareMounter>();
        services.AddSingleton<JsonRpcDispatcher>();
        services.AddHostedService<AgentWorker>();
    });

await builder.Build().RunAsync();