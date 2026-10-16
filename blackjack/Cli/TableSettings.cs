using blackjack.Engine;
using blackjack.Engine.SideBets;

namespace blackjack.Cli
{
    internal sealed record TableSettings(
        string Name,
        decimal Chips,
        int Bots,
        Rules Rules,
        IReadOnlyList<ISideBet> SideBets,
        int Seed);
}
