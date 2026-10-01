using System.Drawing;
using System.Windows.Forms;
using EzCap;

internal static class Program
{
[STAThread]
private static void Main(string[] args)
{
ApplicationConfiguration.Initialize();
var testRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "tests", ".history-smoke", Guid.NewGuid().ToString("N")));
var workspace = Path.GetFullPath(Environment.CurrentDirectory);
if (!testRoot.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Test path is outside the workspace.");
Directory.CreateDirectory(testRoot);
try
{
    // Synthetic images only. This optional fixture tests the real editor and
    // history methods without exercising or changing the production ACL policy.
    var isolatedHistory = args.Contains("--isolated-history");
    var history = isolatedHistory ? CreateIsolatedHistory(testRoot) : new CaptureHistory(testRoot);
    var first = new Bitmap(120, 80);
    var firstPath = history.Add(first);
    using var editor = new EditorForm(first, history, firstPath);
    editor.Show();
    Application.DoEvents();

    using var second = new Bitmap(80, 60);
    history.Add(second);
    Application.DoEvents();
    var strip = FindHistoryStrip(editor) ?? throw new Exception("Capture history strip was not created.");
    var tiles = strip.Controls.OfType<PictureBox>().OrderBy(picture => picture.Left).ToArray();
    if (tiles.Length != 2) throw new Exception($"Expected 2 history images, found {tiles.Length}.");
    if (tiles[0].Right != tiles[1].Left) throw new Exception("History images have a gap.");
    if (strip.Controls.OfType<Label>().Any()) throw new Exception("History labels are visible.");
    using var large = new Bitmap(800, 600);
    var largePath = history.Add(large);
    Application.DoEvents();
    var initialSize = editor.Size;
    var openHistory = typeof(EditorForm).GetMethod("OpenHistory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    openHistory.Invoke(editor, [largePath]);
    Application.DoEvents();
    if (editor.Width <= initialSize.Width && editor.Height <= initialSize.Height)
        throw new Exception("The editor did not grow for a larger history image.");
    openHistory.Invoke(editor, [firstPath]);
    Application.DoEvents();
    var viewport = (Panel)typeof(EditorForm).GetField("_viewport", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;
    var canvas = viewport.Controls[0];
    if (canvas.Left != (viewport.ClientSize.Width - canvas.Width) / 2 ||
        canvas.Top != (viewport.ClientSize.Height - canvas.Height) / 2)
        throw new Exception("The smaller history image was not centered.");
    var beforeClose = File.ReadAllBytes(firstPath);
    using var original = history.Load(firstPath);
    var untouchedPixel = original.GetPixel(90, 60).ToArgb();
    typeof(EditorForm).GetMethod("StartAnnotation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(editor, [new Rectangle(4, 4, 100, 65)]);
    using var borderClipboard = GetClipboardImage() ?? throw new Exception("Pending rectangle was not copied.");
    if (borderClipboard.GetPixel(4, 4).R < 200)
        throw new Exception("Clipboard is missing the pending rectangle.");
    var textEditor = FindTextBox(editor) ?? throw new Exception("Annotation editor was not created.");
    if (textEditor.Parent?.GetType().Name != "Canvas" || textEditor.Width > 2 || textEditor.Height > 2)
        throw new Exception("Text input was shown outside the rectangle.");
    if (editor.Controls.OfType<Panel>().Any(panel => panel.Dock == DockStyle.Top))
        throw new Exception("A top text input panel is still visible.");
    if (editor.Controls.OfType<ToolStrip>().SelectMany(bar => bar.Items.OfType<ToolStripItem>())
        .Any(item => (item.Text ?? string.Empty).Contains("\uC0AC\uAC01\uD615\uC744 \uADF8\uB9B0 \uB4A4")))
        throw new Exception("The removed toolbar instruction is still visible.");
    textEditor.Text = "note";
    if (!((System.Windows.Forms.Timer)typeof(EditorForm).GetField("_clipboardRefreshTimer",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!).Enabled)
        throw new Exception("Typing did not schedule a clipboard refresh.");
    using var textClipboard = GetClipboardImage(image =>
        Enumerable.Range(12, 25).SelectMany(y => Enumerable.Range(12, 55).Select(x => (x, y)))
            .Any(point => image.GetPixel(point.x, point.y).ToArgb() != borderClipboard.GetPixel(point.x, point.y).ToArgb()))
        ?? throw new Exception("Clipboard did not update while typing.");
    editor.Close();
    Application.DoEvents();
    if (beforeClose.SequenceEqual(File.ReadAllBytes(firstPath)))
        throw new Exception("Closing the editor did not save the pending annotation.");
    using (var transparentResult = history.Load(firstPath))
        if (transparentResult.GetPixel(90, 60).ToArgb() != untouchedPixel)
            throw new Exception("Default annotation background was not transparent.");

    using var filledEditor = new EditorForm(history.Load(firstPath), history, firstPath);
    typeof(EditorForm).GetField("_backgroundColor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .SetValue(filledEditor, Color.Yellow);
    filledEditor.Show();
    Application.DoEvents();
    typeof(EditorForm).GetMethod("StartAnnotation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(filledEditor, [new Rectangle(10, 10, 80, 50)]);
    filledEditor.Close();
    Application.DoEvents();
    using (var filledResult = history.Load(firstPath))
        if (filledResult.GetPixel(70, 50).ToArgb() != Color.Yellow.ToArgb())
            throw new Exception("Selected annotation background was not rendered.");
    using var clean = new Bitmap(240, 140);
    using (var graphics = Graphics.FromImage(clean)) graphics.Clear(Color.White);
    var textPath = history.Add(clean);
    using var textOnlyEditor = new EditorForm((Bitmap)clean.Clone(), history, textPath);
    textOnlyEditor.Show();
    Application.DoEvents();
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var checkbox = textOnlyEditor.Controls.OfType<ToolStrip>()
        .SelectMany(bar => bar.Items.OfType<ToolStripControlHost>())
        .Select(host => host.Control).OfType<CheckBox>().Single(control => control.Name == "TextOnlyCheckBox");
    if (checkbox.Text != "\uAE00\uC790\uB9CC") throw new Exception("Text-only label is not valid Korean.");
    if (checkbox.Checked) throw new Exception("Text-only must be off by default.");
    typeof(EditorForm).GetField("_backgroundColor", flags)!.SetValue(textOnlyEditor, Color.Yellow);
    var start = typeof(EditorForm).GetMethod("StartAnnotation", flags)!;
    start.Invoke(textOnlyEditor, [new Rectangle(10, 10, 200, 100)]);
    FindTextBox(textOnlyEditor)!.Text = "Text only";
    checkbox.Checked = true;
    Application.DoEvents();
    using var textOnlyClipboard = GetClipboardImage() ?? throw new Exception("Missing text-only clipboard.");
    AssertTextOnly(textOnlyClipboard);
    var textCanvas = ((Panel)typeof(EditorForm).GetField("_viewport", flags)!.GetValue(textOnlyEditor)!).Controls[0];
    using (var preview = new Bitmap(clean.Width, clean.Height))
    {
        textCanvas.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
        AssertTextOnly(preview);
    }
    checkbox.Checked = false;
    using (var restored = GetClipboardImage() ?? throw new Exception("Missing restored clipboard."))
        if (restored.GetPixel(10, 10).ToArgb() != Color.Red.ToArgb() || restored.GetPixel(180, 90).ToArgb() != Color.Yellow.ToArgb())
            throw new Exception("Disabling text-only did not restore border and fill.");
    checkbox.Checked = true;
    typeof(EditorForm).GetMethod("Copy", flags)!.Invoke(textOnlyEditor, null);
    if (FindTextBox(textOnlyEditor) is null) throw new Exception("Copy committed pending text.");
    typeof(EditorForm).GetMethod("CommitText", flags)!.Invoke(textOnlyEditor, null);
    using (var committed = history.Load(textPath)) AssertTextOnly(committed);
    checkbox.Checked = false;
    using (var unchanged = GetClipboardImage(image => image.GetPixel(10, 10).ToArgb() == Color.White.ToArgb())
        ?? throw new Exception("Missing committed clipboard."))
        AssertTextOnly(unchanged);
    // Save uses this same committed bitmap; verify the PNG encoding and reload.
    var pngPath = Path.Combine(testRoot, "text-only.png");
    ((Bitmap)typeof(EditorForm).GetField("_image", flags)!.GetValue(textOnlyEditor)!)
        .Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
    using (var png = new Bitmap(pngPath)) AssertTextOnly(png);
    typeof(EditorForm).GetMethod("Undo", flags)!.Invoke(textOnlyEditor, null);
    using (var undone = history.Load(textPath))
        if (HasText(undone)) throw new Exception("Undo did not remove committed text.");
    checkbox.Checked = true;
    start.Invoke(textOnlyEditor, [new Rectangle(10, 10, 200, 100)]);
    using (var empty = GetClipboardImage() ?? throw new Exception("Missing empty clipboard."))
        if (HasText(empty) || empty.GetPixel(10, 10).ToArgb() != Color.White.ToArgb())
            throw new Exception("Empty text-only annotation changed the image.");
    FindTextBox(textOnlyEditor)!.Text = "cancel";
    typeof(EditorForm).GetMethod("CancelText", flags)!.Invoke(textOnlyEditor, null);
    using (var cancelled = GetClipboardImage() ?? throw new Exception("Missing cancelled clipboard."))
        if (HasText(cancelled)) throw new Exception("Cancel did not restore the clipboard.");
    var toggleKey = new KeyEventArgs(Keys.Control | Keys.T);
    typeof(Form).GetMethod("OnKeyDown", flags)!.Invoke(textOnlyEditor, [toggleKey]);
    if (checkbox.Checked || !toggleKey.SuppressKeyPress)
        throw new Exception("Ctrl+T did not toggle text-only mode.");
    typeof(EditorForm).GetMethod("AddArrow", flags)!.Invoke(textOnlyEditor,
        [new Point(30, 90), new Point(190, 90)]);
    using (var arrowResult = history.Load(textPath))
        if (arrowResult.GetPixel(100, 90).ToArgb() == Color.White.ToArgb())
            throw new Exception("Arrow was not saved to history.");
    using (var arrowClipboard = GetClipboardImage() ?? throw new Exception("Missing arrow clipboard."))
        if (arrowClipboard.GetPixel(100, 90).ToArgb() == Color.White.ToArgb())
            throw new Exception("Arrow was not copied to clipboard.");
    typeof(EditorForm).GetMethod("Undo", flags)!.Invoke(textOnlyEditor, null);
    using (var undoneArrow = history.Load(textPath))
        if (undoneArrow.GetPixel(100, 90).ToArgb() != Color.White.ToArgb())
            throw new Exception("Undo did not remove the arrow.");
    textOnlyEditor.Close();
    using var largeImage = new Bitmap(2560, 1440);
    var largeTextPath = history.Add(largeImage);
    using var largeEditor = new EditorForm((Bitmap)largeImage.Clone(), history, largeTextPath);
    largeEditor.Show();
    Application.DoEvents();
    start.Invoke(largeEditor, [new Rectangle(20, 20, 400, 100)]);
    var largeTextBox = FindTextBox(largeEditor) ?? throw new Exception("Large image text editor is missing.");
    var typingTime = System.Diagnostics.Stopwatch.StartNew();
    for (var i = 0; i < 50; i++) largeTextBox.Text = $"Typing {i}";
    typingTime.Stop();
    if (typingTime.ElapsedMilliseconds > 2000)
        throw new Exception($"Typing on a large capture took {typingTime.ElapsedMilliseconds} ms.");
    largeEditor.Close();
    Console.WriteLine($"UI smoke passed: history, shortcuts, clipboard, arrow, undo, and 50 large-image text changes in {typingTime.ElapsedMilliseconds} ms.");
    if (isolatedHistory) Console.WriteLine("Isolated history fixture: production constructor and ACL policy were not tested.");
}
finally
{
    Directory.Delete(testRoot, true);
}
}

static CaptureHistory CreateIsolatedHistory(string directory)
{
    var history = (CaptureHistory)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(CaptureHistory));
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    typeof(CaptureHistory).GetField("_directory", flags)!.SetValue(history, directory);
    typeof(CaptureHistory).GetField("_files", flags)!.SetValue(history, new List<string>());
    return history;
}

static Bitmap? GetClipboardImage(Func<Bitmap, bool>? matches = null)
{
    for (var attempt = 0; attempt < 20; attempt++)
    {
        try
        {
            if (Clipboard.GetImage() is Bitmap image)
            {
                if (matches?.Invoke(image) != false) return image;
                image.Dispose();
            }
        }
        catch (System.Runtime.InteropServices.ExternalException) { }
        Application.DoEvents();
        System.Threading.Thread.Sleep(50);
    }
    return null;
}

static bool HasText(Bitmap image) =>
    Enumerable.Range(18, 40).SelectMany(y => Enumerable.Range(18, 140).Select(x => (x, y)))
        .Any(point => image.GetPixel(point.x, point.y).ToArgb() != Color.White.ToArgb());

static void AssertTextOnly(Bitmap image)
{
    if (image.GetPixel(10, 10).ToArgb() != Color.White.ToArgb())
        throw new Exception("Text-only output contains a border.");
    if (image.GetPixel(180, 90).ToArgb() != Color.White.ToArgb())
        throw new Exception("Text-only output contains a background fill.");
    if (!HasText(image)) throw new Exception("Text-only output is missing text.");
}

static Panel? FindHistoryStrip(Control root)
{
    foreach (Control child in root.Controls)
    {
        if (child is Panel { Name: "HistoryStrip" } strip) return strip;
        var nested = FindHistoryStrip(child);
        if (nested is not null) return nested;
    }
    return null;
}

static TextBox? FindTextBox(Control root)
{
    foreach (Control child in root.Controls)
    {
        if (child is TextBox editor) return editor;
        var nested = FindTextBox(child);
        if (nested is not null) return nested;
    }
    return null;
}
}
