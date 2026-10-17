using System.Runtime.InteropServices;
using Spectre.Console;

namespace blackjack.Cli
{
    // One report from the terminal's mouse: the cell it happened on, 0-based, and whether it was
    // the left button going down. Releases, drags and wheel turns are reports too, but not presses.
    internal readonly record struct MouseClick(int Column, int Row, bool Pressed);

    // Turns the terminal's mouse reporting on and off, and decodes the reports it then sends. With
    // button reporting in SGR form on (xterm modes 1000 and 1006), each press and release arrives
    // on stdin as ESC [ < button ; column ; row M (m for a release), and the console hands those
    // characters over one at a time as if they had been typed.
    internal static class MouseInput
    {
        private const char Escape = '\u001b';
        private const string On = "\u001b[?1000h\u001b[?1006h";
        private const string Off = "\u001b[?1006l\u001b[?1000l";
        private const int StdInputHandle = -10;
        private const uint EnableVirtualTerminalInput = 0x0200;

        // Button bits of a report: 0-1 the button (0 is the left one), 2-4 the modifier keys,
        // 5 motion, 6 and 7 the wheel and the extra buttons.
        private const int NotALeftPress = 0b1110_0011;

        // The input mode Windows had before Enable, to put back.
        private static uint? savedMode;

        // Asks the terminal to report clicks, and says whether it will. On Windows only Windows
        // Terminal does (it sets WT_SESSION), and only in virtual terminal input mode; the legacy
        // console is left as it is.
        public static bool Enable(IAnsiConsole console)
        {
            if (!console.Profile.Capabilities.Ansi)
            {
                return false;
            }

            if (OperatingSystem.IsWindows())
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION")))
                {
                    return false;
                }

                var handle = GetStdHandle(StdInputHandle);
                if (!GetConsoleMode(handle, out var mode) || !SetConsoleMode(handle, mode | EnableVirtualTerminalInput))
                {
                    return false;
                }

                savedMode = mode;
            }

            console.WriteAnsi(On);
            return true;
        }

        public static void Disable(IAnsiConsole console)
        {
            if (!console.Profile.Capabilities.Ansi)
            {
                return;
            }

            console.WriteAnsi(Off);
            if (OperatingSystem.IsWindows() && savedMode is { } mode)
            {
                SetConsoleMode(GetStdHandle(StdInputHandle), mode);
                savedMode = null;
            }
        }

        // Reads the rest of the report that first may begin: Escape with the report's other
        // characters already waiting. Null when it isn't one, such as a lone Escape; the keys read
        // while finding that out are in taken, so the caller can still pass them on in order.
        public static MouseClick? TryDecode(ConsoleKeyInfo first, IAnsiConsoleInput input, List<ConsoleKeyInfo> taken)
        {
            if (first.KeyChar != Escape || !Next(input, taken, out var c) || c != '[' || !Next(input, taken, out c) || c != '<')
            {
                return null;
            }

            var fields = new int[3];
            var field = 0;
            var digits = 0;
            while (Next(input, taken, out c))
            {
                if (c is >= '0' and <= '9' && digits < 5)
                {
                    fields[field] = fields[field] * 10 + (c - '0');
                    digits++;
                }
                else if (c == ';' && field < 2 && digits > 0)
                {
                    field++;
                    digits = 0;
                }
                else if (c is 'M' or 'm' && field == 2 && digits > 0)
                {
                    var pressed = c == 'M' && (fields[0] & NotALeftPress) == 0;
                    return new MouseClick(fields[1] - 1, fields[2] - 1, pressed);
                }
                else
                {
                    return null;
                }
            }

            return null;
        }

        private static bool Next(IAnsiConsoleInput input, List<ConsoleKeyInfo> taken, out char c)
        {
            c = '\0';
            if (!input.IsKeyAvailable() || input.ReadKey(intercept: true) is not { } key)
            {
                return false;
            }

            taken.Add(key);
            c = key.KeyChar;
            return true;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    }
}
