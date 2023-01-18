namespace kasthack.ApostlePeter.ServicesImpl;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using kasthack.ApostlePeter.Configuration;
using kasthack.ApostlePeter.Models.Reports;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VkNet.Model;
using VkNet.Model.Attachments;

public class WhitewashingService
{
    private readonly VkFetcherService vkFetcher;
    private readonly ILogger logger;
    private readonly ConfigurationOptions options;

    public WhitewashingService(
        VkFetcherService vkFetcher,
        ILogger<WhitewashingService> logger,
        IOptions<ConfigurationOptions> options)
    {
        this.vkFetcher = vkFetcher;
        this.logger = logger;
        this.options = options.Value;
    }

    public async Task<AccountCheckResult> CheckAccount(int userId, string token, CancellationToken cancellationToken)
    {
        var ownPosts = await this.vkFetcher.GetWallPosts(userId, token, cancellationToken).ConfigureAwait(false);
        var badPosts = ownPosts.Select(post => this.CheckPost(post)).Where(a => a.Result).ToArray();

        GroupCheckResult[] badGroups;
        try
        {
            var ownGroups = await this.vkFetcher.GetGroups(userId, token, cancellationToken).ConfigureAwait(false);
            badGroups = ownGroups.Select(group => this.CheckGroup(group)).Where(a => a.Result).ToArray();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Failed to fetch groups for {profileId}", userId);
            badGroups = Array.Empty<GroupCheckResult>();
        }

        return new AccountCheckResult(badPosts, badGroups);
    }

    private GroupCheckResult CheckGroup(Group group)
    {
        var groupId = new GroupIdentifier((int)group.Id);
        if (this.options.BlackList.Pages.Contains(-(int)group.Id))
        {
            return new GroupCheckResult(true, groupId, "Group id is blacklisted");
        }

        var matchedBlacklistWords = this.options.BlackList.Words.Where(a => group.Name.Contains(a, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matchedBlacklistWords.Any())
        {
            return new GroupCheckResult(true, groupId, $"Group name '{group.Name}' contains prohibited words: {string.Join(",", matchedBlacklistWords)}");
        }

        return new GroupCheckResult(false, groupId, default);
    }

    private PostCheckResult CheckPost(Post post)
    {
        var postUrl = new PostIdentifier((int)post.OwnerId!, (int)post.Id!, post.AccessKey);
        {
            var badFragments = this.options.BlackList.Words.SelectMany(blacklistedWord =>
                        new[] { post.Text, post.CopyText }
                            .Concat(post.CopyHistory?.SelectMany(a => new[] { a.Text, a.CopyText, a.PostSource?.Data?.ToString() }) ?? Array.Empty<string>())
                            .Concat(post.Attachments
                                .Where(a => a.Type == typeof(Link))
                                .Select(a => (Link)a.Instance)
                                .SelectMany(a => new[] { a.Description, a.Uri.ToString() }))
                            .Where(postFragment => postFragment?.Contains(blacklistedWord, StringComparison.OrdinalIgnoreCase) ?? false)
                            .Select(postFragment => (postFragment, blacklistedWord)))
                .ToArray();
            if (badFragments.Any())
            {
                return new PostCheckResult(true, postUrl, $"Post fragements contain prohibited words: {string.Join(",", badFragments.Select(a => a.blacklistedWord))}");
            }
        }

        {
            var sources = new[] { post.CopyOwnerId }
                        .Concat(post?.CopyHistory?.Select(a => a.OwnerId) ?? Array.Empty<long?>())
                        .Where(a => a != null)
                        .Select(a => (int)a!.Value);
            var badSources = sources.Where(source => this.options.BlackList.Pages.Contains(source)).ToArray();
            if (badSources.Any())
            {
                return new PostCheckResult(true, postUrl, $"Post is a repost from one or more prohibited sources: {string.Join(",", badSources)}");
            }
        }

        return new PostCheckResult(false, postUrl, default);
    }
}