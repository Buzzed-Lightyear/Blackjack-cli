using blackjack.Domain;
using blackjack.Engine;
using blackjack.Engine.SideBets;
using static blackjack.Tests.Cards;

namespace blackjack.Tests
{
    public class SideBetTests
    {
        private static SideBetResult Evaluate(ISideBet bet, string player, string dealer) =>
            bet.Evaluate(Of(player), Of(dealer));

        [Fact]
        public void Catalogue_ListsEveryBetOnce_InOrder()
        {
            Assert.Equal(
                new[] { "21+3", "Bust It", "Perfect Pairs", "Lucky Ladies", "Royal Match", "Lucky Lucky" },
                SideBets.All.Select(b => b.Name));
            Assert.All(SideBets.All, b => Assert.False(string.IsNullOrWhiteSpace(b.Description)));
            Assert.Same(SideBets.All[1], SideBets.Named("bust it"));
            Assert.Throws<ArgumentException>(() => SideBets.Named("Over/Under"));
            Assert.Equal(SideBetTiming.AfterDealer, SideBets.All[1].Timing);
            Assert.All(SideBets.All.Where(b => b.Name != "Bust It"), b => Assert.Equal(SideBetTiming.OnDeal, b.Timing));
        }

        [Theory]
        [InlineData("8h 8c", "8d", 30, "three of a kind")]
        [InlineData("8h 8c", "8d 10s", 30, "three of a kind")] // only the upcard counts
        [InlineData("8h Qc", "8d", 0, "no hand")]
        public void Stake_PaysTheOdds_OnTopOfTheStake(string player, string dealer, int pays, string label)
        {
            var result = Evaluate(SideBets.All[0], player, dealer);

            Assert.Equal(pays, result.Pays);
            Assert.Equal(label, result.Label);
            Assert.Equal(pays > 0, result.Wins);
        }

        [Theory]
        [InlineData("7h 7h", "7h", 100, "suited three of a kind")]
        [InlineData("5s 6s", "7s", 40, "straight flush")]
        [InlineData("Qd Kd", "Ad", 40, "straight flush")]
        [InlineData("Ac 2c", "3c", 40, "straight flush")]
        [InlineData("Kh Kc", "Kd", 30, "three of a kind")]
        [InlineData("Jh Qc", "Kd", 10, "straight")]
        [InlineData("2h Ac", "3d", 10, "straight")]
        [InlineData("Kh Ac", "2d", 0, "no hand")] // the ace doesn't wrap round
        [InlineData("2h 9h", "Kh", 5, "flush")]
        [InlineData("2h 2c", "9d", 0, "no hand")]
        public void TwentyOnePlusThree_PaysTheThreeCardPokerHand(string player, string dealer, int pays, string label)
        {
            var result = Evaluate(new TwentyOnePlusThree(), player, dealer);

            Assert.Equal(pays, result.Pays);
            Assert.Equal(label, result.Label);
        }

        [Theory]
        [InlineData("10h 6c 9d", 2, "3-card bust")]
        [InlineData("2h 4c 5d 6s 10h", 4, "5-card bust")]
        [InlineData("2h 2c 2d 2s 5h 10d", 12, "6-card bust")]
        [InlineData("2h 2c 2d 2s 3h 3d 10c", 50, "7-card bust")]
        [InlineData("2h 2c 2d 2s 3h 3d 2h 10c", 250, "8-card bust")]
        [InlineData("Ah 2c 3d 4s 5h 6d 7c 10s", 250, "8-card bust")]
        [InlineData("10h 7c", 0, "no bust")]
        [InlineData("10h 6c 5d", 0, "no bust")]
        [InlineData("Ah Kc", 0, "dealer blackjack")]
        public void BustIt_PaysByTheNumberOfCardsInTheBust(string dealer, int pays, string label)
        {
            var result = new BustIt().Evaluate(Of("10h 7c"), Of(dealer));

            Assert.Equal(pays, result.Pays);
            Assert.Equal(label, result.Label);
        }

        [Theory]
        [InlineData("8h 8h", 25, "perfect pair")]
        [InlineData("8h 8d", 12, "coloured pair")]
        [InlineData("8c 8s", 12, "coloured pair")]
        [InlineData("8h 8c", 6, "mixed pair")]
        [InlineData("Kh Qh", 0, "no pair")]
        public void PerfectPairs_PaysByHowWellThePairMatches(string player, int pays, string label)
        {
            var result = Evaluate(new PerfectPairs(), player, "5d");

            Assert.Equal(pays, result.Pays);
            Assert.Equal(label, result.Label);
        }

        [Theory]
        [InlineData("Qh Qh", "Ad Ks", 1000, "queens of hearts against a dealer blackjack")]
        [InlineData("Qh Qh", "Ad", 125, "queens of hearts")]
        [InlineData("Qh Qh", "Ad 9s", 125, "queens of hearts")]
        [InlineData("Kd Kd", "Ad Ks", 19, "matched 20")]
        [InlineData("Kd Qd", "5d", 9, "suited 20")]
        [InlineData("Ah 9h", "5d", 9, "suited 20")]
        [InlineData("Kd Qs", "5d", 4, "20")]
        [InlineData("Ah 9c", "5d", 4, "20")]
        [InlineData("Kd 9s", "5d", 0, "no 20")]
        [InlineData("Ah Kc", "5d", 0, "no 20")]
        public void LuckyLadies_PaysATwentyMoreForTheLadies(string player, string dealer, int pays, string label)
        {
            var result = Evaluate(new LuckyLadies(), player, dealer);

            Assert.Equal(pays, result.Pays);
            Assert.Equal(label, result.Label);
        }

