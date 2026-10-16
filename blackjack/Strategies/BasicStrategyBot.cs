using blackjack.Domain;
using blackjack.Engine;

namespace blackjack.Strategies
{
    // Multi-deck basic strategy, as the tables for four to eight decks have it. The rows are
    // indexed by the dealer's upcard, 2 to ace, and read: H hit, S stand, D double (hit if the
    // table won't allow it), T double (stand if it won't), Y split and N don't. Never takes
    // insurance.
    public class BasicStrategyBot : IPlayerStrategy
    {
        private readonly Rules rules;
        private readonly Dictionary<int, string> hard;
        private readonly Dictionary<int, string> soft;
        private readonly Dictionary<int, string> pairs;

        public BasicStrategyBot()
            : this(new Rules())
        {
        }

        public BasicStrategyBot(Rules rules)
        {
            ArgumentNullException.ThrowIfNull(rules);
            this.rules = rules;

            hard = new Dictionary<int, string>
            {
                [9] = "HDDDDHHHHH",
                [10] = "DDDDDDDDHH",
                [11] = rules.DealerHitsSoft17 ? "DDDDDDDDDD" : "DDDDDDDDDH",
                [12] = "HHSSSHHHHH",
                [13] = "SSSSSHHHHH",
                [14] = "SSSSSHHHHH",
                [15] = "SSSSSHHHHH",
                [16] = "SSSSSHHHHH"
            };

            soft = new Dictionary<int, string>
            {
                [13] = "HHHDDHHHHH",
                [14] = "HHHDDHHHHH",
                [15] = "HHDDDHHHHH",
                [16] = "HHDDDHHHHH",
                [17] = "HDDDDHHHHH",
                [18] = rules.DealerHitsSoft17 ? "TTTTTSSHHH" : "STTTTSSHHH",
                [19] = rules.DealerHitsSoft17 ? "SSSSTSSSSS" : "SSSSSSSSSS",
                [20] = "SSSSSSSSSS"
            };

            // Keyed by the point value of the pair; the rows for 2s and 3s, 4s and 6s depend on
            // whether a split hand can be doubled.
            var das = rules.DoubleAfterSplit;
            pairs = new Dictionary<int, string>
            {
                [2] = das ? "YYYYYYNNNN" : "NNYYYYNNNN",
                [3] = das ? "YYYYYYNNNN" : "NNYYYYNNNN",
                [4] = das ? "NNNYYNNNNN" : "NNNNNNNNNN",
                [5] = "NNNNNNNNNN",
                [6] = das ? "YYYYYNNNNN" : "NYYYYNNNNN",
                [7] = "YYYYYYNNNN",
                [8] = "YYYYYYYYYY",
                [9] = "YYYYYNYYNN",
                [10] = "NNNNNNNNNN",
                [11] = "YYYYYYYYYY"
            };
        }

        public Rules Rules => rules;

        public decimal DecideBet(decimal chips, TableContext table) =>
            chips >= table.Rules.MinimumBet ? table.Rules.MinimumBet : 0m;

        public bool DecideInsurance(Hand hand, TableContext table) => false;

        public Move DecideMove(Hand hand, Card dealerUpcard, MoveOptions options, TableContext table)
        {
            var column = dealerUpcard.PointValue - 2; // 2 to ace
            var twoCards = hand.Cards.Count == 2;

            if (twoCards && options.CanSplit && hand.Cards[0].PointValue == hand.Cards[1].PointValue
                && pairs[hand.Cards[0].PointValue][column] == 'Y')
            {
                return Move.Split;
            }

            if (!options.CanHit)
            {
                return Move.Stand;
            }

            var canDouble = twoCards && options.CanDouble;
            if (hand.IsSoft)
            {
                return hand.Value >= 21
                    ? Move.Stand
                    : soft.TryGetValue(hand.Value, out var row) ? Play(row[column], canDouble) : Move.Hit;
            }

            if (hand.Value >= 17)
            {
                return Move.Stand;
            }

            return hard.TryGetValue(hand.Value, out var hardRow) ? Play(hardRow[column], canDouble) : Move.Hit;
        }

        private static Move Play(char entry, bool canDouble) => entry switch
        {
            'S' => Move.Stand,
            'D' => canDouble ? Move.Double : Move.Hit,
            'T' => canDouble ? Move.Double : Move.Stand,
            _ => Move.Hit
        };
    }
}
