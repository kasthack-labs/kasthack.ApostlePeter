namespace kasthack.ApostlePeter.ServicesImpl;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using kasthack.ApostlePeter.Configuration;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VkNet;
using VkNet.Model;
using VkNet.Model.Attachments;

public record VkFetcherService(ILogger<VkFetcherService> Logger)
{
    private const int MaxRps = 3;
    private const int PostChunkSize = 100;
    private const int GroupChunkSize = 1000;

    public async Task<int?> ResolveUsername(string domain, string token, CancellationToken cancellationToken)
    {
        using var api = await this.GetApi(token, cancellationToken).ConfigureAwait(false);
        var result = await api.Utils.ResolveScreenNameAsync(domain).ConfigureAwait(false);
        if (result == null || result.Type != VkNet.Enums.VkObjectType.User)
        {
            return null;
        }

        return (int)result.Id!;
    }

    public async Task<IReadOnlyList<Post>> GetWallPosts(int userId, string token, CancellationToken cancellationToken)
    {
        var posts = new List<Post>();
        using var api = await this.GetApi(token, cancellationToken).ConfigureAwait(false);
        var totalCount = 0;
        var offset = 0;

        do
        {
            var delayTask = Task.Delay(1000 / MaxRps, cancellationToken);
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

                this.Logger.LogDebug("Fetched {postCount} more posts(total {offset}) of {totalPostCount} for user {profileIds}", wall.WallPosts.Count, offset, totalCount, userId);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to fetch posts for {profileId} at offset {offset}", userId, offset);
                throw;
            }
            finally
            {
                await delayTask.ConfigureAwait(false);
            }
        }
        while (offset < totalCount && !cancellationToken.IsCancellationRequested);

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
            var delayTask = Task.Delay(1000 / MaxRps, cancellationToken);

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

                this.Logger.LogDebug("Fetched {groupCount} more groups(total {offset}) of {totalGroupCount} for user {profileId}", response.Count, offset, totalCount, userId);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to fetch posts for {profileId} at offset {offset}", userId, offset);
                throw;
            }
            finally
            {
                await delayTask.ConfigureAwait(false);
            }
        }
        while (offset < totalCount && !cancellationToken.IsCancellationRequested);

        return result;
    }

    private async Task<VkApi> GetApi(string token, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var api = new VkApi();
        await api.AuthorizeAsync(new ApiAuthParams
        {
            AccessToken = token,
        }).ConfigureAwait(false);
        return api;
    }
}