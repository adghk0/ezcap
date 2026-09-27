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
    var first = new Bitmap(64, 48);
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
    editor.Close();
    Application.DoEvents();
    Console.WriteLine("UI smoke passed: editor opened and two adjacent image-only history tiles rendered.");
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
