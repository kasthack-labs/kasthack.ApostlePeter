namespace kasthack.ApostlePeter.Configuration;
public class ConfigurationOptions
{
    public TelegramOptions Telegram { get; set; } = default!;

    public BlackListOptions BlackList { get; set; } = default!;

    public VkOptions Vk { get; set; } = default!;
}