namespace kasthack.ApostlePeter.ServicesImpl;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using kasthack.ApostlePeter.Configuration;
using kasthack.ApostlePeter.Models.Vk;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class ConsoleTestService : IHostedService
{
    private readonly ILogger<ConsoleTestService> logger;
    private readonly WhitewashingService whitewashingService;
    private readonly IHostApplicationLifetime hostApplicationLifetime;
    private readonly ConfigurationOptions options;

    public ConsoleTestService(
        ILogger<ConsoleTestService> logger,
        IOptions<ConfigurationOptions> options,
        IHostApplicationLifetime hostApplicationLifetime,
        WhitewashingService whitewashingService)
    {
        this.logger = logger;
        this.whitewashingService = whitewashingService;
        this.hostApplicationLifetime = hostApplicationLifetime;
        this.options = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var appId = this.options.Vk.AppId;
        var privileges = this.options.Vk.Privileges;
        var redirectUri = this.options.Vk.RedirectUri;
        var oauthUrl = Token.GetOauthUrl(appId, privileges, redirectUri);
        this.logger.LogInformation("Open oauth page: {oauthUrl}", oauthUrl);
        this.logger.LogInformation("Enter token url: ");
        var token = Token.FromRedirectUrl(Console.ReadLine()!).Value;

        this.logger.LogInformation("Enter target id: ");
        var id = int.Parse(Console.ReadLine()!);

        var result = await this.whitewashingService.CheckAccount(id, token, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
        this.hostApplicationLifetime.StopApplication();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        this.hostApplicationLifetime.StopApplication();
        return Task.CompletedTask;
    }
}