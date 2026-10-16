using blackjack.Domain;
using blackjack.Engine;
using blackjack.Engine.SideBets;
using blackjack.Strategies;

namespace blackjack.Tests
{
    public class SimulationTests
    {
        private const int Rounds = 20_000;
        private const decimal BuyIn = 1_000m;

        private sealed record Simulation(decimal Net, decimal Wagered, int Splits, int Doubles, int Insured, decimal BiggestBet)
        {
            public decimal Edge => Net / Wagered;
        }

        // Five bots of one type play 20,000 rounds from a seed. Each buys in for 1,000 chips and
        // again whenever it can't cover the minimum, so a gambler's Martingale is capped by a real
        // stack; the first bot also stakes both side bets. Every round checks the table's
        // invariants. Net is the main game's, insurance included; Wagered is what went down on hands.
        private static Simulation Simulate(BotType type, int seed)
        {
            var rules = new Rules();
            var bots = Enumerable.Range(1, 5)
                .Select(i => new Player($"Bot {i}", BuyIn, Bots.Create(type, rules, Bots.RandomFor(seed, i))))
                .ToList();
            var sideBets = new[] { SideBets.All[0], SideBets.All[1] };
            var game = new Game(rules, bots, new Shoe(rules.DeckCount, seed, rules.Penetration), sideBets);

            var revealedSinceShuffle = new List<Card>();
            var counter = new CardCounter();
            game.CardRevealed += counter.OnCardRevealed;
            game.CardRevealed += revealedSinceShuffle.Add;
            game.Shuffled += counter.Reset;
            game.Shuffled += revealedSinceShuffle.Clear;
            TableContext Table() => new(rules, counter.RunningCount, counter.TrueCount(game.DecksRemaining), game.DecksRemaining);

            var wagered = 0m;
            var net = 0m;
            var splits = 0;
            var doubles = 0;
            var insured = 0;
            var biggestBet = 0m;
            for (var round = 0; round < Rounds; round++)
            {
                if (game.Phase == Phase.Settled)
                {
                    game.NewRound();
                }

                foreach (var bot in bots.Where(b => b.Chips < rules.MinimumBet))
                {
                    bot.Receive(BuyIn);
                }

                var chipsBefore = bots.Sum(b => b.Chips);
                foreach (var bot in bots)
                {
                    var bet = bot.Strategy.DecideBet(bot.Chips, Table());
                    Assert.InRange(bet, rules.MinimumBet, bot.Chips);
                    game.PlaceBet(bot, bet);
                    biggestBet = Math.Max(biggestBet, bet);
                }

                foreach (var bet in sideBets)
                {
                    if (bots[0].Chips >= rules.MinimumBet)
                    {
                        game.PlaceSideBet(bots[0], bet, rules.MinimumBet);
                    }
                }

                game.Deal();
                while (game.Phase == Phase.Insurance)
                {
                    var player = game.CurrentPlayer!;
                    if (player.Strategy.DecideInsurance(player.Hands[0], Table()))
                    {
                        insured++;
                        game.TakeInsurance();
                    }
                    else
                    {
                        game.DeclineInsurance();
                    }
                }

                while (game.Phase == Phase.PlayerTurn)
                {
                    var options = new MoveOptions(game.CanDouble, game.CanSplit, game.CanHit);
                    switch (game.CurrentPlayer!.Strategy.DecideMove(game.CurrentHand!, game.DealerUpcard!, options, Table()))
                    {
                        case Move.Hit:
                            game.Hit();
                            break;
                        case Move.Stand:
                            game.Stand();
                            break;
                        case Move.Double:
                            doubles++;
                            game.Double();
                            break;
                        default:
                            splits++;
                            game.Split();
                            break;
                    }
                }

                Assert.Equal(Phase.Settled, game.Phase);
                Assert.True(game.HoleCardRevealed);

                var hands = bots.SelectMany(b => b.Hands).ToList();
                Assert.All(hands, h => Assert.NotNull(h.Outcome));
                Assert.All(hands.Where(h => h.IsBust), h => Assert.Equal(Outcome.Lose, h.Outcome));
                Assert.All(hands.Where(h => h.IsDoubled), h => Assert.Equal(3, h.Cards.Count));
                Assert.All(bots, b => Assert.InRange(b.Hands.Count, 1, rules.MaxSplitHands));
                Assert.All(bots[0].SideBets, s => Assert.True(s.IsResolved));

                // If any hand was still live, the dealer played out to 17 or more.
                var dealerPlayed = !game.DealerHand.IsNatural && hands.Any(h => !h.IsBust && h.Outcome != Outcome.Blackjack);
                if (dealerPlayed)
                {
                    Assert.True(game.DealerHand.Value >= 17);
                }

                Assert.Equal(chipsBefore + bots.Sum(b => b.RoundNet), bots.Sum(b => b.Chips));
                Assert.Equal(revealedSinceShuffle.Sum(CardCounter.HiLoValue), counter.RunningCount);

                wagered += hands.Sum(h => h.Bet);
                net += bots.Sum(b => b.RoundNet - b.SideBetNet);
            }

            return new Simulation(net, wagered, splits, doubles, insured, biggestBet);
        }

        // The bands come from running each type on seeds 1 to 11 and 12345 (100,000 hands each)
        // and taking the mean with a margin of at least four standard deviations across those
        // runs, so they don't flake while still catching a payout bug:
        //   basic     mean -0.27%  sd 0.37%  (the book gives up about half a percent)
        //   counter   mean +0.86%  sd 0.68%  (the bet spread widens the swings)
        //   cautious  mean -5.94%  sd 0.37%
        //   mimic     mean -5.62%  sd 0.37%
        //   gambler   mean -0.73%  sd 1.72%  (a stack that gets ahead doubles into bets in the
        //             tens of thousands, so the result is heavy-tailed and the band has to be wide)
        [Theory]
        [InlineData(BotType.Basic, -0.020, 0.013)]
        [InlineData(BotType.Counter, -0.020, 0.037)]
        [InlineData(BotType.Cautious, -0.075, -0.044)]
        [InlineData(BotType.Mimic, -0.072, -0.041)]
        [InlineData(BotType.Gambler, -0.080, 0.070)]
        public void BotsPlayManyRounds_ChipsAddUp_AndTheHouseKeepsItsEdge(BotType type, double low, double high)
        {
            var result = Simulate(type, 12345);

            Assert.InRange((double)result.Edge, low, high);
            if (type is BotType.Cautious or BotType.Mimic)
            {
                Assert.Equal(0, result.Splits);
                Assert.Equal(0, result.Doubles);
            }
            else
            {
                Assert.True(result.Splits > 0);
                Assert.True(result.Doubles > 0);
            }

            if (type is BotType.Counter or BotType.Gambler)
            {
                Assert.True(result.BiggestBet > 10m);
                Assert.True(result.Insured > 0);
            }
            else
            {
                Assert.Equal(10m, result.BiggestBet);
                Assert.Equal(0, result.Insured);
            }
        }
    }
}
