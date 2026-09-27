using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

namespace EzCap;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        using var instance = new Mutex(true, $"Local\\EzCap-{user}", out var firstInstance);
        if (!firstInstance) return;
        try { Application.Run(new CaptureApp()); }
        catch (Exception ex)
        {
            MessageBox.Show($"EzCap을 시작하지 못했습니다: {ex.Message}", "EzCap", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class CaptureApp : ApplicationContext
{
    private const int HotkeyId = 1;
    private const int HotkeyMessage = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private readonly HotkeyWindow _window;
    private readonly NotifyIcon _tray;
    private readonly CaptureHistory _history;
    private CaptureOverlay? _overlay;
    private readonly List<EditorForm> _editors = [];

    public CaptureApp()
    {
        _history = new CaptureHistory();
        _window = new HotkeyWindow(StartCapture);
        var menu = new ContextMenuStrip();
        menu.Items.Add("직사각형 캡처 (Ctrl+Shift+C)", null, (_, _) => StartCapture());
        menu.Items.Add("종료", null, (_, _) => ExitThread());
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "EzCap — Ctrl+Shift+C로 캡처",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => StartCapture();
        if (!RegisterHotKey(_window.Handle, HotkeyId, ModControl | ModShift, (uint)Keys.C))
        {
            _tray.ShowBalloonTip(5000, "EzCap", "Ctrl+Shift+C 단축키가 이미 사용 중입니다. 트레이 메뉴에서 캡처할 수 있습니다.", ToolTipIcon.Warning);
        }
    }

    private void StartCapture()
    {
        if (_overlay is not null) return;
        var bounds = SystemInformation.VirtualScreen;
        var screenshot = new Bitmap(bounds.Width, bounds.Height);
        try
        {
            using (var graphics = Graphics.FromImage(screenshot))
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            _overlay = new CaptureOverlay(screenshot, bounds, OpenEditor);
            _overlay.FormClosed += (_, _) => _overlay = null;
            _overlay.Show();
            _overlay.Activate();
        }
        catch (Exception ex)
        {
            screenshot.Dispose();
            _tray.ShowBalloonTip(5000, "EzCap", $"캡처를 시작하지 못했습니다: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private void OpenEditor(Bitmap image)
    {
        string path;
        try { path = _history.Add(image); }
        catch (Exception ex)
        {
            image.Dispose();
            MessageBox.Show($"캡처 이력을 저장하지 못했습니다: {ex.Message}", "EzCap", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        var editor = new EditorForm(image, _history, path);
        _editors.Add(editor);
        editor.FormClosed += (_, _) => _editors.Remove(editor);
        editor.Show();
        editor.Activate();
    }

    protected override void ExitThreadCore()
    {
        _overlay?.Close();
        foreach (var editor in _editors.ToArray()) editor.Close();
        UnregisterHotKey(_window.Handle, HotkeyId);
        _window.Dispose();
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        base.ExitThreadCore();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr handle, int id);

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        public HotkeyWindow(Action onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        private readonly Action _onHotkey;

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == HotkeyMessage && message.WParam == (IntPtr)HotkeyId)
                _onHotkey();
            base.WndProc(ref message);
        }

        public void Dispose() => DestroyHandle();
    }
}

internal sealed class CaptureOverlay : Form
{
    private readonly Bitmap _screenshot;
    private readonly Bitmap _dimmed;
    private readonly Action<Bitmap> _onCaptured;
    private Point? _start;
    private Point _end;

    public CaptureOverlay(Bitmap screenshot, Rectangle virtualBounds, Action<Bitmap> onCaptured)
    {
        _screenshot = screenshot;
        _onCaptured = onCaptured;
        _dimmed = (Bitmap)screenshot.Clone();
        using (var graphics = Graphics.FromImage(_dimmed))
        using (var shade = new SolidBrush(Color.FromArgb(90, Color.Black)))
            graphics.FillRectangle(shade, new Rectangle(Point.Empty, _dimmed.Size));
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = virtualBounds;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // The captured image is painted over the desktop; selection stays visible across monitors.
        Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.DrawImageUnscaled(_dimmed, 0, 0);
        if (_start is not { } start) return;
        var selection = RectangleFromPoints(start, _end);
        if (selection.Width == 0 || selection.Height == 0) return;
        e.Graphics.DrawImage(_screenshot, selection, selection, GraphicsUnit.Pixel);
        using var border = new Pen(Color.DeepSkyBlue, 2);
        e.Graphics.DrawRectangle(border, selection);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { Close(); return; }
        if (e.Button != MouseButtons.Left) return;
        _start = e.Location;
        _end = e.Location;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_start is null) return;
        var previous = RectangleFromPoints(_start.Value, _end);
        _end = Clamp(e.Location);
        var current = RectangleFromPoints(_start.Value, _end);
        var dirty = Rectangle.Union(previous, current);
        dirty.Inflate(4, 4);
        Invalidate(dirty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _start is not { } start) return;
        Capture = false;
        _end = Clamp(e.Location);
        var selection = RectangleFromPoints(start, _end);
        if (selection.Width > 0 && selection.Height > 0)
        {
            var cropped = _screenshot.Clone(selection, _screenshot.PixelFormat);
            try { Clipboard.SetImage(cropped); }
            catch (Exception ex)
            {
                MessageBox.Show($"클립보드에 복사하지 못했습니다: {ex.Message}", "EzCap", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Close();
            _onCaptured(cropped);
            return;
        }
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }

    private Point Clamp(Point point) => new(
        Math.Clamp(point.X, 0, ClientSize.Width),
        Math.Clamp(point.Y, 0, ClientSize.Height));

    private static Rectangle RectangleFromPoints(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _screenshot.Dispose();
            _dimmed.Dispose();
        }
        base.Dispose(disposing);
    }
}
