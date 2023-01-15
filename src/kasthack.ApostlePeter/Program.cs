namespace kasthack.ApostlePeter;

using System;
using System.Threading.Tasks;

using kasthack.ApostlePeter.Configuration;
using kasthack.ApostlePeter.ServicesImpl;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

internal class Program
{
    /// <summary>
    /// Whitewashing bot.
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
                var keySection = ctx.Configuration.GetSection("ApostlePeter");
                _ = services
                    .AddOptions()
                        .Configure<ConfigurationOptions>(keySection);

                _ = services
                    .AddLogging(builder =>
                    {
                        Log.Logger = new LoggerConfiguration()
                            .ReadFrom.Configuration(ctx.Configuration)
                            .CreateLogger();
                        _ = builder
                            .ClearProviders()
                            .AddSerilog(dispose: true);
                    })
                    .AddSingleton<VkFetcherService>()
                    .AddTransient<WhitewashingService>()
                    .AddTransient<BotService>()
                    .AddHostedService<BotService>()
                    .AddHttpClient();
            })
            .RunConsoleAsync()
            .ConfigureAwait(false);
}