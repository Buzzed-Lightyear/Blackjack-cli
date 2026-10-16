using blackjack.Domain;
using blackjack.Engine;

namespace blackjack.Strategies
{
    // Chases losses: the stake doubles after every round that lost chips and drops back to the
    // minimum after one that didn't, as far as the chips go. Plays the book, but on hard 12 to 16
    // goes with a hunch against it one time in eight, and takes insurance one time in four. The
    // hunches come from the Random it is given, so a seeded one plays the same way every time.
    public sealed class GamblerBot : IPlayerStrategy
    {
        private readonly BasicStrategyBot book;
        private readonly Random random;
        private decimal lastBet;
        private decimal chipsAfterLastBet;

        public GamblerBot(Rules rules, Random random)
        {
            ArgumentNullException.ThrowIfNull(random);
            book = new BasicStrategyBot(rules);
            this.random = random;
        }

        public decimal DecideBet(decimal chips, TableContext table)
        {
            var min = table.Rules.MinimumBet;
            if (chips < min)
            {
                lastBet = 0m;
                return 0m;
            }

            // Less came back from the last round than went out on it: a loss to chase.
            var lost = lastBet > 0m && chips - chipsAfterLastBet < lastBet;
            var bet = Math.Min(lost ? lastBet * 2 : min, chips);
            lastBet = bet;
            chipsAfterLastBet = chips - bet;
            return bet;
        }

        public bool DecideInsurance(Hand hand, TableContext table) => random.Next(4) == 0;

        public Move DecideMove(Hand hand, Card dealerUpcard, MoveOptions options, TableContext table)
        {
            var advice = book.DecideMove(hand, dealerUpcard, options, table);
            if (hand.IsSoft || hand.Value is < 12 or > 16 || advice is not (Move.Hit or Move.Stand))
            {
                return advice;
            }

            if (random.Next(8) != 0)
            {
                return advice;
            }

            return advice == Move.Hit ? Move.Stand : Move.Hit;
        }
    }
}
