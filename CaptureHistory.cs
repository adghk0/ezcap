using System.Drawing;
using System.Drawing.Imaging;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EzCap;

internal sealed class CaptureHistory
{
    private const string Prefix = "capture-";
    private readonly List<string> _files = [];
    private readonly string _directory;

    public event Action? Changed;
    public IReadOnlyList<string> Files => _files;

    public CaptureHistory()
    {
        _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EzCap");
        var directory = Directory.CreateDirectory(_directory);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("EzCap 이력 폴더가 다른 위치로 연결되어 있습니다.");

        // Screenshots may contain private information, so restrict this shared-location folder.
        var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("현재 사용자 계정을 확인하지 못했습니다.");
        var security = directory.GetAccessControl();
        security.SetAccessRuleProtection(true, false);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            security.RemoveAccessRuleAll(rule);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);

        foreach (var file in Directory.EnumerateFiles(_directory, "capture-*.png*"))
            File.Delete(file);
    }

    public string Add(Bitmap image)
    {
        var path = Path.Combine(_directory, $"{Prefix}{DateTime.Now:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.png");
        image.Save(path, ImageFormat.Png);
        _files.Insert(0, path);
        Changed?.Invoke();
        return path;
    }

    public void Update(string path, Bitmap image)
    {
        if (!_files.Contains(path, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("현재 세션의 캡처 파일이 아닙니다.");
        var temporary = path + ".tmp";
        try
        {
            image.Save(temporary, ImageFormat.Png);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        Changed?.Invoke();
    }

    public Bitmap Load(string path)
    {
        if (!_files.Contains(path, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("현재 세션의 캡처 파일이 아닙니다.");
        using var source = Image.FromFile(path);
        return new Bitmap(source);
    }
}
