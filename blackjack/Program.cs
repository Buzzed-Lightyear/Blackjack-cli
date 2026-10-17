using System.Text;
using blackjack.Cli;
using Spectre.Console;

// Usage: blackjack [--fast] [--seed <n>] [--shoe <n>] [--no-mouse] [--keys] [--keys-file <path>]
//   --fast              no pauses between bot moves and dealer draws
//   --seed <n>          shuffle the shoes from this seed, to replay the same cards; without it one
//                       is picked at random, and shown, so any session can be replayed
//   --shoe <n>          start at shoe n of that seed instead of shoe 1
//   --no-mouse          don't ask the terminal to report clicks on the bar's key hints
//   --keys              print the key bindings and where they're read from, then exit
//   --keys-file <path>  read and save key bindings in this file instead of keys.ini

Console.OutputEncoding = Encoding.UTF8;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("Usage: blackjack [--fast] [--seed <n>] [--shoe <n>] [--no-mouse] [--keys] [--keys-file <path>]");
    return 0;
}

var keysPath = KeyBindings.ResolvePath(Value("--keys-file"));
var warnings = new List<string>();
var keys = KeyBindings.Load(keysPath, warnings);

if (args.Contains("--keys"))
{
    var table = new Table().Border(TableBorder.Rounded).AddColumn("Action").AddColumn("Keys").AddColumn("In the file as");
    foreach (var action in Enum.GetValues<KeyAction>())
    {
        table.AddRow(
            KeyBindings.Describe(action),
            Markup.Escape(string.Join(", ", keys.Get(action).Select(k => k.Label))),
            KeyBindings.IniName(action));
    }

    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine(File.Exists(keysPath)
        ? $"Read from [bold]{Markup.Escape(keysPath)}[/]"
        : $"No {KeyBindings.FileName} found, so these are the defaults. Changing them in setup saves to [bold]{Markup.Escape(keysPath)}[/]");
    foreach (var warning in warnings)
    {
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(warning)}[/]");
    }

    return 0;
}

// Every session has a seed, so that any of them can be replayed.
var seed = Random.Shared.Next();
if (Value("--seed") is { } seedText && !int.TryParse(seedText, out seed))
{
    Console.Error.WriteLine($"--seed needs a whole number, not \"{seedText}\".");
    return 1;
}

var firstShoe = 1;
if (Value("--shoe") is { } shoeText && (!int.TryParse(shoeText, out firstShoe) || firstShoe < 1))
{
    Console.Error.WriteLine($"--shoe needs a whole number of 1 or more, not \"{shoeText}\".");
    return 1;
}

if (!AnsiConsole.Profile.Capabilities.Interactive)
{
    Console.Error.WriteLine("Blackjack needs an interactive terminal.");
    return 1;
}

var pause = args.Contains("--fast") ? TimeSpan.Zero : TimeSpan.FromMilliseconds(700);

new BlackjackApp(AnsiConsole.Console, pause, seed, firstShoe, keys, keysPath, warnings, mouse: !args.Contains("--no-mouse")).Run();
return 0;

string? Value(string flag)
{
    var at = Array.IndexOf(args, flag);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}
