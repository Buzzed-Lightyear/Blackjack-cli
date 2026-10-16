using blackjack.Domain;
using blackjack.Engine;
using blackjack.Strategies;
using static blackjack.Tests.Cards;

namespace blackjack.Tests
{
    public class CardCounterTests
    {
        [Theory]
        [InlineData("2s", 1)]
        [InlineData("6h", 1)]
        [InlineData("7d", 0)]
        [InlineData("9c", 0)]
        [InlineData("10s", -1)]
        [InlineData("Kh", -1)]
        [InlineData("As", -1)]
        public void HiLoValues(string card, int value)
        {
            Assert.Equal(value, CardCounter.HiLoValue(C(card)));
        }

        [Fact]
        public void RunningCount_SumsRevealedCards_AndResets()
        {
            var counter = new CardCounter();
            foreach (var card in Of("2s 3h 9d Kc 5s"))
            {
                counter.OnCardRevealed(card);
            }

            Assert.Equal(2, counter.RunningCount);
            counter.Reset();
            Assert.Equal(0, counter.RunningCount);
        }

        [Fact]
        public void TrueCount_DividesByDecksRemaining()
        {
            var counter = new CardCounter();
            foreach (var card in Of("2s 3h 4d 5c 6s 2h"))
            {
                counter.OnCardRevealed(card);
            }

            Assert.Equal(2m, counter.TrueCount(3m));
            Assert.Equal(24m, counter.TrueCount(0.25m));
            Assert.Equal(6m, counter.TrueCount(0m)); // nothing left to divide by
        }

        [Fact]
        public void HoleCard_IsOnlyCountedOnceTurnedOver()
        {
            // Player 10, 6; dealer 5 up, hole 4 (+1 hidden); player stands, dealer draws 2 then 9.
            var player = new Player("P", 100m, new BasicStrategyBot());
            var game = new Game(new Rules(), new[] { player }, Shoe.Stacked(Of("10h 5s 6c 4d 2c 9s")));
            var counter = new CardCounter();
            game.CardRevealed += counter.OnCardRevealed;

            game.PlaceBet(player, 10m);
            game.Deal();

            Assert.Equal(1, counter.RunningCount); // 10h -1, 5s +1, 6c +1

            game.Stand();

            Assert.Equal(Phase.Settled, game.Phase);
            Assert.Equal(3, counter.RunningCount); // + hole 4d +1, 2c +1, 9s 0
        }

        [Fact]
        public void HoleCard_DealtBeforeAMidRoundReshuffle_IsNotCounted()
        {
            // Player 10, 2; dealer 5 up, 6 in the hole. The stacked shoe is then empty, so the hit
            // reshuffles it and deals the 10 again. The 6 came out of the old shoe.
            var player = new Player("P", 100m, new BasicStrategyBot());
            var game = new Game(new Rules(), new[] { player }, Shoe.Stacked(Of("10h 5s 2c 6d")));
            var counter = new CardCounter();
            game.CardRevealed += counter.OnCardRevealed;
            game.Shuffled += counter.Reset;

            game.PlaceBet(player, 10m);
            game.Deal();
            game.Hit();

            Assert.True(player.Hands[0].IsBust);
            Assert.True(game.HoleCardRevealed);
            Assert.Equal(-1, counter.RunningCount); // only the second 10 came out of the new shoe
        }

        [Fact]
        public void HoleCard_DealtAfterAMidRoundReshuffle_IsCounted()
        {
            // The shoe runs out just before the hole card, so it comes from the new shoe:
            // 10 in the hole, then the dealer draws the 5 to reach 20.
            var player = new Player("P", 100m, new BasicStrategyBot());
            var game = new Game(new Rules(), new[] { player }, Shoe.Stacked(Of("10h 5s 2c")));
            var counter = new CardCounter();
            game.CardRevealed += counter.OnCardRevealed;
            game.Shuffled += counter.Reset;

            game.PlaceBet(player, 10m);
            game.Deal();
            game.Stand();

            Assert.Equal(20, game.DealerHand.Value);
            Assert.Equal(0, counter.RunningCount); // new shoe: 10h -1, 5s +1
        }

        [Fact]
        public void Shuffle_ResetsTheCount()
        {
            var player = new Player("P", 1000m, new BasicStrategyBot());
            var rules = new Rules { DeckCount = 1, Penetration = 0.05m };
            var game = new Game(rules, new[] { player }, new Shoe(1, 3, rules.Penetration));
            var counter = new CardCounter();
            var shuffles = 0;
            game.CardRevealed += counter.OnCardRevealed;
            game.Shuffled += counter.Reset;
            game.Shuffled += () => shuffles++;

            game.PlaceBet(player, 10m);
            game.Deal();
            while (game.Phase == Phase.PlayerTurn)
            {
                game.Stand();
            }

            // 5% penetration is reached by the initial deal, so the shoe is reshuffled as it ends.
            Assert.Equal(1, shuffles);
            Assert.Equal(0, counter.RunningCount);
            Assert.Equal(52, game.CardsRemaining);
        }
    }
}