        [Theory]
        [InlineData("Kh Qh", 25, "royal match")]
        [InlineData("Qs Ks", 25, "royal match")]
        [InlineData("2h Qh", 2.5, "suited")]
        [InlineData("Kh Qs", 0, "unsuited")]
        public void RoyalMatch_PaysSuitedCards(string player, double pays, string label)
        {
            var result = Evaluate(new RoyalMatch(), player, "5d");

            Assert.Equal((decimal)pays, result.Pays);
            Assert.Equal(label, result.Label);
        }

        [Theory]
        [InlineData("7h 7h", "7h", 200, "suited 7-7-7")]
        [InlineData("6c 7c", "8c", 100, "suited 6-7-8")]
        [InlineData("7h 7c", "7d", 50, "7-7-7")]
        [InlineData("8h 6c", "7d", 30, "6-7-8")]
        [InlineData("Kd Ad", "10d", 15, "suited 21")]
        [InlineData("Kd 5s", "6h", 3, "21")]
        [InlineData("Ah Ac", "9d", 3, "21")] // one ace as 11, one as 1
        [InlineData("Kd 5s", "5h", 2, "20")]
        [InlineData("Ah 8c", "10d", 2, "19")]
        [InlineData("Kd 5s", "3h", 0, "no 19, 20 or 21")]
        [InlineData("Kd Qs", "Jh", 0, "no 19, 20 or 21")]
        public void LuckyLucky_PaysNineteenToTwentyOne_WithTwoSpecials(string player, string dealer, int pays, string label)
        {
            var result = Evaluate(new LuckyLucky(), player, dealer);

            Assert.Equal(pays, result.Pays);
            Assert.Equal(label, result.Label);
        }

        // Every ordered draw of the player's two cards and the dealer's cards from the 52 card
        // types, weighted by the copies a six-deck shoe holds, gives the exact house edge.
        [Theory]
        [InlineData(0, 1, 4.62)]  // 21+3
        [InlineData(2, 1, 6.11)]  // Perfect Pairs
        [InlineData(3, 2, 24.71)] // Lucky Ladies, whose top prize needs the dealer's hole card
        [InlineData(4, 1, 6.67)]  // Royal Match
        [InlineData(5, 1, 2.66)]  // Lucky Lucky
        public void DealTimeBets_HaveTheirSixDeckHouseEdge(int index, int dealerCards, double edgePercent)
        {
            var bet = SideBets.All[index];
            var types = Enum.GetValues<Suit>().SelectMany(s => Enum.GetValues<Rank>().Select(r => new Card(r, s))).ToArray();
            var copies = new int[types.Length];
            Array.Fill(copies, 6);
            var draw = new Card[2 + dealerCards];
            var player = new ArraySegment<Card>(draw, 0, 2);
            var dealer = new ArraySegment<Card>(draw, 2, dealerCards);
            double weighted = 0, total = 0;

            void Enumerate(int position, double weight)
            {
                if (position == draw.Length)
                {
                    var result = bet.Evaluate(player, dealer);
                    weighted += weight * (double)(result.Wins ? result.Pays : -1m);
                    total += weight;
                    return;
                }

                for (var i = 0; i < types.Length; i++)
                {
                    if (copies[i] == 0)
                    {
                        continue;
                    }

                    draw[position] = types[i];
                    copies[i]--;
                    Enumerate(position + 1, weight * (copies[i] + 1));
                    copies[i]++;
                }
            }

            Enumerate(0, 1);

            Assert.Equal(edgePercent, -100 * weighted / total, 2);
        }

        // Bust It depends on how the dealer plays, so it is sampled: dealer hands from fresh
        // six-deck shoes, with nobody else at the table.
        [Theory]
        [InlineData(true, 0.056, 0.068)]
        [InlineData(false, 0.076, 0.090)]
        public void BustIt_HouseEdge_DependsOnTheSoft17Rule(bool hitsSoft17, double low, double high)
        {
            var bet = new BustIt();
            var shoe = new Shoe(6, new Random(2024), 0.75m);
            const int hands = 200_000;
            var net = 0m;

            for (var i = 0; i < hands; i++)
            {
                if (shoe.NeedsReshuffle)
                {
                    shoe.Shuffle();
                }

                var dealer = new Hand();
                dealer.Add(shoe.Deal());
                dealer.Add(shoe.Deal());
                while (dealer.Value < 17 || (dealer.Value == 17 && dealer.IsSoft && hitsSoft17))
                {
                    dealer.Add(shoe.Deal());
                }

                var result = bet.Evaluate(Array.Empty<Card>(), dealer.Cards);
                net += result.Wins ? result.Pays : -1m;
            }

            Assert.InRange((double)(-net / hands), low, high);
        }
    }
}
