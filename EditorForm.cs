using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace EzCap;

internal sealed class EditorForm : Form
{
    private Bitmap _image;
    private readonly Stack<Bitmap> _undo = new();
    private readonly Canvas _canvas;
    private Color _color = Color.Red;
    private int _strokeWidth = 3;
    private TextBox? _textEditor;
    private Rectangle? _pendingRectangle;

    public EditorForm(Bitmap image)
    {
        _image = image;
        Text = "EzCap - 캡처 편집";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(Math.Min(image.Width + 50, 1200), Math.Min(image.Height + 110, 850));
        MinimumSize = new Size(480, 300);

        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        var modeLabel = new ToolStripLabel("사각형을 그린 뒤 안에 글자를 입력하세요 (빈 글자도 가능)");
        var colorButton = new ToolStripButton("색상") { BackColor = _color };
        colorButton.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = _color, FullOpen = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _color = dialog.Color;
                colorButton.BackColor = _color;
            }
        };
        var widthLabel = new ToolStripLabel("선 굵기");
        var widthBox = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 48 };
        widthBox.Items.AddRange(["1", "2", "3", "5", "8"]);
        widthBox.SelectedItem = "3";
        widthBox.SelectedIndexChanged += (_, _) => _strokeWidth = int.Parse((string)widthBox.SelectedItem!);
        var undoButton = new ToolStripButton("실행 취소");
        undoButton.Click += (_, _) => Undo();
        var copyButton = new ToolStripButton("클립보드 복사");
        copyButton.Click += (_, _) => Copy();
        var saveButton = new ToolStripButton("PNG 저장");
        saveButton.Click += (_, _) => Save();
        toolbar.Items.AddRange([modeLabel, new ToolStripSeparator(), colorButton,
            widthLabel, widthBox, new ToolStripSeparator(), undoButton, copyButton, saveButton]);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(38, 38, 38) };
        _canvas = new Canvas(this) { Size = image.Size, Location = Point.Empty };
        scroll.Controls.Add(_canvas);
        Controls.Add(scroll);
        Controls.Add(toolbar);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z) { Undo(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.S) { Save(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.C && _textEditor is null) { Copy(); e.SuppressKeyPress = true; }
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
    }

    private void StartAnnotation(Rectangle rectangle)
    {
        if (rectangle.Width < 2 || rectangle.Height < 2) return;
        CommitText();
        _pendingRectangle = rectangle;
        var inset = _strokeWidth + 4;
        _textEditor = new TextBox
        {
            Multiline = true,
            BorderStyle = BorderStyle.None,
            ForeColor = _color,
            BackColor = Color.White,
            Font = new Font("Malgun Gothic", 14),
            Location = new Point(rectangle.Left + inset, rectangle.Top + inset),
            Size = new Size(Math.Max(1, rectangle.Width - inset * 2), Math.Max(1, rectangle.Height - inset * 2))
        };
        _textEditor.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter) { CommitText(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { CancelText(); e.SuppressKeyPress = true; }
        };
        _textEditor.LostFocus += (_, _) => CommitText();
        _canvas.Controls.Add(_textEditor);
        _textEditor.Focus();
        _canvas.Invalidate(rectangle);
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
        using (var pen = new Pen(_color, _strokeWidth))
        using (var brush = new SolidBrush(_color))
        {
            graphics.DrawRectangle(pen, rectangle);
            if (!string.IsNullOrWhiteSpace(value))
            {
                var inset = _strokeWidth + 4;
                var textArea = Rectangle.Inflate(rectangle, -inset, -inset);
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
                graphics.DrawString(value, font, brush, textArea, format);
            }
        }
        _canvas.Controls.Remove(editor);
        editor.Dispose();
        _canvas.Invalidate();
    }

    private void CancelText()
    {
        if (_textEditor is not { } editor) return;
        _textEditor = null;
        _pendingRectangle = null;
        _canvas.Controls.Remove(editor);
        editor.Dispose();
    }

    private void Copy()
    {
        CommitText();
        try { Clipboard.SetImage(_image); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "클립보드 복사 실패", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void Save()
    {
        CommitText();
        using var dialog = new SaveFileDialog { Filter = "PNG 이미지|*.png", DefaultExt = "png", FileName = $"EzCap_{DateTime.Now:yyyyMMdd_HHmmss}.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { _image.Save(dialog.FileName, ImageFormat.Png); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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
            using var pen = new Pen(owner._color, owner._strokeWidth);
            if (owner._pendingRectangle is { } pending)
                e.Graphics.DrawRectangle(pen, pending);
            if (_start is { } start)
                e.Graphics.DrawRectangle(pen, BoundsOf(start, _end));
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
