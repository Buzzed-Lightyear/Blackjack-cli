namespace blackjack.Strategies
{
    // What the table allows for the hand being played. CanHit is only false for split aces that
    // may be split again but not hit, so the choice is between splitting and standing.
    public readonly record struct MoveOptions(bool CanDouble, bool CanSplit, bool CanHit = true);
}
