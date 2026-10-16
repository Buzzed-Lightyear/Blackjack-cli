using blackjack.Domain;

namespace blackjack.Engine.SideBets
{
    // The player's two cards total 20; a pair of queens of hearts is the lady, and pays most when
    // the dealer turns over a blackjack.
    public sealed class LuckyLadies : ISideBet
    {
        private static readonly Card QueenOfHearts = new(Rank.Queen, Suit.Hearts);

        public string Name => "Lucky Ladies";
        public string Description => "Q♥Q♥ with a dealer blackjack 1000, Q♥Q♥ 125, matched 20 19, suited 20 9, any 20 4";
        public SideBetTiming Timing => SideBetTiming.OnDeal;

        public SideBetResult Evaluate(IReadOnlyList<Card> playerCards, IReadOnlyList<Card> dealerCards)
        {
            var (a, b) = (playerCards[0], playerCards[1]);
            if (a.PointValue + b.PointValue != 20)
            {
                return new SideBetResult(0m, "no 20");
            }

            if (a == QueenOfHearts && b == QueenOfHearts)
            {
                return dealerCards.Count == 2 && dealerCards[0].PointValue + dealerCards[1].PointValue == 21
                    ? new SideBetResult(1000m, "queens of hearts against a dealer blackjack")
                    : new SideBetResult(125m, "queens of hearts");
            }

            if (a == b)
            {
                return new SideBetResult(19m, "matched 20");
            }

            return a.Suit == b.Suit ? new SideBetResult(9m, "suited 20") : new SideBetResult(4m, "20");
        }
    }
}
