using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CP2077SaveEditor.Utils;

namespace CP2077SaveEditor.Views.Controls
{
    /// <summary>
    /// The page switcher down the left edge.
    ///
    /// Rows used to jump 60px per click; they now ease to position, the open page keeps
    /// an accent bar so you can see where you are, and the scroll arrows hide themselves
    /// when every row already fits.
    /// </summary>
    public partial class ScrollMenuControl : UserControl
    {
        private const int RowHeight = 44;

        private readonly List<ModernButton> _buttons = new();
        private readonly Eased _scroll = new(0.24f);

        public ScrollMenuControl()
        {
            InitializeComponent();

            Animator.Register(this, Step);
        }

        public void AddButton(string text, EventHandler eventHandler)
        {
            var button = new ModernButton
            {
                Variant = ButtonVariant.Nav,
                ClickEffectEnabled = true,
                Location = new Point(0, _buttons.Count * RowHeight),
                Size = new Size(pnl_Menu.Width, RowHeight),
                Text = text,
                TextFont = new Font("Segoe UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
            };

            // Mark this row as the open one before handing off to the page switcher, so
            // the highlight and the content change together.
            button.Click += (s, e) => SetActive((ModernButton)s);
            button.Click += eventHandler;

            _buttons.Add(button);
            pnl_Menu.Controls.Add(button);

            if (_buttons.Count == 1)
            {
                button.Active = true;
            }

            UpdateChrome();
        }

        /// <summary>Highlights one row and clears the rest.</summary>
        private void SetActive(ModernButton target)
        {
            foreach (var button in _buttons)
            {
                button.Active = ReferenceEquals(button, target);
            }
        }

        /// <summary>Hides the scroll arrows when there is nothing to scroll.</summary>
        private void UpdateChrome()
        {
            var content = _buttons.Count * RowHeight;
            var needsScrolling = content > Height;

            btn_ScrollUp.Visible = needsScrolling;
            btn_ScrollDown.Visible = needsScrolling;

            pnl_Menu.Top = needsScrolling ? btn_ScrollUp.Height : 0;
            pnl_Menu.Height = needsScrolling
                ? Height - btn_ScrollUp.Height - btn_ScrollDown.Height
                : Height;

            foreach (var button in _buttons)
            {
                button.Width = pnl_Menu.Width;
            }

            Clamp();
        }

        private bool Step()
        {
            if (!_scroll.Step())
            {
                return false;
            }

            var top = (int)Math.Round(_scroll.Value);

            for (var i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].Top = top + (i * RowHeight);
            }

            return true;
        }

        private void ScrollBy(int offset)
        {
            _scroll.Target += offset;
            Clamp();
            Animator.Wake();
        }

        /// <summary>Keeps the list from easing past either end.</summary>
        private void Clamp()
        {
            var content = _buttons.Count * RowHeight;
            var min = Math.Min(0, pnl_Menu.Height - content);

            if (_scroll.Target > 0)
            {
                _scroll.Target = 0;
            }

            if (_scroll.Target < min)
            {
                _scroll.Target = min;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (_buttons.Count > 0)
            {
                UpdateChrome();
                Animator.Wake();
            }
        }

        private void btn_ScrollUp_Click(object sender, EventArgs e)
        {
            ScrollBy(RowHeight);
        }

        private void pnl_Menu_MouseWheel(object sender, MouseEventArgs eventArgs)
        {
            if (eventArgs.Delta > 0)
            {
                ScrollBy(RowHeight);
            }

            if (eventArgs.Delta < 0)
            {
                ScrollBy(-RowHeight);
            }
        }

        private void btn_ScrollDown_Click(object sender, EventArgs e)
        {
            ScrollBy(-RowHeight);
        }
    }
}
