using blackjack.Domain;

namespace blackjack.Engine.SideBets
{
    // The player's two cards share a suit; a suited king and queen is the royal match.
    public sealed class RoyalMatch : ISideBet
    {
        public string Name => "Royal Match";
        public string Description => "suited king and queen 25, any two suited cards 5 to 2";
        public SideBetTiming Timing => SideBetTiming.OnDeal;

        public SideBetResult Evaluate(IReadOnlyList<Card> playerCards, IReadOnlyList<Card> dealerCards)
        {
            var (a, b) = (playerCards[0], playerCards[1]);
            if (a.Suit != b.Suit)
            {
                return new SideBetResult(0m, "unsuited");
            }

            var royal = (a.Rank == Rank.King && b.Rank == Rank.Queen) || (a.Rank == Rank.Queen && b.Rank == Rank.King);
            return royal ? new SideBetResult(25m, "royal match") : new SideBetResult(2.5m, "suited");
        }
    }
}
