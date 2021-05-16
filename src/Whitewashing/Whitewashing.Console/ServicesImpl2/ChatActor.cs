namespace Whitewashing.Console.ServicesImpl2
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading.Tasks;

    using Microsoft.Extensions.Logging;

    public class ChatActor : IChatActor
    {
        private ActorStatus status = ActorStatus.Idle;
        private readonly IReadOnlyList<ISourceExtractor> sourceExtractors;
        private readonly ILogger<ChatActor> logger;
        private readonly string id;

        public ChatActor(IEnumerable<ISourceExtractor> sourceExtractors, ILogger<ChatActor> logger)
        {
            this.sourceExtractors = sourceExtractors.ToArray();
            this.logger = logger;
        }

        public Task Process(IIncomingMessage message) => message.Text switch
        {
            "" or null
                => message.Reply(new OutgoingMessage
                {
                    Text = $"Не надо пытаться накормить меня картинками или голосовухами. Цивилизованный AI понимает только текст.",
                }),
            "/start"
                => message.Reply(new OutgoingMessage
                {
                    Text = $"Привет, мой маленький экстремист(ка). Отправь мне ссылку на страницу ВК, по которой нужны рекомендации по очистке.\n\nРепорты о проблемах / запрос фич можно отправить сюда: https://t.me/joinchat/LCYlmetoVigzODQ6",
                }),
            _ =>
                this.status switch
                {
                    ActorStatus.Idle => this.ProcessLinkMessage(message),
                    ActorStatus.Working => message.Reply(new OutgoingMessage
                    {
                        Text = $"Бот ещё думает. Умерьте пыл.",
                    }),
                    _ => message.Reply(new OutgoingMessage
                    {
                        Text = $"У бота приступ шизофрении. Разработчики уже действуют.",
                    }),
                }

        };
        private async Task ProcessLinkMessage(IIncomingMessage message)
        {
            var messageText = message.Text;
            var processorResultsTasks = this
                .sourceExtractors
                .Select(sourceExtractor => (sourceExtractor, result: sourceExtractor.TryResolveLink(messageText)))
                .ToArray();
            try
            {
                await Task.WhenAll(processorResultsTasks.Select(a => a.result)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to resolve links from message {0} for chat {1}", message, this.id);
                await message.Reply(new OutgoingMessage
                {
                    Text = $"Всё развалилось при попытке отрезолвить ссылку '{messageText}'.",
                }).ConfigureAwait(false);
                return;
            }
            var processorResults = processorResultsTasks
                .Select(a => (a.sourceExtractor, a.result.Result))
                .Where(a => a.Result.ResolutionResult != ResolutionResult.InvalidLink)
                .ToArray();

            if (processorResults.Length == 0)
            {
                await message.Reply(new OutgoingMessage
                {
                    Text = $"'{messageText}' не похож на ссылку, которую",
                }).ConfigureAwait(false);
            }
        }

        private enum ActorStatus
        {
            Idle,
            Working,
        }
    }

}
