using blackjack.Domain;
using blackjack.Engine;

namespace blackjack.Cli
{
    // The human's record for the cash-out summary. Hands the autopilot plays are kept apart.
    internal sealed class SessionStats
    {
        public SessionStats(decimal startingChips)
        {
            StartingChips = startingChips;
        }

        public decimal StartingChips { get; }
        public int Hands { get; private set; }
        public int Wins { get; private set; }
        public int Losses { get; private set; }
        public int Pushes { get; private set; }
        public int Blackjacks { get; private set; }
        public int Doubles { get; private set; }
        public int Splits { get; private set; }
        public int InsuranceTaken { get; private set; }
        public int SideBetsPlaced { get; private set; }
        public int SideBetsWon { get; private set; }
        public decimal SideBetNet { get; private set; }
        public int AutopilotHands { get; private set; }
        public decimal AutopilotNet { get; private set; }
        public int Decisions { get; private set; }
        public int BookDecisions { get; private set; }

        // Tallies a settled round: every hand, the insurance and the side bets.
        public void Record(Player player)
        {
            foreach (var hand in player.Hands)
            {
                Hands++;
                if (hand.IsDoubled)
                {
                    Doubles++;
                }

                switch (hand.Outcome)
                {
                    case Outcome.Blackjack:
                        Blackjacks++;
                        Wins++;
                        break;
                    case Outcome.Win:
                        Wins++;
                        break;
                    case Outcome.Push:
                        Pushes++;
                        break;
                    case Outcome.Lose:
                        Losses++;
                        break;
                }
            }

            Splits += Math.Max(0, player.Hands.Count - 1);
            if (player.Insurance > 0m)
            {
                InsuranceTaken++;
            }

            SideBetsPlaced += player.SideBets.Count;
            SideBetsWon += player.SideBets.Count(s => s.Result is { Wins: true });
            SideBetNet += player.SideBetNet;
        }

        // A settled round the book played: its hands, and what they won or lost.
        public void RecordAutopilot(Player player)
        {
            AutopilotHands += player.Hands.Count;
            AutopilotNet += player.RoundNet;
        }

        public void RecordDecision(bool followedBook)
        {
            Decisions++;
            if (followedBook)
            {
                BookDecisions++;
            }
        }
    }
}
