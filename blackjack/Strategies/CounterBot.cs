using blackjack.Domain;
using blackjack.Engine;

namespace blackjack.Strategies
{
    // Plays the book with the Hi-Lo count on top: the stake rises with the true count, insurance is
    // taken when the shoe is rich in tens, and the usual index plays override the book's hit, stand
    // and double decisions on the hands where the count matters most.
    public sealed class CounterBot : IPlayerStrategy
    {
        private readonly BasicStrategyBot book;

        public CounterBot(Rules rules)
        {
            book = new BasicStrategyBot(rules);
        }

        // Units of the minimum by true count: 1 below +2, then 2, 4 and 8, and 12 from +5, as far
        // as the chips go. One unit short means sitting out.
        public decimal DecideBet(decimal chips, TableContext table)
        {
            var min = table.Rules.MinimumBet;
            if (chips < min)
            {
                return 0m;
            }

            var units = Math.Floor(table.TrueCount) switch
            {
                < 2m => 1,
                2m => 2,
                3m => 4,
                4m => 8,
                _ => 12
            };
            return Math.Min(units * min, chips);
        }

        public bool DecideInsurance(Hand hand, TableContext table) => table.TrueCount >= 3m;

        public Move DecideMove(Hand hand, Card dealerUpcard, MoveOptions options, TableContext table)
        {
            var advice = book.DecideMove(hand, dealerUpcard, options, table);
            if (advice == Move.Split || !options.CanHit || hand.IsSoft)
            {
                return advice;
            }

            var count = table.TrueCount;
            var canDouble = hand.Cards.Count == 2 && options.CanDouble;
            return (hand.Value, dealerUpcard.PointValue) switch
            {
                (16, 10) when count >= 0m => Move.Stand,
                (15, 10) when count >= 4m => Move.Stand,
                (12, 3) when count >= 2m => Move.Stand,
                (12, 2) when count >= 3m => Move.Stand,
                (13, 2) => count >= -1m ? Move.Stand : Move.Hit,
                (12, 4) => count >= 0m ? Move.Stand : Move.Hit,
                (10, 10) when count >= 4m && canDouble => Move.Double,
                (11, 11) when count >= 1m && canDouble => Move.Double,
                (9, 2) when count >= 1m && canDouble => Move.Double,
                (9, 7) when count >= 3m && canDouble => Move.Double,
                _ => advice
            };
        }
    }
}
