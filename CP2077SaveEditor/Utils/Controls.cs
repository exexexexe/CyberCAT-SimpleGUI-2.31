using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CP2077SaveEditor.Utils;

namespace CP2077SaveEditor
{
    /// <summary>How a <see cref="ModernButton"/> presents itself.</summary>
    public enum ButtonVariant
    {
        /// <summary>Neutral surface with a hairline border. The default.</summary>
        Default,

        /// <summary>Accent-filled. For the one action that matters on a screen.</summary>
        Primary,

        /// <summary>Flat list row, for the side menu. Honours <see cref="ModernButton.Active"/>.</summary>
        Nav,

        /// <summary>Borderless, text only.</summary>
        Ghost,

        /// <summary>Use the explicit DefaultColor / HoverColor / TextColor properties instead of the palette.</summary>
        Custom,
    }

    /// <summary>
    /// An owner-drawn button with eased hover and press states.
    ///
    /// Still a Panel, so every existing designer file keeps compiling. Colors now come
    /// from <see cref="Theme"/> via <see cref="Variant"/>; the older DefaultColor /
    /// HoverColor / TextColor properties are consulted only when Variant is
    /// <see cref="ButtonVariant.Custom"/>, which is why the light values still sitting
    /// in the designer files no longer have any effect.
    /// </summary>
    [DefaultEvent("Click")]
    public class ModernButton : Panel
    {
        private readonly Eased _hover = new(0.26f);
        private readonly Eased _press = new(0.38f);

        private string _text = string.Empty;
        private Font _textFont = Theme.UiFont;
        private Color _defaultColor = Theme.Raised;
        private Color _hoverColor = Theme.Accent2;
        private Color _textColor = Theme.Text;
        private ButtonVariant _variant = ButtonVariant.Default;
        private bool _active;
        private bool _clickEffectEnabled = true;
        private bool _isDown;

        public ModernButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);

