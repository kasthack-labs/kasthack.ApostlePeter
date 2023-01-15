namespace kasthack.ApostlePeter.ServicesImpl;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using kasthack.ApostlePeter.Configuration;
using kasthack.ApostlePeter.Models.Reports;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;

using VkNet.Exception;

public partial class BotService : IHostedService, IUpdateHandler
{
    private const int MaxMessageLength = 4000;
    private const int JailMessageEasterEggThreshold = 100;
    private readonly ILogger logger;
    private readonly IOptionsMonitor<ConfigurationOptions> options;
    private readonly TelegramBotClient bot;
    private readonly VkFetcherService vkService;
    private readonly WhitewashingService whitewashingService;
    private readonly SemaphoreSlim locker = new(1);

    public BotService(
        IOptionsMonitor<ConfigurationOptions> options,
        ILogger<BotService> logger,
        VkFetcherService vkService,
        WhitewashingService whitewashingService)
    {
        this.logger = logger;
        this.options = options;
        this.bot = new TelegramBotClient(this.Options.Telegram.Token);
        this.vkService = vkService;
        this.whitewashingService = whitewashingService;
    }

    private ConfigurationOptions Options => this.options.CurrentValue;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = await this.bot.GetMeAsync(cancellationToken: cancellationToken);
        this.bot.StartReceiving(
            this,
            default,
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    Task IUpdateHandler.HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case ApiRequestException apiRequestException:
                this.logger.LogError(
                    apiRequestException,
                    "Telegram API Error:\n[{errorCode}]\n{errorMessage}",
                    apiRequestException.ErrorCode,
                    apiRequestException.Message);
                break;
            default:
                this.logger.LogError(exception, "An exception has occured");
                break;
        }

