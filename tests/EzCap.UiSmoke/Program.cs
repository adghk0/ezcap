using System.Drawing;
using System.Windows.Forms;
using EzCap;

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
    var beforeClose = File.ReadAllBytes(firstPath);
    using var original = history.Load(firstPath);
    var untouchedPixel = original.GetPixel(90, 60).ToArgb();
    typeof(EditorForm).GetMethod("StartAnnotation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(editor, [new Rectangle(4, 4, 100, 65)]);
    var textEditor = FindTextBox(editor) ?? throw new Exception("Annotation editor was not created.");
    if (textEditor.Parent?.Parent != editor) throw new Exception("Text input covers the capture canvas.");
    if (textEditor.Parent.Controls.OfType<Label>().Any())
        throw new Exception("Text input has an overlapping label.");
    textEditor.Text = "note";
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
    Console.WriteLine("UI smoke passed: transparent default and selected background color rendered.");
}
finally
{
    Directory.Delete(testRoot, true);
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
