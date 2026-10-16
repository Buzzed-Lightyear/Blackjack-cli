using blackjack.Engine;

namespace blackjack.Strategies
{
    // Builds bots by type, names them for the screen, and hands out the Random each one plays with.
    public static class Bots
    {
        public static IReadOnlyList<BotType> All { get; } = Enum.GetValues<BotType>();

        public static IPlayerStrategy Create(BotType type, Rules rules, Random random)
        {
            ArgumentNullException.ThrowIfNull(rules);
            ArgumentNullException.ThrowIfNull(random);
            return type switch
            {
                BotType.Basic => new BasicStrategyBot(rules),
                BotType.Counter => new CounterBot(rules),
                BotType.Cautious => new CautiousBot(),
                BotType.Mimic => new MimicBot(rules),
                BotType.Gambler => new GamblerBot(rules, random),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a bot type.")
            };
        }

        // The type as setup and the summary spell it: "counter".
        public static string Name(BotType type) => type.ToString().ToLowerInvariant();

        // The short name shown under a bot's seat.
        public static string Tag(BotType type) => type switch
        {
            BotType.Basic => "book",
            BotType.Counter => "count",
            BotType.Cautious => "safe",
            BotType.Mimic => "mimic",
            BotType.Gambler => "gambler",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a bot type.")
        };

        // The type a strategy was built as, or null for a seat that isn't a bot.
        public static BotType? TypeOf(IPlayerStrategy strategy) => strategy switch
        {
            BasicStrategyBot => BotType.Basic,
            CounterBot => BotType.Counter,
            CautiousBot => BotType.Cautious,
            MimicBot => BotType.Mimic,
            GamblerBot => BotType.Gambler,
            _ => null
        };

        // By name, in any case: "counter", "Gambler".
        public static bool TryParse(string name, out BotType type)
        {
            ArgumentNullException.ThrowIfNull(name);
            foreach (var candidate in All)
            {
                if (candidate.ToString().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    type = candidate;
                    return true;
                }
            }

            type = default;
            return false;
        }

        public static BotType Parse(string name) =>
            TryParse(name, out var type) ? type : throw new ArgumentException($"There is no bot type called \"{name}\".", nameof(name));

        // The Random for the bot in a seat, from the session seed, so the same seed seats bots that
        // make the same choices; seat 0 draws the mix of types. HashCode.Combine would do the mixing,
        // but it is randomized per process.
        public static Random RandomFor(int seed, int seat)
        {
            unchecked
            {
                var h = (uint)seed * 0xCC9E2D51u + (uint)seat * 0x1B873593u;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 13;
                return new Random((int)h);
            }
        }
    }
}
