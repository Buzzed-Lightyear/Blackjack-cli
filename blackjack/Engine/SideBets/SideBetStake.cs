namespace blackjack.Engine.SideBets
{
    // One side bet a player has on the table this round. Game fills in the result and payout.
    public class SideBetStake
    {
        public SideBetStake(ISideBet bet, decimal amount)
        {
            ArgumentNullException.ThrowIfNull(bet);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

            Bet = bet;
            Amount = amount;
        }

        public ISideBet Bet { get; }
        public decimal Amount { get; }
        public SideBetResult? Result { get; internal set; }

        // Includes the returned stake, like Hand.Payout.
        public decimal Payout { get; internal set; }

        public bool IsResolved => Result is not null;
        public decimal Net => Payout - Amount;
    }
}
