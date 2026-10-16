using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CP2077SaveEditor.Utils;

/// <summary>
/// The single source of truth for how the editor looks.
///
/// Call <see cref="Apply"/> once per form, after InitializeComponent(). It walks the
/// control tree, restyles every control by type, and hooks ControlAdded so controls
/// built at runtime (inventory rows, appearance pickers, dialogs) are themed the
/// moment they appear. Designer files still carry their original light colors --
/// those are simply overwritten here rather than edited in twenty places.
/// </summary>
public static class Theme
{
    // ---- palette ---------------------------------------------------------------
    // Charcoal base, Cyberpunk yellow as the primary accent, cyan for hover/focus.

    public static readonly Color Base = Hex(0x15161A);   // window background
    public static readonly Color Surface = Hex(0x1E2026);   // panels, lists, cards
    public static readonly Color Raised = Hex(0x252830);   // inputs, hovered rows
    public static readonly Color Line = Hex(0x32363F);   // hairline borders
    public static readonly Color LineStrong = Hex(0x454B57);   // emphasised borders

    public static readonly Color Accent = Hex(0xFCEE0A);   // primary: yellow
    public static readonly Color Accent2 = Hex(0x00E5FF);   // hover / focus: cyan
    public static readonly Color Danger = Hex(0xFF2E63);   // errors, destructive
    public static readonly Color Ok = Hex(0x3DDC84);   // success

    public static readonly Color Text = Hex(0xE8E8ED);
    public static readonly Color TextMuted = Hex(0x8A8D98);
    public static readonly Color TextDim = Hex(0x5C606B);   // disabled
    public static readonly Color OnAccent = Hex(0x15161A);   // text on an accent fill

    public static readonly Font UiFont = new("Segoe UI", 9f);
    public static readonly Font UiFontBold = new("Segoe UI Semibold", 9f);

    private static Color Hex(int rgb) => Color.FromArgb(0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    /// <summary>Blend two colors. <paramref name="t"/> 0 returns <paramref name="a"/>, 1 returns <paramref name="b"/>.</summary>
    public static Color Mix(Color a, Color b, float t)
    {
        if (t <= 0f) return a;
        if (t >= 1f) return b;
        return Color.FromArgb(
            a.A + (int)((b.A - a.A) * t),
            a.R + (int)((b.R - a.R) * t),
            a.G + (int)((b.G - a.G) * t),
            a.B + (int)((b.B - a.B) * t));
    }

    /// <summary>A rounded-rectangle path. Radius is clamped so it can never exceed the box.</summary>
    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, Math.Min(radius, Math.Min(r.Width, r.Height) / 2)) * 2;

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    // ---- process-wide setup ----------------------------------------------------

    /// <summary>
    /// Switches WinForms' own common controls to dark. This is what makes scrollbars,
    /// NumericUpDown spin buttons, the TabControl strip and the common dialogs dark --
    /// none of them honour BackColor, so no amount of per-control theming reaches them.
    /// Must run before the first window is created.
    /// </summary>
    public static void InitProcess()
    {
#pragma warning disable WFO5001 // SetColorMode is [Experimental] in the current SDK.
        try
        {
            Application.SetColorMode(SystemColorMode.Dark);
        }
        catch (Exception)
        {
            // Older runtime, or the switch was removed: the manual theming below still applies.
        }
#pragma warning restore WFO5001
    }

    // ---- the walker ------------------------------------------------------------

    // Tracks which containers already have a ControlAdded hook. A weak table so forms
    // and controls stay collectable; ListView.Tag is off limits (it holds VirtualInfo).
    private static readonly ConditionalWeakTable<Control, object> Walked = new();
    private static readonly ConditionalWeakTable<Control, object> Decorated = new();

