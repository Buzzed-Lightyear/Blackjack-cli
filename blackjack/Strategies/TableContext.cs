using blackjack.Engine;

namespace blackjack.Strategies
{
    // What a player can see of the table when asked for a decision: the rules (the minimum bet
    // among them) and the Hi-Lo count as the app keeps it, which is over the cards dealt since the
    // last shuffle.
    public readonly record struct TableContext(Rules Rules, int RunningCount, decimal TrueCount, decimal DecksRemaining);
}
