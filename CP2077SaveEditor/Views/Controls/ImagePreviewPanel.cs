using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CP2077SaveEditor.Utils;

namespace CP2077SaveEditor.Views.Controls
{
    /// <summary>
    /// Shows the picture for whatever row is selected.
    ///
    /// The wiki renders are roughly 3:1, so they are shown in a pane rather than as
    /// row thumbnails, where they would be a few unreadable pixels tall. Loading is
    /// asynchronous and the panel simply says so; a vehicle or weapon with no picture
    /// says that too, rather than leaving an empty box.
    /// </summary>
    public sealed class ImagePreviewPanel : Control
    {
        private readonly Eased _fade = new(0.18f);

        private string _id;
        private string _caption;
        private string _note;
        private Image _image;
        private bool _loading;

        public ImagePreviewPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            SetStyle(ControlStyles.Selectable, false);

            TabStop = false;
            BackColor = Theme.Surface;

            Animator.Register(this, () => _fade.Step());
            ImageLibrary.ImageReady += OnImageReady;
        }

        /// <summary>Point the panel at a TweakDBID, or null to clear it.</summary>
        public void Show(string id)
        {
            if (string.Equals(_id, id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _id = id;
            _image = null;
            _loading = false;
            _note = null;
            _fade.Target = 0f;

            if (string.IsNullOrEmpty(id))
            {
                _caption = null;
                Invalidate();
                return;
            }

            if (!ImageLibrary.TryGetEntry(id, out var entry))
            {
                _caption = null;
                _note = "No picture for this one";
                Invalidate();
                Animator.Wake();
                return;
            }

            _caption = entry.Name;

            // A vehicle match below 1.0 is the right model but maybe the wrong variant;
            // say so instead of quietly showing a slightly wrong car.
            if (entry.Score < 1.0)
            {
                _note = "closest match";
            }

            _image = ImageLibrary.Peek(id);

            if (_image != null)
            {
                _fade.Target = 1f;
            }
            else
            {
                _loading = true;
                ImageLibrary.Request(id);
            }

            Invalidate();
            Animator.Wake();
        }

        private void OnImageReady(string id)
        {
            // Raised on a worker thread.
            if (!string.Equals(_id, id, StringComparison.OrdinalIgnoreCase) || IsDisposed)
            {
                return;
            }

            try
            {
                if (!IsHandleCreated)
                {
                    return;
                }

                BeginInvoke(new Action(() =>
                {
                    if (!string.Equals(_id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    _image = ImageLibrary.Peek(id);
                    _loading = false;
                    _fade.Target = _image != null ? 1f : 0f;

                    if (_image == null)
                    {
                        _note = "Could not load the picture";
                    }

                    Invalidate();
                    Animator.Wake();
                }));
            }
            catch (Exception)
            {
                // Window went away mid-load.
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Surface);

            var frame = new Rectangle(0, 0, Width - 1, Height - 1);

            using (var pen = new Pen(Theme.Line))
            {
                g.DrawRectangle(pen, frame);
            }

            var inner = Rectangle.Inflate(frame, -10, -10);

            if (inner.Width <= 8 || inner.Height <= 8)
            {
                return;
            }

            var textHeight = string.IsNullOrEmpty(_caption) ? 0 : 34;
            var picture = new Rectangle(inner.X, inner.Y, inner.Width, inner.Height - textHeight);

            if (_image != null && _fade.Value > 0.01f)
            {
                DrawFitted(g, _image, picture, _fade.Value);
            }
            else if (_loading)
            {
                DrawCentered(g, "Loading...", picture, Theme.TextMuted);
            }
            else if (!string.IsNullOrEmpty(_note) && string.IsNullOrEmpty(_caption))
            {
                DrawCentered(g, _note, picture, Theme.TextDim);
            }
            else if (string.IsNullOrEmpty(_id))
            {
                DrawCentered(g, "Select a row", picture, Theme.TextDim);
            }

            if (string.IsNullOrEmpty(_caption))
            {
                return;
            }

            var captionBox = new Rectangle(inner.X, inner.Bottom - textHeight, inner.Width, 18);
            TextRenderer.DrawText(g, _caption, Theme.UiFontBold, captionBox, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (string.IsNullOrEmpty(_note))
            {
                return;
            }

            var noteBox = new Rectangle(inner.X, inner.Bottom - textHeight + 17, inner.Width, 15);
            TextRenderer.DrawText(g, _note, Theme.UiFont, noteBox, Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static void DrawFitted(Graphics g, Image image, Rectangle box, float alpha)
        {
            var scale = Math.Min(box.Width / (double)image.Width, box.Height / (double)image.Height);

            if (scale <= 0)
            {
                return;
            }

            var w = Math.Max(1, (int)(image.Width * scale));
            var h = Math.Max(1, (int)(image.Height * scale));
            var target = new Rectangle(box.X + ((box.Width - w) / 2), box.Y + ((box.Height - h) / 2), w, h);

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (alpha >= 0.995f)
            {
                g.DrawImage(image, target);
                return;
            }

            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha };
            attributes.SetColorMatrix(matrix);

            g.DrawImage(image, target, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }

        private static void DrawCentered(Graphics g, string text, Rectangle box, Color color)
        {
            TextRenderer.DrawText(g, text, Theme.UiFont, box, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ImageLibrary.ImageReady -= OnImageReady;
            }

            base.Dispose(disposing);
        }
    }
}
