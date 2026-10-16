using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CP2077SaveEditor.Utils;

namespace CP2077SaveEditor.Views.Controls
{
    /// <summary>
    /// A 3px indeterminate activity strip.
    ///
    /// Reading or writing a save runs on a worker thread, which leaves the window
    /// looking frozen even though it is fine. This gives that work a visible pulse,
    /// and fades itself out when idle so there is nothing to look at the rest of
    /// the time. No input, no layout cost -- purely feedback.
    /// </summary>
    public sealed class ActivityBar : Control
    {
        private readonly Eased _fade = new(0.14f);
        private float _phase;
        private bool _busy;

        public ActivityBar()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            // Never take focus or clicks; this is a readout, not a control.
            SetStyle(ControlStyles.Selectable, false);

            Height = 3;
            TabStop = false;
            BackColor = Theme.Base;

            Animator.Register(this, Step);
        }

        /// <summary>Starts or stops the pulse. Safe to set from any thread via the owning form.</summary>
        public bool Busy
        {
            get => _busy;
            set
            {
                if (_busy == value)
                {
                    return;
                }

                _busy = value;
                _fade.Target = value ? 1f : 0f;
                Animator.Wake();
            }
        }

        private bool Step()
        {
            var moved = _fade.Step();

            if (!_busy && _fade.Value <= 0.001f)
            {
                return moved;
            }

            // Two full sweeps per second.
            _phase += 0.009f;

            if (_phase > 1f)
            {
                _phase -= 1f;
            }

            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Base);

            var alpha = _fade.Value;

            if (alpha <= 0.001f || Width <= 0)
            {
                return;
            }

            // Track.
            using (var track = new SolidBrush(Theme.Mix(Theme.Base, Theme.Line, alpha)))
            {
                g.FillRectangle(track, 0, 0, Width, Height);
            }

            // A chunk that slides left to right and wraps, eased so it slows at the edges.
            var span = Math.Max(60, Width / 4);
            var eased = (float)(0.5 - 0.5 * Math.Cos(_phase * Math.PI * 2));
            var x = (int)(eased * (Width + span)) - span;

            var band = new Rectangle(x, 0, span, Height);

            if (band.Width <= 0)
            {
                return;
            }

            using var brush = new LinearGradientBrush(
                band,
                Color.FromArgb((int)(alpha * 255), Theme.Accent),
                Color.FromArgb((int)(alpha * 255), Theme.Accent2),
                LinearGradientMode.Horizontal);

            g.FillRectangle(brush, band);
        }
    }
}
