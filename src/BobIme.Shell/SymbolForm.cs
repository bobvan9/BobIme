using BobIme.Shell.Interop;

namespace BobIme.Shell;

/// <summary>
/// 符号面板：和候选窗一样不激活、不进任务栏，点了直接把符号送进原来打字的那个窗口，焦点不会跑掉。
/// </summary>
internal sealed class SymbolForm : FloatingForm
{
    private const int Inset = 12;
    private const int HeaderHeight = 34;
    private const int TabHeight = 30;
    private const int CellWidth = 46;
    private const int CellHeight = 44;
    private const int Columns = 16;
    private const int CloseWidth = 34;
    private const int TabPad = 18;
    private static readonly Size MaxMeasure = new(4000, 4000);

    private static readonly Color PanelColor = Color.FromArgb(0xF9, 0xF9, 0xF9);
    private static readonly Color InkColor = Color.FromArgb(0x1F, 0x1F, 0x1F);
    private static readonly Color AccentColor = Color.FromArgb(0x0F, 0x6C, 0xBD);
    private static readonly Color BorderColor = Color.FromArgb(0xC8, 0xCC, 0xD0);
    private static readonly Color HoverColor = Color.FromArgb(0xE4, 0xE7, 0xEA);
    private static readonly Color TabColor = Color.FromArgb(0x6E, 0x76, 0x81);
    private static readonly Color SelectedTabColor = Color.White;

    private readonly Font _headFont = new("Microsoft YaHei UI", 11f);
    private readonly Font _symbolFont = new("Microsoft YaHei UI", 18f);
    private readonly List<(Rectangle Bounds, string Text)> _cells = [];
    private readonly List<(Rectangle Bounds, int Index)> _tabs = [];
    private Rectangle _titleHot = Rectangle.Empty;
    private Rectangle _closeHot = Rectangle.Empty;
    private int _group;
    private int _hoverCell = -1;
    private int _hoverTab = -1;
    private bool _hoverClose;
    private bool _dragging;
    private Point _dragAt;

    public SymbolForm()
    {
        BackColor = PanelColor;
        DoubleBuffered = true;
    }

    /// <summary>按设置摆位置；已经在屏幕上就只挪位置，不抢焦点也不关掉重开。</summary>
    public void ShowAt(PanelPlacement placement)
    {
        RebuildLayout();
        var position = placement == PanelPlacement.Caret
            ? CaretLocator.Locate(ClientSize, Handle)
            : CaretLocator.Center(ClientSize);
        Native.SetWindowPos(
            Handle, Native.HWND_TOPMOST, position.X, position.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Show();
        Invalidate();
    }

    private void RebuildLayout()
    {
        _cells.Clear();
        _tabs.Clear();

        // 分组标签一行排不下就折行，窗宽取最宽的一行和候选网格里更大的那个。
        // 行数取各组里最多的那个：换分组时窗口尺寸不变，免得面板跟着挪位。
        var gridWidth = Columns * CellWidth;
        var x = Inset;
        var y = HeaderHeight;
        var rowWidth = 0;
        var widest = 0;
        var rows = 0;
        for (var i = 0; i < Symbols.Groups.Length; i++)
        {
            var width = TextRenderer.MeasureText(
                Symbols.Groups[i].Name, _headFont, MaxMeasure, TextFormatFlags.NoPadding).Width + TabPad;
            if (x + width > gridWidth + Inset && x > Inset)
            {
                widest = Math.Max(widest, rowWidth);
                x = Inset;
                y += TabHeight;
                rowWidth = 0;
            }

            _tabs.Add((new Rectangle(x, y, width, TabHeight), i));
            x += width;
            rowWidth = x - Inset;
            rows = Math.Max(rows, (Symbols.Groups[i].Items.Length + Columns - 1) / Columns);
        }

        widest = Math.Max(widest, rowWidth);
        var gridTop = y + TabHeight;
        var items = Symbols.Groups[_group].Items;
        ClientSize = new Size(Math.Max(gridWidth, widest) + Inset * 2, gridTop + rows * CellHeight + Inset);
        _closeHot = new Rectangle(ClientSize.Width - CloseWidth, 0, CloseWidth, HeaderHeight);
        _titleHot = new Rectangle(Inset, 0, ClientSize.Width - CloseWidth - Inset, HeaderHeight);

        for (var i = 0; i < items.Length; i++)
        {
            _cells.Add((
                new Rectangle(Inset + i % Columns * CellWidth, gridTop + i / Columns * CellHeight, CellWidth, CellHeight),
                items[i]));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(PanelColor);
        using (var pen = new Pen(BorderColor))
        {
            g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        TextRenderer.DrawText(
            g, "符号输入", _headFont, _titleHot, InkColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        DrawBox(g, _closeHot, "×", _headFont, TabColor, _hoverClose ? HoverColor : PanelColor);

        foreach (var (bounds, index) in _tabs)
        {
            var fill = index == _group ? SelectedTabColor : _hoverTab == index ? HoverColor : PanelColor;
            DrawBox(g, bounds, Symbols.Groups[index].Name, _headFont,
                index == _group ? AccentColor : TabColor, fill);
        }

        for (var i = 0; i < _cells.Count; i++)
        {
            DrawBox(g, _cells[i].Bounds, _cells[i].Text, _symbolFont, InkColor,
                i == _hoverCell ? HoverColor : PanelColor);
        }
    }

    /// <summary>画一格：先铺底色再写字，选中态和悬停态只差那层底色。</summary>
    private static void DrawBox(
        Graphics g, Rectangle bounds, string text, Font font, Color color, Color fill)
    {
        if (fill != PanelColor)
        {
            using var brush = new SolidBrush(fill);
            g.FillRectangle(brush, bounds);
        }

        TextRenderer.DrawText(
            g, text, font, bounds, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_closeHot.Contains(e.Location))
        {
            Hide();
            return;
        }

        var tab = HitTab(e.Location);
        if (tab >= 0)
        {
            _group = tab;
            RebuildLayout();
            Invalidate();
            return;
        }

        var cell = HitCell(e.Location);
        if (cell >= 0)
        {
            // 面板不激活，目标程序一直是前台，这一下就直接落在他的光标上。
            TextInserter.SendText(_cells[cell].Text);
            return;
        }

        if (e.Y < HeaderHeight)
        {
            _dragging = true;
            _dragAt = e.Location;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Location = new Point(Location.X + e.X - _dragAt.X, Location.Y + e.Y - _dragAt.Y);
            _dragAt = e.Location;
            return;
        }

        var cell = HitCell(e.Location);
        var tab = cell >= 0 ? -1 : HitTab(e.Location);
        var close = cell < 0 && tab < 0 && _closeHot.Contains(e.Location);
        Cursor = cell >= 0 || tab >= 0 || close ? Cursors.Hand : Cursors.Default;
        if (cell == _hoverCell && tab == _hoverTab && close == _hoverClose)
        {
            return;
        }

        _hoverCell = cell;
        _hoverTab = tab;
        _hoverClose = close;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverCell = -1;
        _hoverTab = -1;
        _hoverClose = false;
        Invalidate();
    }

    private int HitCell(Point point)
    {
        for (var i = 0; i < _cells.Count; i++)
        {
            if (_cells[i].Bounds.Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    private int HitTab(Point point)
    {
        for (var i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].Bounds.Contains(point))
            {
                return _tabs[i].Index;
            }
        }

        return -1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _headFont.Dispose();
            _symbolFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