    public static void Apply(Control root)
    {
        if (root is null)
        {
            return;
        }

        Style(root);

        if (Walked.TryGetValue(root, out _) == false)
        {
            Walked.Add(root, new object());
            root.ControlAdded += (_, e) => Apply(e.Control);
        }

        foreach (Control child in root.Controls)
        {
            Apply(child);
        }
    }

    private static void Style(Control c)
    {
        EnableDoubleBuffer(c);

        switch (c)
        {
            // Our own controls paint themselves straight from this palette.
            case ModernButton:
            case ModernValuePicker:
                return;

            case Form form:
                form.BackColor = Base;
                form.ForeColor = Text;
                form.Font = UiFont;
                DarkTitleBar(form);
                return;

            case UserControl uc:
                uc.BackColor = Base;
                uc.ForeColor = Text;
                return;

            case GroupBox box:
                // GroupBox draws its frame with system colors, so paint over it entirely.
                box.ForeColor = TextMuted;
                box.BackColor = Surface;
                Once(box, () => box.Paint += PaintGroupBox);
                return;

            case TabControl tabs:
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabs.ItemSize = new Size(tabs.ItemSize.Width, 26);
                tabs.SizeMode = TabSizeMode.Normal;
                tabs.BackColor = Surface;
                Once(tabs, () => tabs.DrawItem += DrawTabItem);
                return;

            case TabPage page:
                page.BackColor = Surface;
                page.ForeColor = Text;
                return;

            case ListView list:
                list.BackColor = Surface;
                list.ForeColor = Text;
                list.BorderStyle = BorderStyle.FixedSingle;
                // Items keep their default rendering (several of these are virtual-mode
                // lists); only the column header is redrawn, because that is the one
                // part that stays light grey on a dark background.
                list.OwnerDraw = true;
                Once(list, () =>
                {
                    list.DrawColumnHeader += DrawListHeader;
                    list.DrawItem += (_, e) => e.DrawDefault = true;
                    list.DrawSubItem += (_, e) => e.DrawDefault = true;
                });
                return;

            case TreeView tree:
                tree.BackColor = Surface;
                tree.ForeColor = Text;
                tree.LineColor = LineStrong;
                tree.BorderStyle = BorderStyle.FixedSingle;
                return;

            case ListBox box2:
                box2.BackColor = Raised;
                box2.ForeColor = Text;
                box2.BorderStyle = BorderStyle.FixedSingle;
                return;

            case TextBox tb:
                tb.BackColor = Raised;
                // Silver is this app's "showing placeholder text" marker, and the search
                // boxes compare against it to tell a placeholder from a real query. Map it
                // to the dim tone rather than overwriting it, or the search boxes would
                // start searching for the word "Search".
                tb.ForeColor = tb.ForeColor == Color.Silver ? TextDim : Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
                return;

            case NumericUpDown num:
                num.BackColor = Raised;
                num.ForeColor = Text;
                num.BorderStyle = BorderStyle.FixedSingle;
                return;

            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = Raised;
                combo.ForeColor = Text;
                return;

            case CheckBox check:
                check.FlatStyle = FlatStyle.Flat;
                check.BackColor = Color.Transparent;
                check.ForeColor = Text;
                return;

            case Button btn:
                btn.FlatStyle = FlatStyle.Flat;
                btn.BackColor = Raised;
                btn.ForeColor = Text;
                btn.FlatAppearance.BorderColor = Line;
                btn.FlatAppearance.MouseOverBackColor = Mix(Raised, Accent2, 0.18f);
                return;

            case StatusStrip strip:
                strip.BackColor = Surface;
                strip.ForeColor = TextMuted;
                strip.Renderer = new ThemedToolStripRenderer();
                strip.SizingGrip = false;
                return;

            case ToolStrip ts:
                ts.BackColor = Surface;
                ts.ForeColor = Text;
                ts.Renderer = new ThemedToolStripRenderer();
                return;

            case Label label:
                label.BackColor = Color.Transparent;
                if (label.ForeColor == SystemColors.ControlText || label.ForeColor == Color.Black)
                {
                    label.ForeColor = Text;
                }
                return;

            case Panel panel:
                // A bordered panel reads as a card; a bare one is just a layout box and
                // should disappear into whatever it sits on.
                panel.BackColor = panel.BorderStyle == BorderStyle.None ? Base : Surface;
                panel.ForeColor = Text;
                return;

            case PictureBox pic:
                pic.BackColor = Surface;
                return;

            default:
                c.ForeColor = Text;
                return;
        }
    }

