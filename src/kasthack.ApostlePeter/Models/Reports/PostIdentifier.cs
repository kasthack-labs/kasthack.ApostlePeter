namespace kasthack.ApostlePeter.Models.Reports;

public record PostIdentifier(int OwnerId, int Id, string? AccessKey)
{
    public override string ToString() => $"https://vk.com/wall{this.OwnerId}_{this.Id}{(this.AccessKey != null ? "?access_key=" + this.AccessKey : string.Empty)}";
}
