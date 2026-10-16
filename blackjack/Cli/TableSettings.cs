using blackjack.Engine;

namespace blackjack.Cli
{
    internal sealed record TableSettings(
        string Name,
        decimal Chips,
        int Bots,
        Rules Rules,
        int Seed);
}
