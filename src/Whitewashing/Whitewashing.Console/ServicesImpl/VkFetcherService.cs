namespace Whitewashing.Console.ServicesImpl
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    using VkNet;
    using VkNet.Model;
    using VkNet.Model.Attachments;

    public class VkFetcherService
    {
        private const int MaxRps = 3;
        private const int PostChunkSize = 100;
        private const int GroupChunkSize = 1000;
        private readonly ILogger logger;
        private readonly Configuration options;

        public VkFetcherService(IOptions<Configuration> options, ILogger<VkFetcherService> logger)
        {
            this.logger = logger;
            this.options = options.Value;
        }
        public async Task<int?> ResolveUsername(string domain, string token, CancellationToken cancellationToken)
        {
            var posts = new List<Post>();
            using var api = await this.GetApi(token, cancellationToken).ConfigureAwait(false);
            var result = await api.Utils.ResolveScreenNameAsync(domain).ConfigureAwait(false);
            if (result == null || result.Type != VkNet.Enums.VkObjectType.User)
            {
                return null;
            }
            return (int)result.Id;
        }

        public async Task<IReadOnlyList<Post>> GetWallPosts(int userId, string token, CancellationToken cancellationToken)
        {
            var posts = new List<Post>();
            using var api = await this.GetApi(token, cancellationToken).ConfigureAwait(false);
            var totalCount = 0;
            var offset = 0;

            do
            {
                var delayTask = Task.Delay(1000 / MaxRps);
                try
                {
                    var wall = await api.Wall.GetAsync(new VkNet.Model.RequestParams.WallGetParams
                    {
                        Count = PostChunkSize,
                        Offset = (ulong)offset,
                        Extended = false,
                        OwnerId = userId,

                    }).ConfigureAwait(false);
                    totalCount = (int)wall.TotalCount;
                    offset += wall.WallPosts.Count;
                    posts.AddRange(wall.WallPosts);

                    this.logger.LogDebug("Fetched {0} more posts(total {1}) of {2} for user {3}", wall.WallPosts.Count, offset, totalCount, userId);
                }
                catch (Exception ex)
                {
                    this.logger.LogError("Failed to fetch posts for {0} at offset {1}", userId, offset);
                    throw;
                }
                finally
                {
                    await delayTask.ConfigureAwait(false);
                }
            } while (offset < totalCount && !cancellationToken.IsCancellationRequested);

            return posts;
        }
        public async Task<IReadOnlyList<Group>> GetGroups(int userId, string token, CancellationToken cancellationToken)
        {
            var result = new List<Group>();
            using var api = await this.GetApi(token, cancellationToken).ConfigureAwait(false);

            var totalCount = 0;
            var offset = 0;
            do
            {
                var delayTask = Task.Delay(1000 / MaxRps);

                try
                {
                    var response = await api.Groups.GetAsync(new VkNet.Model.RequestParams.GroupsGetParams
                    {
                        Count = GroupChunkSize,
                        Offset = offset,
                        UserId = userId,
                        Extended = true,
                    }).ConfigureAwait(false);
                    totalCount = (int)response.TotalCount;
                    offset += response.Count;
                    result.AddRange(response);

                    this.logger.LogDebug("Fetched {0} more groups(total {1}) of {2} for user {3}", response.Count, offset, totalCount, userId);
                }
                catch (Exception ex)
                {
                    this.logger.LogError($"Failed to fetch posts for {0} at offset {1}", userId, offset);
                    throw;

                }
                finally
                {
                    await delayTask.ConfigureAwait(false);
                }
            } while (offset < totalCount && !cancellationToken.IsCancellationRequested);

            return result;
        }
        private async Task<VkApi> GetApi(string token, CancellationToken cancellationToken)
        {
            var api = new VkApi();
            await api.AuthorizeAsync(new ApiAuthParams
            {
                AccessToken = token,
            }).ConfigureAwait(false);
            return api;
        }
    }


}