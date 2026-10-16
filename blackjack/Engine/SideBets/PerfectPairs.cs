using blackjack.Domain;

namespace blackjack.Engine.SideBets
{
    // The player's two cards are a pair: the closer the match, the more it pays.
    public sealed class PerfectPairs : ISideBet
    {
        public string Name => "Perfect Pairs";
        public string Description => "perfect pair 25, coloured pair 12, mixed pair 6";
        public SideBetTiming Timing => SideBetTiming.OnDeal;

        public SideBetResult Evaluate(IReadOnlyList<Card> playerCards, IReadOnlyList<Card> dealerCards)
        {
            var (a, b) = (playerCards[0], playerCards[1]);
            if (a.Rank != b.Rank)
            {
                return new SideBetResult(0m, "no pair");
            }

            if (a.Suit == b.Suit)
            {
                return new SideBetResult(25m, "perfect pair");
            }

            return a.IsRed == b.IsRed ? new SideBetResult(12m, "coloured pair") : new SideBetResult(6m, "mixed pair");
        }
    }
}
