namespace blackjack.Engine.SideBets
{
    public enum SideBetTiming
    {
        // Settled as soon as the cards are dealt and the dealer has peeked.
        OnDeal,

        // Settled once the dealer's hand is final; the dealer plays it out for this even when no
        // player hand is left to beat.
        AfterDealer
    }
}
