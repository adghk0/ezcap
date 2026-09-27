using System.Drawing;
using System.Windows.Forms;
using EzCap;

internal static class Program
{
[STAThread]
private static void Main()
{
ApplicationConfiguration.Initialize();
var testRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "tests", ".history-smoke", Guid.NewGuid().ToString("N")));
var workspace = Path.GetFullPath(Environment.CurrentDirectory);
if (!testRoot.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Test path is outside the workspace.");
Directory.CreateDirectory(testRoot);
try
{
    var history = new CaptureHistory(testRoot);
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
    using var borderClipboard = Clipboard.GetImage() as Bitmap ?? throw new Exception("Pending rectangle was not copied.");
    if (borderClipboard.GetPixel(4, 4).R < 200)
        throw new Exception("Clipboard is missing the pending rectangle.");
    var textEditor = FindTextBox(editor) ?? throw new Exception("Annotation editor was not created.");
    if (textEditor.Parent?.GetType().Name != "Canvas" || textEditor.Width > 2 || textEditor.Height > 2)
        throw new Exception("Text input was shown outside the rectangle.");
    if (editor.Controls.OfType<Panel>().Any(panel => panel.Dock == DockStyle.Top))
        throw new Exception("A top text input panel is still visible.");
    if (editor.Controls.OfType<ToolStrip>().SelectMany(bar => bar.Items.OfType<ToolStripItem>())
        .Any(item => (item.Text ?? string.Empty).Contains("사각형을 그린 뒤")))
        throw new Exception("The removed toolbar instruction is still visible.");
    textEditor.Text = "note";
    using var textClipboard = Clipboard.GetImage() as Bitmap ?? throw new Exception("Pending text was not copied.");
    if (Enumerable.Range(12, 25).SelectMany(y => Enumerable.Range(12, 55).Select(x => (x, y)))
        .All(point => textClipboard.GetPixel(point.x, point.y).ToArgb() == borderClipboard.GetPixel(point.x, point.y).ToArgb()))
        throw new Exception("Clipboard did not update while typing.");
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
    Console.WriteLine("UI smoke passed: history layout, annotation colors, and pending clipboard previews.");
}
finally
{
    Directory.Delete(testRoot, true);
}
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