        return Task.CompletedTask;
    }

    async Task IUpdateHandler.HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        var handler = update.Type switch
        {
            UpdateType.Message => this.BotOnMessageReceived(update.Message!),
            _ => this.UnknownUpdateHandlerAsync(update),
        };

        try
        {
            await handler;
        }
        catch (Exception exception)
        {
            await ((IUpdateHandler)this).HandlePollingErrorAsync(botClient, exception, cancellationToken);
        }
    }

    [GeneratedRegex("^[\\w\\.]+$")]
    private static partial Regex DomainRegex();

    private async Task BotOnMessageReceived(Message message)
    {
        this.logger.LogDebug("Received message from {messageChatId}: {messageText}", message.Chat.Id, message.Text);
        if (message.Type != MessageType.Text)
        {
            _ = await this.bot.SendTextMessageAsync(message.Chat, Strings.AIOnlyUnderstandsText);
        }

        var action = message.Text?.Split(' ').FirstOrDefault() switch
        {
            "/start" => this.OnStart(message),
            _ => this.OnLink(message),

            // "/help" => OnHelp(message),
        };
        _ = Task.Run(async () =>
        {
            try
            {
                await action;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error handling message");
            }
        });
    }

    private async Task OnLink(Message message)
    {
        var ct = CancellationToken.None;
        var text = message.Text?.Trim();
        if (text == null)
        {
            return;
        }

        if (!text.StartsWith("http") && text.StartsWith("vk", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            _ = await this.bot.SendTextMessageAsync(message.Chat, string.Format(Strings.InvalidUri, text));
            this.logger.LogWarning("Not link received {uri}", text);
            return;
        }

        var urlDomain = uri.Host.ToLowerInvariant();
        if (urlDomain is not "vk.com" and not "m.vk.com")
        {
            _ = await this.bot.SendTextMessageAsync(message.Chat, string.Format(Strings.InvalidDomain, uri.Host));
            this.logger.LogWarning("Bad domain received {domain}", uri.Host);
            return;
        }

        if (!uri.LocalPath.StartsWith("/id") || !int.TryParse(uri.LocalPath[3..], out var id))
        {
            try
            {
                var domain = uri.LocalPath[1..];
                if (!DomainRegex().IsMatch(domain))
                {
                    _ = await this.bot.SendTextMessageAsync(message.Chat, Strings.InvalidProfileLink);
                    this.logger.LogWarning("Invalid vk uri {uri}", uri);
                    return;
                }

                var tmpid = await this.vkService.ResolveUsername(domain, this.Options.Vk.ServiceToken, ct).ConfigureAwait(false);
                if (tmpid == null)
                {
                    _ = await this.bot.SendTextMessageAsync(message.Chat, Strings.NotAVkProfile);
                    this.logger.LogWarning("Invalid vk entity type {uri}", uri);
                    return;
                }
                else
                {
                    id = tmpid.Value;
                }
            }
            catch (Exception ex)
            {
                _ = await this.bot.SendTextMessageAsync(message.Chat, string.Format(Strings.ErrorWhileProcessingProfile, uri, ex.Message));
                this.logger.LogError(ex, "Failed to resolve uri {uri}", uri);
                return;
            }
        }

        _ = await this.bot.SendTextMessageAsync(message.Chat, Strings.Thinking);
        try
        {
            AccountCheckResult result;
            try
            {
                await this.locker.WaitAsync().ConfigureAwait(false);
                this.logger.LogWarning("Starting checking {profileId} for {chatId}", id, message.Chat.Id);
                result = await this.whitewashingService.CheckAccount(id, this.Options.Vk.UserToken, ct).ConfigureAwait(false);
            }
            finally
            {
                _ = this.locker.Release();
            }

            // var
            var messages = new List<string>();
            var currentBuilder = new StringBuilder();

            _ = currentBuilder.AppendLine(Strings.RecheckManuallyWarning);

            if (!result.Groups.Any() && !result.Posts.Any())
            {
                _ = currentBuilder.AppendLine(Strings.IHaventFoundAnything);
            }
            else
            {
                _ = currentBuilder.AppendFormat(Strings.FoundNPostsAndMGroups, result.Posts.Count, result.Groups.Count);

                if (result.Posts.Count > JailMessageEasterEggThreshold)
                {
                    _ = currentBuilder.AppendLine(Strings.YouAreGoingToJailForTwentyYears);
                }
            }

            if (result.Posts.Any())
            {
                _ = currentBuilder.AppendLine(Strings.Posts);
                foreach (var post in result.Posts)
                {
                    _ = currentBuilder.AppendLine(post.PostId.ToString());
                    if (currentBuilder.Length > MaxMessageLength)
                    {
                        messages.Add(currentBuilder.ToString());
                        _ = currentBuilder.Clear();
                    }
                }
            }

            if (result.Groups.Any())
            {
                _ = currentBuilder.AppendLine(Strings.Groups);
                foreach (var post in result.Groups)
                {
                    _ = currentBuilder.AppendLine(post.GroupId.ToString());
                    if (currentBuilder.Length > MaxMessageLength)
                    {
                        messages.Add(currentBuilder.ToString());
                        _ = currentBuilder.Clear();
                    }
                }
            }

            if (currentBuilder.Length > 0)
            {
                messages.Add(currentBuilder.ToString());
            }

            this.logger.LogInformation(
                "Report sample for {messageChatId}({messageChatFirstName}, {messageChatLastName}): {report}",
                message.Chat.Id,
                message.Chat.FirstName,
                message.Chat.LastName,
                string.Join("\n", messages[0].Split('\n').Skip(3).Take(3)));
            foreach (var reportMessage in messages)
            {
                _ = await this.bot.SendTextMessageAsync(message.Chat, reportMessage).ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);
            }

            using var ms = new MemoryStream();
            await JsonSerializer.SerializeAsync(ms, result, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }).ConfigureAwait(false);
            _ = ms.Seek(0, SeekOrigin.Begin);
            _ = await this.bot.SendDocumentAsync(message.Chat, new InputOnlineFile(ms, "report.json"), Strings.JsonReportForNerds);

            return;
        }
        catch (Exception ex)
        {
            if (ex is VkApiMethodInvokeException && ex.Message.Contains("This profile is private", StringComparison.CurrentCultureIgnoreCase))
            {
                _ = await this.bot.SendTextMessageAsync(message.Chat, Strings.YourProfileIsPrivate);
                this.logger.LogError(ex, "Private profile {profileId}", id);
            }
            else
            {
                _ = await this.bot.SendTextMessageAsync(message.Chat, string.Format(Strings.ErrorWhileProcessingProfileV2, id, ex.Message));
                this.logger.LogError(ex, "Failed to check account {profileId}", id);
            }

            return;
        }
    }

    private async Task OnStart(Message message) => _ = await this.bot.SendTextMessageAsync(message.Chat, Strings.Greeting);

    private Task UnknownUpdateHandlerAsync(Update update)
    {
        this.logger.LogError("Unknown update type: {updateType}", update.Type);
        return Task.CompletedTask;
    }
}