            // The base class raises Click from its own mouse handling; we do it ourselves
            // in OnMouseUp so a press that slides off the button does not count as a click.
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);

            base.BorderStyle = System.Windows.Forms.BorderStyle.None;
            BackColor = Theme.Base;
            TabStop = true;
            Cursor = Cursors.Hand;

            Animator.Register(this, () => _hover.Step() | _press.Step());
        }

        [Browsable(true)]
        [Category("Style")]
        public override string Text
        {
            get => _text;
            set
            {
                _text = value ?? string.Empty;
                Invalidate();
            }
        }

        [Browsable(true)]
        [Category("Style")]
        [DefaultValue(ButtonVariant.Default)]
        public ButtonVariant Variant
        {
            get => _variant;
            set
            {
                _variant = value;
                Invalidate();
            }
        }

        /// <summary>Marks the current page in a <see cref="ButtonVariant.Nav"/> menu.</summary>
        [Browsable(true)]
        [Category("Style")]
        public bool Active
        {
            get => _active;
            set
            {
                if (_active == value)
                {
                    return;
                }

                _active = value;
                Invalidate();
            }
        }

        [Browsable(true)]
        [Category("Style")]
        public Color DefaultColor
        {
            get => _defaultColor;
            set
            {
                _defaultColor = value;
                Invalidate();
            }
        }

        [Browsable(true)]
        [Category("Style")]
        public Color HoverColor
        {
            get => _hoverColor;
            set
            {
                _hoverColor = value;
                Invalidate();
            }
        }

        [Browsable(true)]
        [Category("Style")]
        public Color TextColor
        {
            get => _textColor;
            set
            {
                _textColor = value;
                Invalidate();
            }
        }

        [Browsable(true)]
        [Category("Style")]
        public Font TextFont
        {
            get => _textFont;
            set
            {
                _textFont = value ?? Theme.UiFont;
                Invalidate();
            }
        }

        [Browsable(true)]
        [Category("Style")]
        public bool ClickEffectEnabled
        {
            get => _clickEffectEnabled;
            set => _clickEffectEnabled = value;
        }

        /// <summary>
        /// Swallowed on purpose: the border is painted from the palette, and letting the
        /// window frame draw a system-coloured one on top undoes the theme.
        /// </summary>
        public new BorderStyle BorderStyle
        {
            get => System.Windows.Forms.BorderStyle.None;
            set => base.BorderStyle = System.Windows.Forms.BorderStyle.None;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var parentColor = Parent?.BackColor ?? Theme.Base;
            g.Clear(parentColor);

            var hover = Enabled ? _hover.Value : 0f;
            var press = Enabled ? _press.Value : 0f;

            ResolveColors(hover, press, out var fill, out var border, out var foreColor);

            var box = new Rectangle(0, 0, Width - 1, Height - 1);

            // A pressed button sinks a pixel rather than changing shape.
            var sink = (int)Math.Round(press * (_clickEffectEnabled ? 1f : 0f));

            using (var path = Theme.RoundedRect(box, _variant == ButtonVariant.Nav ? 0 : 4))
            {
                using (var brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                if (border != Color.Transparent)
                {
                    using var pen = new Pen(border);
                    g.DrawPath(pen, path);
                }
            }

            if (_variant == ButtonVariant.Nav && _active)
            {
                // Accent bar on the leading edge marks the open page.
                using var brush = new SolidBrush(Theme.Accent);
                g.FillRectangle(brush, 0, 0, 3, Height);
            }

            if (string.IsNullOrEmpty(_text))
            {
                return;
            }

            var isNav = _variant == ButtonVariant.Nav;

            var textBox = new Rectangle(
                isNav ? 14 : 4,
                sink,
                Width - (isNav ? 18 : 8),
                Height);

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                        (isNav ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);

            TextRenderer.DrawText(g, _text, _textFont, textBox, foreColor, flags);

            if (Focused && ShowFocusCues)
            {
                var ring = new Rectangle(2, 2, Width - 5, Height - 5);
                using var path = Theme.RoundedRect(ring, isNav ? 0 : 3);
                using var pen = new Pen(Theme.Accent2) { DashStyle = DashStyle.Dot };
                g.DrawPath(pen, path);
            }
        }

        private void ResolveColors(float hover, float press, out Color fill, out Color border, out Color foreColor)
        {
            var parentColor = Parent?.BackColor ?? Theme.Base;

            if (!Enabled)
            {
                var flat = _variant is ButtonVariant.Nav or ButtonVariant.Ghost;
                fill = flat ? parentColor : Theme.Surface;
                border = flat ? Color.Transparent : Theme.Line;
                foreColor = Theme.TextDim;
                return;
            }

            switch (_variant)
            {
                case ButtonVariant.Primary:
                    fill = Theme.Mix(Theme.Accent, Color.White, hover * 0.22f);
                    fill = Theme.Mix(fill, Theme.OnAccent, press * 0.18f);
                    border = Theme.Mix(Theme.Accent, Color.White, hover * 0.35f);
                    foreColor = Theme.OnAccent;
                    return;

                case ButtonVariant.Nav:
                    fill = Theme.Mix(
                        _active ? Theme.Surface : parentColor,
                        Theme.Raised,
                        Math.Max(hover, press * 0.6f));
                    border = Color.Transparent;
                    foreColor = _active
                        ? Theme.Accent
                        : Theme.Mix(Theme.TextMuted, Theme.Text, hover);
                    return;

                case ButtonVariant.Ghost:
                    fill = Theme.Mix(parentColor, Theme.Raised, hover * 0.8f);
                    border = Color.Transparent;
                    foreColor = Theme.Mix(Theme.TextMuted, Theme.Accent2, hover);
                    return;

                case ButtonVariant.Custom:
                    fill = Theme.Mix(_defaultColor, _hoverColor, hover);
                    border = Theme.Line;
                    foreColor = _textColor;
                    return;

                default:
                    fill = Theme.Mix(Theme.Raised, Theme.Mix(Theme.Raised, Theme.Accent2, 0.20f), hover);
                    fill = Theme.Mix(fill, Theme.Surface, press * 0.5f);
                    border = Theme.Mix(Theme.Line, Theme.Accent2, hover);
                    foreColor = Theme.Mix(Theme.Text, Theme.Accent2, hover * 0.65f);
                    return;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover.Target = 1f;
            Animator.Wake();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover.Target = 0f;
            _press.Target = 0f;
            _isDown = false;
            Animator.Wake();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _isDown = true;
            _press.Target = 1f;
            Animator.Wake();
            Focus();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            var wasDown = _isDown;
            _isDown = false;
            _press.Target = 0f;
            Animator.Wake();

            if (wasDown && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            {
                OnClick(EventArgs.Empty);
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode is not (Keys.Space or Keys.Enter))
            {
                return;
            }

            e.Handled = true;
            _press.Target = 1f;
            Animator.Wake();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);

            if (e.KeyCode is not (Keys.Space or Keys.Enter))
            {
                return;
            }

            e.Handled = true;
            _press.Target = 0f;
            Animator.Wake();
            OnClick(EventArgs.Empty);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            _press.Target = 0f;
            Animator.Wake();
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);

            if (!Enabled)
            {
                _hover.Target = 0f;
                _press.Target = 0f;
            }

            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    /// <summary>
    /// The appearance-tab spinner: a name, a value, and an arrow on each side.
    /// Behaviour is unchanged; the painting now comes from <see cref="Theme"/> and the
    /// arrow hover fades instead of snapping.
    /// </summary>
    public class ModernValuePicker : Panel
    {
        //Painting
        private readonly Font[] fonts = new Font[]
        {
            new Font("Segoe UI", 9f),
            new Font("Segoe UI", 15f),
            new Font("Segoe UI Semibold", 9f),
        };

        private readonly Eased _leftHover = new(0.3f);
        private readonly Eased _rightHover = new(0.3f);

        private bool[] gStates = new bool[] { false, false, false, false, false };
        private bool[] enabledStates = new bool[] { true, true };
        private Rectangle leftRect = new Rectangle(1, 1, 1, 1);
        private Rectangle rightRect = new Rectangle(1, 1, 1, 1);

        //Other
        private PickerValueType _type = PickerValueType.Integer;
        private int _index = 0;
        private int _maxInt = 99;
        private int _minInt = 0;
        private string[] _stringCollection = new string[] { };
        private string _stringValue = "00";

        public delegate void IndexChangedHandler(ModernValuePicker sender);
        public event IndexChangedHandler IndexChanged;

        public string PickerName { get; set; } = "Name";

        public string StringValue
        {
            get
            {
                return _stringValue;
            }
            set
            {
                _stringValue = value;
                this.Invalidate();
            }
        }

        public bool SuppressIndexChange { get; set; } = false;

        public PickerValueType PickerType
        {
            get
            {
                return _type;
            }
            set
            {
                _type = value;
                SuppressIndexChange = true;
                Index = 0;
            }
        }

        public int Index
        {
            get
            {
                return _index;
            }
            set
            {
                if (PickerType == PickerValueType.Integer)
                {
                    if (value > MaxIntegerValue)
                    {
                        _index = MaxIntegerValue;
                    }
                    else if (value < MinIntegerValue)
                    {
                        _index = MinIntegerValue;
                    }
                    else
                    {
                        _index = value;
                    }
                    StringValue = _index.ToString("00");
                }
                else
                {
                    if (value > StringCollection.Length - 1)
                    {
                        _index = 0;
                    }
                    else if (value < 0)
                    {
                        _index = StringCollection.Length - 1;
                    }
                    else
                    {
                        _index = value;
                    }

                    if (StringCollection.Length < 1)
                    {
                        StringValue = string.Empty;
                    }
                    else
                    {
                        StringValue = StringCollection[_index];
                    }
                }

                if (SuppressIndexChange == true)
                {
                    SuppressIndexChange = false;
                }
                else
                {
                    if (IndexChanged != null) { IndexChanged(this); }
                }
            }
        }

        public string[] StringCollection
        {
            get
            {
                return _stringCollection;
            }
            set
            {
                _stringCollection = value;
                if (PickerType == PickerValueType.String)
                {
                    SuppressIndexChange = true;
                    Index = 0;
                }
            }
        }

        public int MaxIntegerValue
        {
            get
            {
                return _maxInt;
            }
            set
            {
                _maxInt = value;
                if (PickerType == PickerValueType.Integer && Index > _maxInt)
                {
                    Index = _maxInt;
                }
            }
        }

        public int MinIntegerValue
        {
            get
            {
                return _minInt;
            }
            set
            {
                _minInt = value;
                if (PickerType == PickerValueType.Integer && Index < _minInt)
                {
                    Index = _minInt;
                }
            }
        }

        public ModernValuePicker()
        {
            PickerName = "Name";
            StringValue = "00";
            Size = new Size(200, 100);

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            DoubleBuffered = true;
            BackColor = Theme.Surface;

            Animator.Register(this, () => _leftHover.Step() | _rightHover.Step());
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            leftRect = new Rectangle(10, (this.Height / 5) * 3 - 25, 20, 50);
            rightRect = new Rectangle(this.Width - 30, (this.Height / 5) * 3 - 25, 20, 50);

            var leftSize = TextRenderer.MeasureText("<", fonts[0]);
            var rightSize = TextRenderer.MeasureText(">", fonts[0]);
            var valueSize = TextRenderer.MeasureText(StringValue.ToUpper(), fonts[1]);
            var nameSize = TextRenderer.MeasureText(PickerName.ToUpper(), fonts[2]);

            DrawArrow(g, leftRect, "<", leftSize, _leftHover.Value, gStates[2], enabledStates[0]);
            DrawArrow(g, rightRect, ">", rightSize, _rightHover.Value, gStates[3], enabledStates[1]);

            if (!gStates[4])
            {
                TextRenderer.DrawText(g, StringValue.ToUpper(), fonts[1],
                    new Point((this.Width / 2) - (valueSize.Width / 2), (this.Height / 5) * 3 - (valueSize.Height / 2)),
                    Enabled ? Theme.Text : Theme.TextDim);
            }

            TextRenderer.DrawText(g, PickerName.ToUpper(), fonts[2],
                new Point((this.Width / 2) - (nameSize.Width / 2), (this.Height / 5) - (nameSize.Height / 2)),
                Enabled ? Theme.TextMuted : Theme.TextDim);
        }

        private void DrawArrow(Graphics g, Rectangle rect, string glyph, Size glyphSize, float hover, bool pressed, bool armed)
        {
            var enabled = Enabled && armed;

            var fill = !enabled
                ? Theme.Surface
                : pressed
                    ? Theme.Mix(Theme.Raised, Theme.Accent, 0.35f)
                    : Theme.Mix(Theme.Surface, Theme.Raised, hover);

            var border = !enabled
                ? Theme.Line
                : Theme.Mix(Theme.Line, Theme.Accent2, hover);

            var glyphColor = !enabled
                ? Theme.TextDim
                : pressed
                    ? Theme.Accent
                    : Theme.Mix(Theme.TextMuted, Theme.Accent2, hover);

            using (var path = Theme.RoundedRect(rect, 3))
            {
                using (var brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                using var pen = new Pen(border);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, glyph, fonts[0],
                new Point(rect.X + ((rect.Width / 2) - (glyphSize.Width / 2)), rect.Y + ((rect.Height / 2) - (glyphSize.Height / 2))),
                glyphColor);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (leftRect.Contains(e.Location) && !gStates[0] && enabledStates[0])
            {
                gStates[0] = true;
                _leftHover.Target = 1f;
                Animator.Wake();
            }
            else if (!leftRect.Contains(e.Location) && gStates[0])
            {
                gStates[0] = false;
                _leftHover.Target = 0f;
                Animator.Wake();
            }

            if (rightRect.Contains(e.Location) && !gStates[1] && enabledStates[1])
            {
                gStates[1] = true;
                _rightHover.Target = 1f;
                Animator.Wake();
            }
            else if (!rightRect.Contains(e.Location) && gStates[1])
            {
                gStates[1] = false;
                _rightHover.Target = 0f;
                Animator.Wake();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            // Index 4 is the "value is being edited" flag, which a mouse leave must not clear.
            gStates = new bool[] { false, false, false, false, gStates[4] };

            _leftHover.Target = 0f;
            _rightHover.Target = 0f;
            Animator.Wake();

            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (leftRect.Contains(e.Location) && !gStates[2] && enabledStates[0])
            {
                gStates[2] = true;
                this.Invalidate(leftRect);
            }

            if (rightRect.Contains(e.Location) && !gStates[3] && enabledStates[1])
            {
                gStates[3] = true;
                this.Invalidate(rightRect);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (gStates[2])
            {
                if (enabledStates[0])
                {
                    Index -= 1;
                }
                gStates[2] = false;
                this.Invalidate(leftRect);
            }

            if (gStates[3])
            {
                if (enabledStates[1])
                {
                    Index += 1;
                }
                gStates[3] = false;
                this.Invalidate(rightRect);
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            this.Invalidate();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            if (gStates[4])
            {
                return;
            }

            var valueSize = TextRenderer.MeasureText(StringValue.ToUpper(), fonts[1]);
            var valueLoc = new Point((this.Width / 2) - (valueSize.Width / 2), (this.Height / 5) * 3 - (valueSize.Height / 2));

            if (!new Rectangle(valueLoc, valueSize).Contains(PointToClient(Cursor.Position)))
            {
                return;
            }

            Control entryBox;
            if (PickerType == PickerValueType.Integer)
            {
                entryBox = new TextBox { BorderStyle = BorderStyle.FixedSingle };
            }
            else
            {
                var newCombo = new ComboBox();
                newCombo.Items.AddRange(StringCollection);
                newCombo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                newCombo.AutoCompleteSource = AutoCompleteSource.ListItems;
                newCombo.FlatStyle = FlatStyle.Flat;
                newCombo.SelectedIndexChanged += entryBox_IndexChanged;
                entryBox = newCombo;
            }
            this.Controls.Add(entryBox);

            // Themed here rather than by Theme.Apply: the picker paints itself, so the
            // walker deliberately does not descend into it.
            entryBox.BackColor = Theme.Raised;
            entryBox.ForeColor = Theme.Text;

            entryBox.Font = fonts[1];
            entryBox.Location = valueLoc;
            entryBox.Text = StringValue;
            entryBox.Size = valueSize;
            entryBox.KeyDown += entryBox_KeyDown;
            entryBox.Select();

            enabledStates = new bool[] { false, false };
            gStates[4] = true;
            this.Invalidate();
        }

        private void entryBox_IndexChanged(object sender, EventArgs e)
        {
            var castedSender = (ComboBox)sender;
            if (castedSender.Text != StringValue)
            {
                entryBox_ApplyValue(sender);
            }
        }

        private void entryBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                entryBox_ApplyValue(sender);
            }
        }

        private void entryBox_ApplyValue(object sender)
        {
            Control castedSender;
            if (sender is ComboBox)
            {
                castedSender = (ComboBox)sender;
                var newInd = Array.FindIndex(StringCollection, x => x.ToLower() == castedSender.Text.ToLower());
                if (newInd > -1)
                {
                    Index = newInd;
                }
            }
            else
            {
                castedSender = (TextBox)sender;
                int newInd;
                if (int.TryParse(castedSender.Text, out newInd) == true)
                {
                    Index = newInd;
                }
            }
            this.Controls.Remove(castedSender);

            enabledStates = new bool[] { true, true };
            gStates[4] = false;
            this.Invalidate();
        }
    }

    public enum PickerValueType
    {
        Integer,
        String
    }
}
