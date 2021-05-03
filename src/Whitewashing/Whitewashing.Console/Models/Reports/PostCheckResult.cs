namespace Whitewashing.Console.Models.Reports
{
    public record PostCheckResult(bool Result, PostIdentifier PostId, string? Description);
}
