using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.CompilerServices;
using EzCap;

// Exercise only the production renderer, without constructing a Form or history
// directory. This does not test desktop interaction, clipboard, or history ACLs.
var flags = BindingFlags.Instance | BindingFlags.NonPublic;
var owner = (EditorForm)RuntimeHelpers.GetUninitializedObject(typeof(EditorForm));
void Set(string name, object value) => typeof(EditorForm).GetField(name, flags)!.SetValue(owner, value);
Set("_color", Color.Red);
Set("_strokeWidth", 3);
Set("_backgroundColor", Color.Yellow);
using var font = new Font("Malgun Gothic", 14);
Bitmap Render(bool textOnly, string text)
{
    Set("_textOnly", textOnly);
    var bitmap = new Bitmap(240, 140);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.Clear(Color.White);
    typeof(EditorForm).GetMethod("RenderAnnotation", flags)!.Invoke(owner,
        [graphics, new Rectangle(10, 10, 200, 100), text, font]);
    return bitmap;
}
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
using var normal = Render(false, "Text only");
Check(normal.GetPixel(10, 10).ToArgb() == Color.Red.ToArgb(), "Default border missing.");
Check(normal.GetPixel(180, 90).ToArgb() == Color.Yellow.ToArgb(), "Default fill missing.");
using var textOnly = Render(true, "Text only");
Check(textOnly.GetPixel(10, 10).ToArgb() == Color.White.ToArgb(), "Text-only border present.");
Check(textOnly.GetPixel(180, 90).ToArgb() == Color.White.ToArgb(), "Text-only fill present.");
Check(Enumerable.Range(18, 40).SelectMany(y => Enumerable.Range(18, 140).Select(x => (x, y)))
    .Any(p => textOnly.GetPixel(p.x, p.y).ToArgb() != Color.White.ToArgb()), "Text missing.");
using var empty = Render(true, "");
Check(Enumerable.Range(0, empty.Height).SelectMany(y => Enumerable.Range(0, empty.Width).Select(x => (x, y)))
    .All(p => empty.GetPixel(p.x, p.y).ToArgb() == Color.White.ToArgb()), "Empty annotation changed pixels.");
using var encoded = new MemoryStream();
textOnly.Save(encoded, ImageFormat.Png);
encoded.Position = 0;
using var decoded = new Bitmap(encoded);
Check(Enumerable.Range(0, decoded.Height).SelectMany(y => Enumerable.Range(0, decoded.Width).Select(x => (x, y)))
    .All(p => decoded.GetPixel(p.x, p.y).ToArgb() == textOnly.GetPixel(p.x, p.y).ToArgb()), "PNG round-trip differs.");
Console.WriteLine("Rendering smoke passed: default border/fill, text-only border/fill suppression, text, empty annotation, PNG round-trip.");
GC.SuppressFinalize(owner);
