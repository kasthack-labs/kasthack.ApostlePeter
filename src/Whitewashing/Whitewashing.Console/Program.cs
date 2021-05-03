namespace Whitewashing.Console
{
    using System;
    using System.Threading.Tasks;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    using Serilog;

    using Whitewashing.Console.ServicesImpl;

    class Program
    {
        /// <summary>
        /// Whitewashing bot
        /// </summary>
        private static async Task Main() => await
            Host
                .CreateDefaultBuilder()
                .ConfigureHostConfiguration(configHost => configHost.AddEnvironmentVariables())
                .ConfigureAppConfiguration((ctx, configuration) =>
                    configuration
                        .SetBasePath(Environment.CurrentDirectory)
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                        .AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                        .AddEnvironmentVariables()
                        .AddUserSecrets<Program>())
                .ConfigureServices((ctx, services) =>
                {
                    var keySection = ctx.Configuration.GetSection("Whitewasher");
                    services
                        .AddOptions()
                            .Configure<Configuration>(keySection);

                    services
                        .AddLogging(builder =>
                        {
                            Log.Logger = new LoggerConfiguration()
                                .ReadFrom.Configuration(ctx.Configuration)
                                .CreateLogger();
                            builder
                                .ClearProviders()
                                .AddSerilog(dispose: true);
                        })
                        .AddSingleton<VkFetcherService>()
                        .AddTransient<WhitewashingService>()
                        .AddTransient<BotService>()
                        .AddHostedService<BotService>() //ConsoleTestService
                                                        //.AddSingleton(new DownloaderOptions(OutputDir, customFeed, Action, Version))
                        .AddHttpClient();
                })
                .RunConsoleAsync()
                .ConfigureAwait(false);
    }
}