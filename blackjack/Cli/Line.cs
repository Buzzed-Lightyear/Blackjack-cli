using Spectre.Console;
using Spectre.Console.Rendering;

namespace blackjack.Cli
{
    // One line of markup that is cut short with an ellipsis instead of wrapping, so the rows a
    // region of the table takes never depend on what is written in it.
    internal sealed class Line : IRenderable
    {
        private readonly IRenderable markup;

        public Line(string markup, Justify justify = Justify.Left)
        {
            this.markup = new Markup(markup).Justify(justify);
        }

        public Measurement Measure(RenderOptions options, int maxWidth) => markup.Measure(options, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            var width = markup.Measure(options, int.MaxValue).Max;
            if (width <= maxWidth)
            {
                return markup.Render(options, maxWidth);
            }

            // Rendered at its own width nothing wraps; the first line is then cut to fit.
            var kept = new List<Segment>();
            var room = maxWidth - 1;
            foreach (var segment in markup.Render(options, width))
            {
                if (segment.IsLineBreak)
                {
                    break;
                }

                if (segment.CellCount() <= room)
                {
                    kept.Add(segment);
                    room -= segment.CellCount();
                    continue;
                }

                var text = segment.Text;
                while (text.Length > 0 && new Segment(text, segment.Style).CellCount() > room)
                {
                    text = text[..^1];
                }

                if (text.Length > 0)
                {
                    kept.Add(new Segment(text, segment.Style));
                }

                break;
            }

            kept.Add(new Segment("…"));
            return kept;
        }
    }
}
