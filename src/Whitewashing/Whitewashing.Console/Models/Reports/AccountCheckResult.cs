namespace Whitewashing.Console.Models.Reports
{
    using System.Collections.Generic;
    public record AccountCheckResult(IReadOnlyList<PostCheckResult> Posts, IReadOnlyList<GroupCheckResult> Groups);
}
