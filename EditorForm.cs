using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace EzCap;

internal sealed class EditorForm : Form
{
    private Bitmap _image;
    private readonly CaptureHistory _history;
    private string _historyPath;
    private readonly Stack<Bitmap> _undo = new();
    private readonly Canvas _canvas;
    private readonly Panel _viewport;
    private readonly Panel _historyStrip;
    private Color _color = Color.Red;
    private Color? _backgroundColor;
    private int _strokeWidth = 3;
    private bool _textOnly;
    private TextBox? _textEditor;
    private Rectangle? _pendingRectangle;

    public EditorForm(Bitmap image, CaptureHistory history, string historyPath)
    {
        _image = image;
        _history = history;
        _historyPath = historyPath;
        Text = "EzCap - 캡처 편집";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(Math.Min(image.Width + 50, 1200), Math.Min(image.Height + 110, 850));
        MinimumSize = new Size(480, 300);

        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        var colorButton = new ToolStripButton("글자/테두리 색") { BackColor = _color };
        colorButton.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = _color, FullOpen = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _color = dialog.Color;
                colorButton.BackColor = _color;
                _canvas?.Invalidate();
                UpdateClipboard();
            }
        };
        var backgroundButton = new ToolStripDropDownButton("배경: 투명");
        backgroundButton.DropDownItems.Add("투명", null, (_, _) =>
        {
            _backgroundColor = null;
            backgroundButton.Text = "배경: 투명";
            backgroundButton.BackColor = SystemColors.Control;
            _canvas?.Invalidate();
            UpdateClipboard();
        });
        backgroundButton.DropDownItems.Add("색상 선택...", null, (_, _) =>
        {
            using var dialog = new ColorDialog { Color = _backgroundColor ?? Color.White, FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _backgroundColor = dialog.Color;
            backgroundButton.Text = "배경색";
            backgroundButton.BackColor = dialog.Color;
            _canvas?.Invalidate();
            UpdateClipboard();
        });
        var widthLabel = new ToolStripLabel("선 굵기");
        var widthBox = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 48 };
        widthBox.Items.AddRange(["1", "2", "3", "5", "8"]);
        widthBox.SelectedItem = "3";
        widthBox.SelectedIndexChanged += (_, _) =>
        {
            _strokeWidth = int.Parse((string)widthBox.SelectedItem!);
            _canvas?.Invalidate();
            UpdateClipboard();
        };
        var undoButton = new ToolStripButton("실행 취소");
        var textOnlyCheckBox = new CheckBox
        {
            Name = "TextOnlyCheckBox",
            Text = "글자만",
            AutoSize = true,
            BackColor = Color.Transparent
        };
        textOnlyCheckBox.CheckedChanged += (_, _) =>
        {
            _textOnly = textOnlyCheckBox.Checked;
            if (_textEditor is { } editor)
            {
                var background = !_textOnly && _backgroundColor is { } selected
                    ? selected : _image.GetPixel(editor.Left, editor.Top);
                editor.BackColor = background.A == 255 ? background : Color.Black;
            }
            _canvas?.Invalidate();
            UpdateClipboard();
            _textEditor?.Focus();
        };
        var textOnlyHost = new ToolStripControlHost(textOnlyCheckBox);
        undoButton.Click += (_, _) => Undo();
        var copyButton = new ToolStripButton("클립보드 복사");
        copyButton.Click += (_, _) => Copy();
        var saveButton = new ToolStripButton("PNG 저장");
        saveButton.Click += (_, _) => Save();
        toolbar.Items.AddRange([textOnlyHost, colorButton, backgroundButton,
            widthLabel, widthBox, new ToolStripSeparator(), undoButton, copyButton, saveButton]);

        _viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(38, 38, 38) };
        _canvas = new Canvas(this) { Size = image.Size, Location = Point.Empty };
        _viewport.Controls.Add(_canvas);
        _historyStrip = new Panel
        {
            Name = "HistoryStrip",
            Dock = DockStyle.Bottom,
            Height = 90,
            AutoScroll = true,
            BackColor = Color.FromArgb(38, 38, 38)
        };
        Controls.Add(_viewport);
        Controls.Add(_historyStrip);
        Controls.Add(toolbar);
        _viewport.Resize += (_, _) => CenterCanvas();
        Load += (_, _) => FitImage();
        _history.Changed += RefreshHistory;
        RefreshHistory();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z) { Undo(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.S) { Save(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.C) { Copy(); e.SuppressKeyPress = true; }
        };
    }

    private void AddUndo()
    {
        _undo.Push((Bitmap)_image.Clone());
        if (_undo.Count <= 20) return;
        var items = _undo.Reverse().ToArray();
        items[0].Dispose();
        _undo.Clear();
        foreach (var item in items.Skip(1)) _undo.Push(item);
    }

    private void Undo()
    {
        CommitText();
        if (_undo.Count == 0) return;
        _image.Dispose();
        _image = _undo.Pop();
        _canvas.Invalidate();
        UpdateHistory();
        UpdateClipboard();
    }

    private void StartAnnotation(Rectangle rectangle)
    {
        if (rectangle.Width < 2 || rectangle.Height < 2) return;
        CommitText();
        _pendingRectangle = rectangle;
        var inset = _strokeWidth + 4;
        var inputPoint = new Point(
            Math.Clamp(rectangle.Left + inset, 0, _image.Width - 1),
            Math.Clamp(rectangle.Top + inset, 0, _image.Height - 1));
        var inputBackground = !_textOnly && _backgroundColor is { } selected
            ? selected : _image.GetPixel(inputPoint.X, inputPoint.Y);
        if (inputBackground.A < 255) inputBackground = Color.Black;
        _textEditor = new TextBox
        {
            Multiline = true,
            BorderStyle = BorderStyle.None,
            ForeColor = _color,
            Font = new Font("Malgun Gothic", 14),
            BackColor = inputBackground,
            Location = inputPoint,
            Size = new Size(1, 1)
        };
        _textEditor.TextChanged += (_, _) =>
        {
            _canvas.Invalidate(rectangle);
            UpdateClipboard();
        };
        _textEditor.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter) { CommitText(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { CancelText(); e.SuppressKeyPress = true; }
        };
        _canvas.Controls.Add(_textEditor);
        _textEditor.Focus();
        _canvas.Invalidate(rectangle);
        UpdateClipboard();
    }

    private void CommitText()
    {
        if (_textEditor is not { } editor || _pendingRectangle is not { } rectangle) return;
        _textEditor = null;
        _pendingRectangle = null;
        var value = editor.Text;
        var font = editor.Font;
        AddUndo();
        using (var graphics = Graphics.FromImage(_image))
            RenderAnnotation(graphics, rectangle, value, font);
        _canvas.Controls.Remove(editor);
        editor.Dispose();
        _canvas.Invalidate();
        UpdateHistory();
        UpdateClipboard();
    }

    private void CancelText()
    {
        if (_textEditor is not { } editor) return;
        _textEditor = null;
        _pendingRectangle = null;
        _canvas.Controls.Remove(editor);
        editor.Dispose();
        _canvas.Invalidate();
        UpdateClipboard();
    }

    private void RenderAnnotation(Graphics graphics, Rectangle rectangle, string value, Font font)
    {
        if (!_textOnly)
        {
            if (_backgroundColor is { } background)
            {
                using var fill = new SolidBrush(background);
                graphics.FillRectangle(fill, rectangle);
            }
            using var pen = new Pen(_color, _strokeWidth);
            graphics.DrawRectangle(pen, rectangle);
        }
        if (string.IsNullOrWhiteSpace(value)) return;
        var inset = _strokeWidth + 4;
        var textArea = new Rectangle(rectangle.Left + inset, rectangle.Top + inset,
            Math.Max(1, rectangle.Width - inset * 2), Math.Max(1, rectangle.Height - inset * 2));
        using var brush = new SolidBrush(_color);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        graphics.DrawString(value, font, brush, textArea, format);
    }

    private void Copy()
    {
        UpdateClipboard(showError: true);
    }

    private void UpdateClipboard(bool showError = false)
    {
        try
        {
            using var image = (Bitmap)_image.Clone();
            if (_pendingRectangle is { } rectangle && _textEditor is { } editor)
            {
                using var graphics = Graphics.FromImage(image);
                RenderAnnotation(graphics, rectangle, editor.Text, editor.Font);
            }
            Clipboard.SetDataObject(image, true, 3, 100);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            if (showError)
                MessageBox.Show(this, ex.Message, "클립보드 복사 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateHistory()
    {
        try { _history.Update(_historyPath, _image); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "캡처 이력 갱신 실패", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RefreshHistory()
    {
        _historyStrip.SuspendLayout();
        foreach (var picture in _historyStrip.Controls.OfType<PictureBox>().ToArray())
        {
            picture.Image?.Dispose();
            picture.Dispose();
        }
        _historyStrip.Controls.Clear();
        var index = 0;
        foreach (var path in _history.Files)
        {
            try
            {
                using var image = _history.Load(path);
                var thumbnail = new Bitmap(108, 72);
                using (var graphics = Graphics.FromImage(thumbnail))
                {
                    var scale = Math.Max(108.0 / image.Width, 72.0 / image.Height);
                    var size = new Size((int)Math.Ceiling(image.Width * scale), (int)Math.Ceiling(image.Height * scale));
                    graphics.DrawImage(image, new Rectangle((108 - size.Width) / 2, (72 - size.Height) / 2, size.Width, size.Height));
                }
                var picture = new PictureBox
                {
                    Image = thumbnail,
                    Tag = path,
                    Location = new Point(index * 108, 0),
                    Size = new Size(108, 72),
                    Margin = Padding.Empty,
                    SizeMode = PictureBoxSizeMode.Normal
                };
                picture.Click += (_, _) => OpenHistory(path);
                picture.Paint += (_, e) =>
                {
                    if (path == _historyPath)
                        ControlPaint.DrawBorder(e.Graphics, picture.ClientRectangle, Color.DeepSkyBlue, ButtonBorderStyle.Solid);
                };
                _historyStrip.Controls.Add(picture);
                index++;
            }
            catch (IOException) { /* A file being replaced will appear on the next update. */ }
        }
        _historyStrip.AutoScrollMinSize = new Size(index * 108, 72);
        _historyStrip.ResumeLayout();
    }

    private void OpenHistory(string path)
    {
        if (path == _historyPath) return;
        CommitText();
        try
        {
            var image = _history.Load(path);
            _image.Dispose();
            _image = image;
            _historyPath = path;
            foreach (var undo in _undo) undo.Dispose();
            _undo.Clear();
            _canvas.Size = image.Size;
            FitImage();
            _canvas.Invalidate();
            foreach (Control picture in _historyStrip.Controls) picture.Invalidate();
            UpdateClipboard();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "캡처 이력 열기 실패", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void FitImage()
    {
        _viewport.AutoScrollPosition = Point.Empty;
        _canvas.Location = Point.Empty;
        _viewport.AutoScrollMinSize = _image.Size;

        var work = Screen.FromControl(this).WorkingArea;
        var nonViewportHeight = ClientSize.Height - _viewport.Height;
        var desiredOuter = SizeFromClientSize(new Size(
            _image.Width + 32,
            _image.Height + nonViewportHeight + 32));
        var target = new Size(
            Math.Min(work.Width, Math.Max(Width, desiredOuter.Width)),
            Math.Min(work.Height, Math.Max(Height, desiredOuter.Height)));
        if (target != Size)
        {
            Bounds = new Rectangle(
                work.Left + (work.Width - target.Width) / 2,
                work.Top + (work.Height - target.Height) / 2,
                target.Width, target.Height);
        }
        CenterCanvas();
    }

    private void CenterCanvas()
    {
        if (_viewport.IsDisposed || _canvas.IsDisposed) return;
        _canvas.Location = new Point(
            Math.Max(0, (_viewport.ClientSize.Width - _canvas.Width) / 2),
            Math.Max(0, (_viewport.ClientSize.Height - _canvas.Height) / 2));
    }

    private void Save()
    {
        CommitText();
        using var dialog = new SaveFileDialog { Filter = "PNG 이미지|*.png", DefaultExt = "png", FileName = $"EzCap_{DateTime.Now:yyyyMMdd_HHmmss}.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { _image.Save(dialog.FileName, ImageFormat.Png); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        CommitText();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _history.Changed -= RefreshHistory;
            foreach (var picture in _historyStrip.Controls.OfType<PictureBox>().ToArray()) picture.Image?.Dispose();
            _image.Dispose();
            foreach (var image in _undo) image.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class Canvas : Control
    {
        private readonly EditorForm owner;
        private Point? _start;
        private Point _end;

        public Canvas(EditorForm owner)
        {
            this.owner = owner;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(owner._image, Point.Empty);
            if (owner._pendingRectangle is { } pending)
                owner.RenderAnnotation(e.Graphics, pending, owner._textEditor?.Text ?? string.Empty,
                    owner._textEditor?.Font ?? owner.Font);
            if (_start is { } start)
            {
                using var pen = new Pen(owner._color, owner._strokeWidth);
                // The dashed selection guide is editor-only, never part of the output.
                if (owner._textOnly) pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                e.Graphics.DrawRectangle(pen, BoundsOf(start, _end));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            owner.CommitText();
            _start = e.Location;
            _end = e.Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_start is null) return;
            var old = BoundsOf(_start.Value, _end);
            _end = e.Location;
            var dirty = Rectangle.Union(old, BoundsOf(_start.Value, _end));
            dirty.Inflate(owner._strokeWidth + 2, owner._strokeWidth + 2);
            Invalidate(dirty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_start is not { } start) return;
            Capture = false;
            var rectangle = BoundsOf(start, e.Location);
            _start = null;
            owner.StartAnnotation(rectangle);
            Invalidate();
        }

        private static Rectangle BoundsOf(Point a, Point b) =>
            Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }
}
