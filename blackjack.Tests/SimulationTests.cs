using blackjack.Domain;
using blackjack.Engine;
using blackjack.Strategies;

namespace blackjack.Tests
{
    public class SimulationTests
    {
        [Fact]
        public void BotsPlayManyRounds_ChipsAddUp_AndTheHouseKeepsItsEdge()
        {
            var rules = new Rules();
            var bots = Enumerable.Range(1, 5)
                .Select(i => new Player($"Bot {i}", 1_000_000m, new BasicStrategyBot()))
                .ToList();
            var game = new Game(rules, bots, new Shoe(rules.DeckCount, 12345, rules.Penetration));

            var revealedSinceShuffle = new List<Card>();
            var counter = new CardCounter();
            game.CardRevealed += counter.OnCardRevealed;
            game.CardRevealed += revealedSinceShuffle.Add;
            game.Shuffled += counter.Reset;
            game.Shuffled += revealedSinceShuffle.Clear;

            var wagered = 0m;
            var net = 0m;
            for (var round = 0; round < 20_000; round++)
            {
                if (game.Phase == Phase.Settled)
                {
                    game.NewRound();
                }

                var chipsBefore = bots.Sum(b => b.Chips);
                foreach (var bot in bots)
                {
                    game.PlaceBet(bot, bot.Strategy.DecideBet(bot.Chips, rules.MinimumBet));
                }

                game.Deal();
                while (game.Phase == Phase.PlayerTurn)
                {
                    var move = game.CurrentPlayer!.Strategy.DecideMove(game.CurrentHand!, game.DealerUpcard!);
                    if (move == Move.Hit)
                    {
                        game.Hit();
                    }
                    else
                    {
                        game.Stand();
                    }
                }

                Assert.Equal(Phase.Settled, game.Phase);
                Assert.True(game.HoleCardRevealed);

                var hands = bots.Select(b => b.Hands.Single()).ToList();
                Assert.All(hands, h => Assert.NotNull(h.Outcome));
                Assert.All(hands.Where(h => h.IsBust), h => Assert.Equal(Outcome.Lose, h.Outcome));

                // If any hand was still live, the dealer played out to 17 or more.
                var dealerPlayed = !game.DealerHand.IsNatural && hands.Any(h => !h.IsBust && h.Outcome != Outcome.Blackjack);
                if (dealerPlayed)
                {
                    Assert.True(game.DealerHand.Value >= 17);
                }

                var roundNet = hands.Sum(h => h.Payout - h.Bet);
                Assert.Equal(chipsBefore + roundNet, bots.Sum(b => b.Chips));
                Assert.Equal(revealedSinceShuffle.Sum(CardCounter.HiLoValue), counter.RunningCount);

                wagered += hands.Sum(h => h.Bet);
                net += roundNet;
            }

            // Hit/stand-only basic strategy gives up roughly 2 to 3 percent; a payout bug would show up here.
            Assert.InRange(net / wagered, -0.05m, -0.005m);
        }
    }
}