    /// <summary>Runs <paramref name="action"/> the first time it is requested for <paramref name="c"/>.</summary>
    private static void Once(Control c, Action action)
    {
        if (Decorated.TryGetValue(c, out _))
        {
            return;
        }

        Decorated.Add(c, new object());
        action();
    }

    /// <summary>
    /// Turns on the protected DoubleBuffered flag. This is the single biggest win for
    /// "smooth": it stops lists, trees and panels flickering while they scroll or relayout.
    /// </summary>
    private static void EnableDoubleBuffer(Control c)
    {
        try
        {
            var prop = typeof(Control).GetProperty(
                "DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            prop?.SetValue(c, true, null);
        }
        catch (Exception)
        {
            // Not fatal -- just means this control keeps flickering.
        }
    }

    // ---- custom painting -------------------------------------------------------

    private static void PaintGroupBox(object sender, PaintEventArgs e)
    {
        var box = (GroupBox)sender;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Cover the system-drawn frame, then draw ours.
        g.Clear(box.Parent?.BackColor ?? Base);

        var body = new Rectangle(0, 7, box.Width - 1, box.Height - 8);
        using (var path = RoundedRect(body, 4))
        using (var fill = new SolidBrush(Surface))
        using (var pen = new Pen(Line))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        if (string.IsNullOrEmpty(box.Text))
        {
            return;
        }

        var size = TextRenderer.MeasureText(box.Text, UiFontBold);
        var label = new Rectangle(10, 0, size.Width + 8, 14);

        using (var fill = new SolidBrush(box.Parent?.BackColor ?? Base))
        {
            g.FillRectangle(fill, label);
        }

        TextRenderer.DrawText(g, box.Text, UiFontBold, new Point(14, 0), TextMuted);
    }

    private static void DrawListHeader(object sender, DrawListViewColumnHeaderEventArgs e)
    {
        var g = e.Graphics;
        var hot = e.State.HasFlag(ListViewItemStates.Selected);

        using (var fill = new SolidBrush(hot ? Raised : Surface))
        {
            g.FillRectangle(fill, e.Bounds);
        }

        using (var pen = new Pen(Line))
        {
            // Separator between columns, and a baseline under the whole header.
            g.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
            g.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        var text = new Rectangle(e.Bounds.X + 7, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
        TextRenderer.DrawText(g, e.Header?.Text ?? string.Empty, UiFontBold, text, TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private static void DrawTabItem(object sender, DrawItemEventArgs e)
    {
        var tabs = (TabControl)sender;
        var page = tabs.TabPages[e.Index];
        var selected = tabs.SelectedIndex == e.Index;
        var hot = e.State.HasFlag(DrawItemState.HotLight);
        var g = e.Graphics;

        var bounds = e.Bounds;
        bounds.Inflate(2, 2);

        using (var fill = new SolidBrush(selected ? Surface : Base))
        {
            g.FillRectangle(fill, bounds);
        }

        if (selected)
        {
            // Accent underline rather than a box: cheaper to read, and it points at the page.
            using var accent = new SolidBrush(Accent);
            g.FillRectangle(accent, bounds.X, bounds.Bottom - 2, bounds.Width, 2);
        }

        var color = selected ? Text : hot ? Accent2 : TextMuted;
        TextRenderer.DrawText(g, page.Text, selected ? UiFontBold : UiFont, bounds, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    // ---- window chrome ---------------------------------------------------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>Paints the title bar dark, so the window does not wear a white hat.</summary>
    private static void DarkTitleBar(Form form)
    {
        void Set()
        {
            try
            {
                var on = 1;
                DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
            }
            catch (Exception)
            {
                // Pre-20H1 Windows: light title bar, everything else still themed.
            }
        }

        if (form.IsHandleCreated)
        {
            Set();
        }

        Once(form, () => form.HandleCreated += (_, _) => Set());
    }

    /// <summary>Dark colors for StatusStrip / ToolStrip, which ignore BackColor on their own.</summary>
    private sealed class ThemedToolStripRenderer : ToolStripProfessionalRenderer
    {
        public ThemedToolStripRenderer() : base(new ThemedColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // A single hairline along the top edge instead of a raised 3D border.
            using var pen = new Pen(Line);
            e.Graphics.DrawLine(pen, 0, 0, e.ToolStrip.Width, 0);
        }
    }

    private sealed class ThemedColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Surface;
        public override Color ToolStripGradientMiddle => Surface;
        public override Color ToolStripGradientEnd => Surface;
        public override Color StatusStripGradientBegin => Surface;
        public override Color StatusStripGradientEnd => Surface;
        public override Color ToolStripBorder => Line;
        public override Color ToolStripDropDownBackground => Raised;
        public override Color ImageMarginGradientBegin => Raised;
        public override Color ImageMarginGradientMiddle => Raised;
        public override Color ImageMarginGradientEnd => Raised;
        public override Color MenuItemSelected => Mix(Raised, Accent2, 0.22f);
        public override Color MenuItemSelectedGradientBegin => Mix(Raised, Accent2, 0.22f);
        public override Color MenuItemSelectedGradientEnd => Mix(Raised, Accent2, 0.22f);
        public override Color MenuItemBorder => Accent2;
        public override Color MenuBorder => Line;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Line;
    }
}

/// <summary>
/// One shared 60 fps ticker for every animation in the app.
///
/// A control registers a step function, sets a target, and calls <see cref="Wake"/>.
/// The timer stops itself as soon as nothing is moving, so an idle window costs nothing.
/// </summary>
internal static class Animator
{
    private sealed class Entry
    {
        public Control Control;
        public Func<bool> Step;
    }

    private static readonly List<Entry> Entries = new();
    private static readonly Timer Ticker = new() { Interval = 16 };

    static Animator()
    {
        Ticker.Tick += Tick;
    }

    public static void Register(Control control, Func<bool> step)
    {
        Entries.Add(new Entry { Control = control, Step = step });
    }

    /// <summary>Call after changing an animation target.</summary>
    public static void Wake()
    {
        Ticker.Enabled = true;
    }

    private static void Tick(object sender, EventArgs e)
    {
        var moving = false;

        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            var entry = Entries[i];

            if (entry.Control.IsDisposed)
            {
                Entries.RemoveAt(i);
                continue;
            }

            if (!entry.Step())
            {
                continue;
            }

            moving = true;

            if (entry.Control.IsHandleCreated)
            {
                entry.Control.Invalidate();
            }
        }

        if (!moving)
        {
            Ticker.Enabled = false;
        }
    }
}

/// <summary>
/// A 0..1 value that eases toward its target instead of snapping. This is what turns
/// hover and press states from a hard colour swap into a fade.
/// </summary>
internal sealed class Eased
{
    private readonly float _rate;

    public Eased(float rate = 0.22f)
    {
        _rate = rate;
    }

    public float Value { get; private set; }

    public float Target { get; set; }

    /// <summary>Advances one frame. Returns true while the value is still moving.</summary>
    public bool Step()
    {
        var delta = Target - Value;

        if (Math.Abs(delta) < 0.004f)
        {
            if (Value == Target)
            {
                return false;
            }

            Value = Target;
            return true;
        }

        Value += delta * _rate;
        return true;
    }
}
