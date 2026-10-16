namespace blackjack.Domain
{
    public class Shoe
    {
        public const int CardsPerDeck = 52;

        private readonly IReadOnlyList<Card> template;
        private readonly int? seed;
        private readonly int reshuffleAt;
        private Card[] cards = Array.Empty<Card>();
        private int next;

        // penetration is the fraction of the shoe dealt before the cut card comes out. The shoes are
        // numbered from firstShoe, and each one's order depends only on the seed and its number, so
        // shoe 3 of a seed is the same shoe however shoes 1 and 2 were played.
        public Shoe(int deckCount, int seed, decimal penetration = 0.75m, int firstShoe = 1)
            : this(BuildDecks(deckCount), seed, penetration, firstShoe)
        {
        }

        private Shoe(IReadOnlyList<Card> template, int? seed, decimal penetration, int firstShoe)
        {
            if (penetration <= 0m || penetration > 1m)
            {
                throw new ArgumentOutOfRangeException(nameof(penetration), "Penetration must be above 0 and at most 1.");
            }

            ArgumentOutOfRangeException.ThrowIfLessThan(firstShoe, 1);

            this.template = template;
            this.seed = seed;
            reshuffleAt = (int)Math.Round(template.Count * (1m - penetration));
            ShoeNumber = firstShoe - 1;
            Shuffle();
        }

        // Deals exactly these cards in this order, and Shuffle() puts them back in the same order.
        // Lets tests rig naturals and busts.
        public static Shoe Stacked(params Card[] order) => new(order.ToArray(), null, 1m, 1);

        public int ShoeNumber { get; private set; }
        public int CardsRemaining => cards.Length - next;
        public bool NeedsReshuffle => CardsRemaining <= reshuffleAt;

        // Brings out the next shoe.
        public void Shuffle()
        {
            ShoeNumber++;
            cards = template.ToArray();
            next = 0;
            if (seed is { } s)
            {
                new Random(Mix(s, ShoeNumber)).Shuffle(cards);
            }
        }

        public Card Deal()
        {
            if (CardsRemaining == 0)
            {
                throw new InvalidOperationException("The shoe is empty; shuffle before dealing.");
            }

            return cards[next++];
        }

        private static List<Card> BuildDecks(int deckCount)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(deckCount, 1);

            var decks = new List<Card>(deckCount * CardsPerDeck);
            for (var d = 0; d < deckCount; d++)
            {
                foreach (var suit in Enum.GetValues<Suit>())
                {
                    foreach (var rank in Enum.GetValues<Rank>())
                    {
                        decks.Add(new Card(rank, suit));
                    }
                }
            }

            return decks;
        }

        // One Random seed from the seed and the shoe number. HashCode.Combine would do the mixing,
        // but it is randomized per process, so the same shoe would never come out twice.
        private static int Mix(int seed, int shoeNumber)
        {
            unchecked
            {
                var h = (uint)seed * 0x9E3779B1u + (uint)shoeNumber;
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                return (int)h;
            }
        }
    }
}
