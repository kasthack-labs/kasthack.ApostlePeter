namespace Whitewashing.Console.ServicesImpl
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Telegram.Bot;
    using Telegram.Bot.Exceptions;
    using Telegram.Bot.Extensions.Polling;
    using Telegram.Bot.Types;
    using Telegram.Bot.Types.Enums;
    using Telegram.Bot.Types.InputFiles;
    using Telegram.Bot.Types.ReplyMarkups;

    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.Extensions.Hosting;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Collections.Generic;
    using VkNet.Exception;

    public class BotService : IHostedService
    {
        private readonly ILogger logger;
        private readonly Configuration options;
        private readonly TelegramBotClient bot;
        private readonly VkFetcherService vkService;
        private readonly WhitewashingService whitewashingService;
        private readonly SemaphoreSlim locker = new SemaphoreSlim(1);

        public BotService(
            IOptions<Configuration> options,
            ILogger<BotService> logger,
            VkFetcherService vkService,
            WhitewashingService whitewashingService
            )
        {
            this.logger = logger;
            this.options = options.Value;
            this.bot = new TelegramBotClient(this.options.Telegram.Token);
            this.vkService = vkService;
            this.whitewashingService = whitewashingService;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {

            var me = await this.bot.GetMeAsync();
            this.bot.StartReceiving(
                new DefaultUpdateHandler(this.HandleUpdateAsync, this.HandleErrorAsync),
                cancellationToken
            );
        }
        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            var handler = update.Type switch
            {
                UpdateType.Message => this.BotOnMessageReceived(update.Message),
                _ => this.UnknownUpdateHandlerAsync(update)
            };

            try
            {
                await handler;
            }
            catch (Exception exception)
            {
                await this.HandleErrorAsync(botClient, exception, cancellationToken);
            }
        }
        private async Task BotOnMessageReceived(Message message)
        {
            logger.LogDebug("Received message from {0}: {1}", message.Chat.Id, message.Text);
            if (message.Type != MessageType.Text)
            {
                await this.bot.SendTextMessageAsync(message.Chat, $"Не надо пытаться накормить меня картинками или голосовухами. Цивилизованный AI понимает только текст.");
            }

            var action = (message.Text?.Split(' ').FirstOrDefault()) switch
            {
                "/start" => OnStart(message),
                //"/help" => OnHelp(message),

                _ => OnLink(message)

            };
            Task.Run(async () =>
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
                await this.bot.SendTextMessageAsync(message.Chat, $"Это('{text}') совсем не похоже на ссылку. @<username> не поддерживаются, используйте https://vk.com/<username>");
                this.logger.LogWarning("Not link received {0}", text);
                return;
            }
            var urlDomain = uri.Host.ToLowerInvariant();
            if (urlDomain != "vk.com" && urlDomain != "m.vk.com")
            {
                await this.bot.SendTextMessageAsync(message.Chat, $"Домен {uri.Host} не похож на vk.com. ");
                this.logger.LogWarning("Bad domain received {0}", uri.Host);
                return;
            }
            int id;
            if (!uri.LocalPath.StartsWith("/id") || !int.TryParse(uri.LocalPath[3..], out id))
            {
                try
                {
                    var domain = uri.LocalPath[1..];
                    if (!Regex.IsMatch(domain, "^[\\w\\.]+$"))
                    {
                        await this.bot.SendTextMessageAsync(message.Chat, $"Это не похоже на ссылку на профиль вк. Обычно та выглядит как 'https://vk.com/id1' или 'https://vk.com/durov'");
                        this.logger.LogWarning("Invalid vk uri {0}", uri);
                        return;
                    }
                    var tmpid = await this.vkService.ResolveUsername(domain, this.options.Vk.ServiceToken, ct).ConfigureAwait(false);
                    if (tmpid == null)
                    {
                        await this.bot.SendTextMessageAsync(message.Chat, $"Это не профиль ВК. Группы пока не обрабатываются(делаю). Обычно ссылка выглядит как 'https://vk.com/id1' или 'https://vk.com/durov'");
                        this.logger.LogWarning("Invalid vk entity type {0}", uri);
                        return;
                    }
                    else
                    {
                        id = tmpid.Value;
                    }
                }
                catch (Exception ex)
                {
                    await this.bot.SendTextMessageAsync(message.Chat, $"Всё развалилось при попытке отрезолвить ссылку {uri}: {ex.Message}.\n\n Попробуйте позже");
                    this.logger.LogError(ex, "Failed to resolve uri {0}", uri);
                    return;
                }
            }
            await this.bot.SendTextMessageAsync(message.Chat, $"Думаем... Пока использую один токен на всех пользователей, так что, возможно, это надолго");
            try
            {
                Models.Reports.AccountCheckResult result;
                try
                {
                    await locker.WaitAsync().ConfigureAwait(false);
                    this.logger.LogWarning("Starting checking {0} for {1}", id, message.Chat.Id);
                    result = await this.whitewashingService.CheckAccount(id, this.options.Vk.UserToken, ct).ConfigureAwait(false);
                }
                finally
                {
                    this.locker.Release();
                }
                //var 
                var messages = new List<string>();
                var maxLength = 4000;
                var currentBuilder = new StringBuilder();

                currentBuilder.AppendLine("Warning: перепроверяйте за мной. Я могу не определить какие-то упоминания. Если вы нашли пропущенный пост/группу, напишите об этом в чат разработки бота -- автор поправит.\n\n");

                if (!result.Groups.Any() && !result.Posts.Any())
                {
                    currentBuilder.AppendLine("Я ничего не нашёл, но, возможно, ФСБ сумеет. Если у вас закрытый профиль, это могло быть причиной.");
                }
                else
                {
                    currentBuilder.AppendLine($"Найдено {result.Posts.Count} террористических постов и {result.Groups.Count} групп.");

                    if (result.Posts.Count > 100)
                    {
                        currentBuilder.AppendLine("Тут лет на 20 наберется.");
                    }
                }

                if (result.Posts.Any())
                {
                    currentBuilder.AppendLine("Посты:");
                    foreach (var post in result.Posts)
                    {
                        currentBuilder.AppendLine(post.PostId.ToString());
                        if (currentBuilder.Length > maxLength)
                        {
                            messages.Add(currentBuilder.ToString());
                            currentBuilder.Clear();
                        }
                    }
                }

                if (result.Groups.Any())
                {
                    currentBuilder.AppendLine("Группы:");
                    foreach (var post in result.Groups)
                    {
                        currentBuilder.AppendLine(post.GroupId.ToString());
                        if (currentBuilder.Length > maxLength)
                        {
                            messages.Add(currentBuilder.ToString());
                            currentBuilder.Clear();
                        }
                    }
                }
                if (currentBuilder.Length > 0)
                {
                    messages.Add(currentBuilder.ToString());
                }
                this.logger.LogInformation("Report sample for {0}({1}, {2}): {3}", message.Chat.Id, message.Chat.FirstName, message.Chat.LastName, string.Join("\n", messages.First().Split('\n').Skip(3).Take(3)));
                foreach (var reportMessage in messages)
                {
                    await this.bot.SendTextMessageAsync(message.Chat, reportMessage).ConfigureAwait(false);
                    await Task.Delay(50).ConfigureAwait(false);
                }
                using var ms = new MemoryStream();
                await System.Text.Json.JsonSerializer.SerializeAsync(ms, result, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }).ConfigureAwait(false);
                ms.Seek(0, SeekOrigin.Begin);
                await this.bot.SendDocumentAsync(message.Chat, new InputOnlineFile(ms, "report.json"), "Репорт в JSON для нердов");

                return;
            }
            catch (Exception ex)
            {
                if (ex is VkApiMethodInvokeException && ex.Message.Contains("This profile is private", StringComparison.CurrentCultureIgnoreCase))
                {
                    await this.bot.SendTextMessageAsync(message.Chat, $"Похоже, у вас приватный профиль. Поменяйте настройки приватности.");
                    this.logger.LogError(ex, "Private profile {0}", id);
                }
                else
                {
                    await this.bot.SendTextMessageAsync(message.Chat, $"Всё развалилось при попытке проверить аккаунт {id}. Если у вас приватный профиль, это могло быть причиной.: {ex.Message}");
                    this.logger.LogError(ex, "Failed to check account {0}", id);
                }
                return;
            }


        }

        private async Task OnStart(Message message)
        {
            await this.bot.SendTextMessageAsync(message.Chat, $"Привет, мой маленький экстремист(ка). Отправь мне ссылку на страницу ВК, по которой нужны рекомендации по очистке.\n\nРепорты о проблемах / запрос фич можно отправить сюда: https://t.me/joinchat/LCYlmetoVigzODQ6");
        }

        public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            this.logger.LogError(exception, ErrorMessage);
        }
        private async Task UnknownUpdateHandlerAsync(Update update)
        {
            this.logger.LogError($"Unknown update type: {update.Type}");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
        }
    }
}