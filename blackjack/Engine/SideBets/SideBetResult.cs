namespace blackjack.Engine.SideBets
{
    // Pays is a multiple of the stake on top of getting it back ("to 1"); 0 loses the stake.
    // Label names the hand that was or wasn't made: "flush", "4-card bust", "no pair".
    public readonly record struct SideBetResult(decimal Pays, string Label)
    {
        public bool Wins => Pays > 0m;
    }
}
