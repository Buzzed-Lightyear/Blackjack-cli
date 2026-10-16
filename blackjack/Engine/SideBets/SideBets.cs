namespace blackjack.Engine.SideBets
{
    // Every side bet a table can offer.
    public static class SideBets
    {
        public static IReadOnlyList<ISideBet> All { get; } = new ISideBet[]
        {
            new TwentyOnePlusThree(),
            new BustIt(),
            new PerfectPairs(),
            new LuckyLadies(),
            new RoyalMatch(),
            new LuckyLucky()
        };

        public static ISideBet Named(string name) =>
            All.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"There is no side bet called \"{name}\".", nameof(name));
    }
}
