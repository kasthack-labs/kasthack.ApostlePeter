using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using VkNet.Model;
using VkNet.Model.Attachments;

namespace Whitewashing.Console.ServicesImpl
{
    public interface IVkFetcherService
    {
        Task<IReadOnlyList<Group>> GetGroups(int userId, string token, CancellationToken cancellationToken);
        Task<IReadOnlyList<Post>> GetWallPosts(int userId, string token, CancellationToken cancellationToken);
        Task<int?> ResolveUsername(string domain, string token, CancellationToken cancellationToken);
    }
}