namespace blackjack.Cli
{
    // How the rows and columns of the table are shared out at a console size. Everything here
    // depends on the size and the number of seats alone, never on what a frame shows, so no region
    // moves while the terminal stays the same. The header and the bar are fixed; the dealer and your
    // seat grow with the cards; the event log gets whatever is left, and always at least one row.
    internal sealed class TableLayout
    {
        public const int MinWidth = 60;
        public const int MinHeight = 16;
        public const int TilesHeight = 23;
        public const int TallTilesHeight = 36;
        public const int OneSeatRowWidth = 90;
        public const int CountPanelWidth = 70;
        public const int CountWidth = 26;
        public const int HeaderRows = 1;
        public const int BarRows = 4;

        // seatLines is how many grid rows each seat takes (its name, cards and status).
        public TableLayout(int width, int height, int seats, int seatLines)
        {
            Width = width;
            Height = height;
            CountPanel = width >= CountPanelWidth;
            TwoSeatRows = width < OneSeatRowWidth && seats > 4;
            TileHeight = height >= TallTilesHeight ? CardArt.TallTileHeight
                : height >= TilesHeight ? CardArt.TileHeight
                : 0;

            // The biggest cards that leave the log a row, and two seat rows only when they fit too.
            while (TileHeight > 0 && Spare() < 1)
            {
                TileHeight = TileHeight == CardArt.TallTileHeight ? CardArt.TileHeight : 0;
            }

            if (Spare() < 1)
            {
                TwoSeatRows = false;
            }

            DealerRows = TileHeight == 0 ? 2 : TileHeight + 2;
            PanelRows = TileHeight == 0 ? 5 : TileHeight + 4;
            EventRows = Spare();

            // Only on 16 rows with the count under your seat: the dealer gives up its blank row.
            if (EventRows < 1)
            {
                DealerRows--;
                EventRows++;
            }

            int Spare() =>
                height - HeaderRows - (TileHeight == 0 ? 2 : TileHeight + 2) - SeatRows * seatLines
                - (TileHeight == 0 ? 5 : TileHeight + 4) - BarRows - (CountPanel ? 0 : 1);
        }

        public int Width { get; }
        public int Height { get; }
        public bool TooSmall => Width < MinWidth || Height < MinHeight;

        // 0 for one-line chips, otherwise the height of the tiles the dealer and you get.
        public int TileHeight { get; }
        public int DealerRows { get; }

        // The other seats wrap onto a second grid when the terminal is narrow.
        public bool TwoSeatRows { get; }
        public int SeatRows => TwoSeatRows ? 2 : 1;

        // Your seat's panel, borders included.
        public int PanelRows { get; }

        // The Hi-Lo count sits in a panel beside your seat, or on one line under it when narrow.
        public bool CountPanel { get; }
        public int YouRows => PanelRows + (CountPanel ? 0 : 1);
        public int PanelWidth => CountPanel ? Width - CountWidth : Width;
        public int EventRows { get; }
    }
}
