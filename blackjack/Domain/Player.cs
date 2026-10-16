using blackjack.Engine.SideBets;
using blackjack.Strategies;

namespace blackjack.Domain
{
    public class Player
    {
        public Player(string name, decimal chips, IPlayerStrategy strategy)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentOutOfRangeException.ThrowIfNegative(chips);
            ArgumentNullException.ThrowIfNull(strategy);

            Name = name;
            Chips = chips;
            Strategy = strategy;
        }

        public string Name { get; }
        public decimal Chips { get; private set; }
        public List<Hand> Hands { get; } = new();
        public IPlayerStrategy Strategy { get; }

        // This round's side bets, in the order they were placed.
        public List<SideBetStake> SideBets { get; } = new();

        // The insurance stake, and what it paid back once the dealer peeked.
        public decimal Insurance { get; internal set; }
        public decimal InsurancePayout { get; internal set; }

        public decimal SideBetNet => SideBets.Sum(s => s.Net);

        // What the round has won or lost so far. Payouts are 0 until settlement, so mid-round
        // this is minus everything at risk.
        public decimal RoundNet =>
            Hands.Sum(h => h.Payout - h.Bet) + InsurancePayout - Insurance + SideBetNet;

        public void PlaceBet(decimal amount)
        {
            Stake(amount);
            Hands.Add(new Hand { Bet = amount });
        }

        public void PlaceSideBet(ISideBet bet, decimal amount)
        {
            ArgumentNullException.ThrowIfNull(bet);
            Stake(amount);
            SideBets.Add(new SideBetStake(bet, amount));
        }

        // Takes chips off the stack for a bet, a double, a split hand or insurance.
        public void Stake(decimal amount)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
            if (amount > Chips)
            {
                throw new InvalidOperationException("Not enough chips to place the bet.");
            }

            Chips -= amount;
        }

        public void Receive(decimal amount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(amount);
            Chips += amount;
        }

        internal void ClearRound()
        {
            Hands.Clear();
            SideBets.Clear();
            Insurance = 0m;
            InsurancePayout = 0m;
        }
    }
}
