using Spectre.Console;

namespace blackjack.Cli
{
    // Rebinds keys from setup: press the new key for each action in turn.
    internal static class KeyBindingsEditor
    {
        public static string Summary(KeyBindings keys) =>
            $"{K(keys, KeyAction.Hit)} hit · {K(keys, KeyAction.Stand)} stand · {K(keys, KeyAction.Double)} double · "
            + $"{K(keys, KeyAction.Split)} split · {K(keys, KeyAction.Deal)} deal · "
            + $"{K(keys, KeyAction.BetDown)}/{K(keys, KeyAction.BetUp)} bet · "
            + $"{K(keys, KeyAction.SideBet1)}/{K(keys, KeyAction.SideBet2)} side bets · {K(keys, KeyAction.Insurance)} insurance · "
            + $"{K(keys, KeyAction.FastForward)} autopilot · {K(keys, KeyAction.NextShoe)} play out shoe · "
            + $"{K(keys, KeyAction.Book)} book · {K(keys, KeyAction.Count)} count · {K(keys, KeyAction.CashOut)} cash out";

        public static void Edit(IAnsiConsole console, KeyBindings keys)
        {
            console.MarkupLine("[grey]Press the key for each action. Enter keeps the current one, Escape stops.[/]");
            foreach (var action in Enum.GetValues<KeyAction>())
            {
                while (true)
                {
                    console.Markup($"  {KeyBindings.Describe(action)} [grey]({Markup.Escape(keys.Label(action))})[/]: ");
                    var info = console.Input.ReadKey(intercept: true)
                        ?? throw new InvalidOperationException("No console input is available.");

                    if (info.Key == ConsoleKey.Enter)
                    {
                        console.MarkupLine("[grey]kept[/]");
                        break;
                    }

                    if (info.Key == ConsoleKey.Escape)
                    {
                        console.MarkupLine("[grey]done[/]");
                        return;
                    }

                    if (!KeyChord.TryFrom(info, out var chord) || chord.IsReserved)
                    {
                        console.MarkupLine("[red]that key can't be bound[/]");
                        continue;
                    }

                    if (keys.Conflict(action, chord) is { } other)
                    {
                        console.MarkupLine($"[red]{Markup.Escape(chord.Label)} already does \"{KeyBindings.Describe(other)}\"[/]");
                        continue;
                    }

                    keys.Set(action, chord);
                    console.MarkupLine($"[yellow]{Markup.Escape(chord.Label)}[/]");
                    break;
                }
            }
        }

        private static string K(KeyBindings keys, KeyAction action) => $"[bold]{Markup.Escape(keys.Label(action))}[/]";
    }
}
