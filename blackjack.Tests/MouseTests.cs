using System.Text.RegularExpressions;
using blackjack.Cli;
using blackjack.Domain;
using blackjack.Engine;
using blackjack.Engine.SideBets;
using blackjack.Strategies;
using Spectre.Console;
using Spectre.Console.Testing;
using static blackjack.Tests.Cards;

namespace blackjack.Tests
{
    // Clicking the key hints in the bar: the terminal's mouse reports are decoded, matched against
    // where the last frame drew the hints, and turned into the keys the hints name.
    public class MouseTests
    {
        private static TestConsole NewConsole(int width = 110, int height = 30) =>
            new TestConsole().Width(width).Height(height).Interactive();

        private static ConsoleKeyInfo First(TestConsole console) =>
            console.Input.ReadKey(intercept: true) ?? throw new InvalidOperationException();

        [Theory]
        [InlineData("\u001b[<0;11;29M", 10, 28, true)]
        [InlineData("\u001b[<0;11;29m", 10, 28, false)]  // released
        [InlineData("\u001b[<4;1;1M", 0, 0, true)]        // with Shift held
        [InlineData("\u001b[<32;11;29M", 10, 28, false)] // dragged
        [InlineData("\u001b[<64;11;29M", 10, 28, false)] // wheel up
        [InlineData("\u001b[<65;11;29M", 10, 28, false)] // wheel down
        [InlineData("\u001b[<2;200;60M", 199, 59, false)] // right button
        public void Decoder_ReadsAReport_ToItsEnd(string report, int column, int row, bool pressed)
        {
            var console = NewConsole();
            console.Input.PushText(report);
            console.Input.PushKey(ConsoleKey.S);
            var taken = new List<ConsoleKeyInfo>();

            var click = MouseInput.TryDecode(First(console), console.Input, taken);

            Assert.Equal(new MouseClick(column, row, pressed), click);
            Assert.Equal(report.Length - 1, taken.Count);
            Assert.Equal(ConsoleKey.S, First(console).Key); // the key after the report is untouched
        }

        [Fact]
        public void Decoder_LeavesALoneEscapeAlone()
        {
            var console = NewConsole();
            console.Input.PushKey(ConsoleKey.Escape);
            var first = First(console);
            var taken = new List<ConsoleKeyInfo>();

            Assert.Null(MouseInput.TryDecode(first, console.Input, taken));
            Assert.Empty(taken);
            Assert.Equal(ConsoleKey.Escape, first.Key);
            Assert.Null(MouseInput.TryDecode(new ConsoleKeyInfo('D', ConsoleKey.D, false, false, false), console.Input, taken));
        }

        [Fact]
        public void Decoder_HandsBackTheKeysOfWhatIsNotAReport()
        {
            // Escape then a digit, as when a typed bet is cleared and typed again.
            var console = NewConsole();
            console.Input.PushKey(ConsoleKey.Escape);
            console.Input.PushKey(ConsoleKey.D4);
            var taken = new List<ConsoleKeyInfo>();

            Assert.Null(MouseInput.TryDecode(First(console), console.Input, taken));
            Assert.Equal(new[] { ConsoleKey.D4 }, taken.Select(k => k.Key));

            // A sequence that starts like a report and isn't one.
            console.Input.PushText("\u001b[<0;3x");
            taken.Clear();

            Assert.Null(MouseInput.TryDecode(First(console), console.Input, taken));
            Assert.Equal("[<0;3x", string.Concat(taken.Select(k => k.KeyChar)));

            // A report cut short.
            console.Input.PushText("\u001b[<0;3;");
            taken.Clear();

            Assert.Null(MouseInput.TryDecode(First(console), console.Input, taken));
            Assert.Equal("[<0;3;", string.Concat(taken.Select(k => k.KeyChar)));
        }

    }
}
