using blackjack.Engine;
using blackjack.Engine.SideBets;
using Spectre.Console;

namespace blackjack.Cli
{
    // Asks how the table should be set up. Every prompt has a default, so Enter all the way through works.
    // Amounts are whole chips, capped well short of where decimal arithmetic could overflow.
    internal static class TableSetup
    {
        public const int MaxChips = 1_000_000_000;

        // seed is the shuffle seed offered with the rules; the player can keep it or type another.
        public static TableSettings Ask(IAnsiConsole console, int seed, KeyBindings keys, string keysPath)
        {
            var name = console.Prompt(
                new TextPrompt<string>("Your name?")
                    .DefaultValue("Player")
                    .Validate(n => n.Trim().Length is > 0 and <= 20, "[red]Pick a name of 1 to 20 characters.[/]"))
                .Trim();

            var rules = new Rules();
            if (console.Confirm("Change the table rules?", defaultValue: false))
            {
                seed = AskRules(console, rules, seed);
            }

            var minBet = (int)rules.MinimumBet;
            var chips = console.Prompt(
                new TextPrompt<int>("Starting chips?")
                    .DefaultValue(Math.Max(1000, minBet))
                    .Validate(c => c >= minBet && c <= MaxChips, $"[red]Pick a whole number from {minBet} to {MaxChips}.[/]"));

            var bots = console.Prompt(
                new TextPrompt<int>($"Bots at the table? [grey](0-{Game.MaxPlayers - 1})[/]")
                    .DefaultValue(2)
                    .Validate(n => n is >= 0 and < Game.MaxPlayers, $"[red]Pick 0 to {Game.MaxPlayers - 1} bots.[/]"));

            var sideBets = new List<ISideBet> { SideBets.All[0], SideBets.All[1] };
            if (console.Confirm($"Change the side bets? [grey]({sideBets[0].Name} and {sideBets[1].Name})[/]", defaultValue: false))
            {
                AskSideBets(console, sideBets);
            }

            console.MarkupLine($"[grey]Keys: {KeyBindingsEditor.Summary(keys)}[/]");
            if (console.Confirm("Change the key bindings?", defaultValue: false))
            {
                KeyBindingsEditor.Edit(console, keys);
                try
                {
                    keys.Save(keysPath);
                    console.MarkupLine($"[grey]Saved to {Markup.Escape(keysPath)}[/]");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    console.MarkupLine($"[yellow]Couldn't save {Markup.Escape(keysPath)}: {Markup.Escape(e.Message)}[/]");
                }
            }

            return new TableSettings(name, chips, bots, rules, sideBets, seed);
        }

        // Fills in the rules and returns the shuffle seed to use.
        private static int AskRules(IAnsiConsole console, Rules rules, int seed)
        {
            rules.DeckCount = console.Prompt(
                new TextPrompt<int>("Decks in the shoe? [grey](1-8)[/]")
                    .DefaultValue(rules.DeckCount)
                    .Validate(n => n is >= 1 and <= 8, "[red]Pick 1 to 8 decks.[/]"));

            rules.MinimumBet = console.Prompt(
                new TextPrompt<int>("Minimum bet?")
                    .DefaultValue((int)rules.MinimumBet)
                    .Validate(b => b is > 0 and <= MaxChips, $"[red]Pick a whole number from 1 to {MaxChips}.[/]"));

            rules.DealerHitsSoft17 = console.Confirm("Dealer hits soft 17?", defaultValue: rules.DealerHitsSoft17);

            var payout = console.Prompt(
                new TextPrompt<string>("Blackjack pays?")
                    .AddChoices(new[] { "3:2", "6:5" })
                    .DefaultValue("3:2"));
            rules.BlackjackPayout = payout == "6:5" ? 1.2m : 1.5m;

            var penetration = console.Prompt(
                new TextPrompt<int>("Shoe penetration? [grey](% dealt before the reshuffle, 50-90)[/]")
                    .DefaultValue((int)(rules.Penetration * 100))
                    .Validate(p => p is >= 50 and <= 90, "[red]Pick 50 to 90.[/]"));
            rules.Penetration = penetration / 100m;

            rules.DoubleOnAnyTwo = console.Confirm("Double on any two cards? [grey](no: hard 9 to 11 only)[/]", defaultValue: rules.DoubleOnAnyTwo);
            rules.DoubleAfterSplit = console.Confirm("Double after a split?", defaultValue: rules.DoubleAfterSplit);

            rules.MaxSplitHands = console.Prompt(
                new TextPrompt<int>("Most hands after splitting? [grey](1-4, 1 turns splitting off)[/]")
                    .DefaultValue(rules.MaxSplitHands)
                    .Validate(n => n is >= 1 and <= 4, "[red]Pick 1 to 4 hands.[/]"));

            rules.ResplitAces = console.Confirm("Resplit aces?", defaultValue: rules.ResplitAces);
            rules.HitSplitAces = console.Confirm("Hit split aces?", defaultValue: rules.HitSplitAces);
            rules.SplitBlackjackPaysEven = console.Confirm("A 21 after a split pays even money?", defaultValue: rules.SplitBlackjackPaysEven);

            return console.Prompt(
                new TextPrompt<int>("Shuffle seed? [grey](the same seed deals the same shoes)[/]")
                    .DefaultValue(seed));
        }

        // Two of the catalogue, by name; the second must differ from the first.
        private static void AskSideBets(IAnsiConsole console, List<ISideBet> sideBets)
        {
            var names = SideBets.All.Select(b => b.Name).ToArray();
            var first = console.Prompt(
                new TextPrompt<string>("First side bet?")
                    .AddChoices(names)
                    .DefaultValue(sideBets[0].Name));

            var second = console.Prompt(
                new TextPrompt<string>("Second side bet?")
                    .AddChoices(names.Where(n => n != first))
                    .InvalidChoiceMessage("[red]Pick a bet that differs from the first.[/]")
                    .DefaultValue(first == sideBets[1].Name ? sideBets[0].Name : sideBets[1].Name));

            sideBets[0] = SideBets.Named(first);
            sideBets[1] = SideBets.Named(second);
        }
    }
}
