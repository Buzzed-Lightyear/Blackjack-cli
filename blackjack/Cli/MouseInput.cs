using System.Runtime.InteropServices;
using Spectre.Console;

namespace blackjack.Cli
{
    // One report from the terminal's mouse: the cell it happened on, 0-based, and whether it was
    // the left button going down. Releases, drags and wheel turns are reports too, but not presses.
    internal readonly record struct MouseClick(int Column, int Row, bool Pressed);

    // Turns the terminal's mouse reporting on and off: button reporting in SGR form (xterm modes
    // 1000 and 1006), whose reports TerminalInput decodes.
    internal static class MouseInput
    {
        private const string On = "\u001b[?1000h\u001b[?1006h";
        private const string Off = "\u001b[?1006l\u001b[?1000l";
        private const int StdInputHandle = -10;
        private const uint EnableVirtualTerminalInput = 0x0200;

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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    }
}
