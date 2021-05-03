namespace Whitewashing.Console.Models.Reports
{
    public record GroupCheckResult(bool Result, GroupIdentifier GroupId, string? Description);
}
