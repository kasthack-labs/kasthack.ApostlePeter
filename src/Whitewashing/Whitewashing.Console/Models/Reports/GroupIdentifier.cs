namespace Whitewashing.Console.Models.Reports
{
    public record GroupIdentifier(int Id)
    {
        public override string ToString() => $"https://vk.com/club{this.Id}";
    }
}
