namespace kasthack.ApostlePeter.Configuration;

public class VkOptions
{
    public int AppId { get; set; } = default!;

    public string Privileges { get; set; } = default!;

    public string ServiceToken { get; set; } = default!;

    public string UserToken { get; set; } = default!;

    public string RedirectUri { get; set; } = default!;
}