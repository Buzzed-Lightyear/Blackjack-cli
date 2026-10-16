using blackjack.Engine;
using blackjack.Engine.SideBets;
using blackjack.Strategies;

namespace blackjack.Cli
{
    // BotTypes holds one type per bot in seat order, with any random pick already drawn.
    internal sealed record TableSettings(
        string Name,
        decimal Chips,
        IReadOnlyList<BotType> BotTypes,
        Rules Rules,
        IReadOnlyList<ISideBet> SideBets,
        int Seed);
}
