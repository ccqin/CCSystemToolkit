using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using PermDoctor.Core.Models;

namespace PermDoctor.Core.Services;

/// <summary>修复结果。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="Message">结果说明；失败时含原因与建议（提权 / 等效命令）。</param>
public sealed record FixResult(bool Success, string Message);

/// <summary>
/// 一键修复：授权 / 重置继承 / 取得所有权 / 删除授权条目。
/// 修改 DACL 需要目录的 WRITE_DAC 权限（通常所有者或管理员才有）；
/// 普通权限下被拒时返回可复制的 icacls/takeown 等效命令，由宿主提示以管理员重启后执行。
/// </summary>
public sealed class FixService
{
    /// <summary>
    /// 删除一条 ACL 授权（典型：清理孤儿 SID）。
    /// 需要 WRITE_DAC（通常管理员/所有者）；系统关键授权（SYSTEM/Administrators/TrustedInstaller）会被阻止。
    /// 仅影响本目录的安全描述符；子项继承该规则的会随之失去。
    /// </summary>
    public FixResult RemoveRule(string path, AclEntryView entry)
    {
        if (entry.IsSystemCritical)
        {
            return new FixResult(false,
                $"✘ 已阻止：{entry.Identity} 是系统关键授权（SYSTEM / Administrators / TrustedInstaller），" +
                "删除后管理员可能无法接管该目录。如确有需要，请用 icacls 手动处理。");
        }

        try
        {
            var dir = new DirectoryInfo(path);
            var sec = dir.GetAccessControl(AccessControlSections.Access);
            var sid = new SecurityIdentifier(entry.Sid);
            var rule = new FileSystemAccessRule(
                sid, entry.Rights,
                entry.Inheritance, entry.Propagation,
                entry.TypeText == "允许" ? AccessControlType.Allow : AccessControlType.Deny);
            sec.RemoveAccessRuleSpecific(rule);
            dir.SetAccessControl(sec);
            return new FixResult(true, $"✔ 已删除授权条目：{entry.Identity}（{entry.RightsText}）。");
        }
        catch (UnauthorizedAccessException)
        {
            return Denied($"删除 {entry.Identity} 的授权");
        }
        catch (Exception ex)
        {
            return new FixResult(false, $"✘ 删除失败：{ex.Message}");
        }
    }

    /// <summary>给当前用户授予 Modify（含子文件夹和文件继承）。最常用、影响最小。</summary>
    public FixResult GrantModify(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            var sec = dir.GetAccessControl(AccessControlSections.Access);
            using var identity = WindowsIdentity.GetCurrent();
            var rule = new FileSystemAccessRule(
                identity.User!,
                FileSystemRights.Modify | FileSystemRights.ReadPermissions | FileSystemRights.ChangePermissions,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);
            sec.AddAccessRule(rule);
            dir.SetAccessControl(sec);
            return new FixResult(true, $"✔ 已授予 {identity.Name} 修改权限（含子项）。");
        }
        catch (UnauthorizedAccessException)
        {
            return Denied("授予修改权限");
        }
        catch (Exception ex)
        {
            return new FixResult(false, $"✘ 授予失败：{ex.Message}");
        }
    }

    /// <summary>恢复从父目录继承权限（等价 icacls /reset，仅本层；可选级联）。</summary>
    public FixResult EnableInheritance(string path, bool recursive)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            var sec = dir.GetAccessControl(AccessControlSections.Access);
            sec.SetAccessRuleProtection(false, false);   // 启用继承、保留现有显式规则
            dir.SetAccessControl(sec);

            if (recursive)
            {
                foreach (var sub in dir.GetDirectories("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var s = sub.GetAccessControl(AccessControlSections.Access);
                        s.SetAccessRuleProtection(false, false);
                        sub.SetAccessControl(s);
                    }
                    catch
                    {
                        // 个别子目录失败不阻断整体（如符号链接指向的位置）
                    }
                }
            }
            return new FixResult(true, $"✔ 已{(recursive ? "级联" : "")}恢复继承：目录将重新接收父目录授权。");
        }
        catch (UnauthorizedAccessException)
        {
            return Denied("恢复继承");
        }
        catch (Exception ex)
        {
            return new FixResult(false, $"✘ 恢复继承失败：{ex.Message}");
        }
    }

    /// <summary>取得目录所有权（需要管理员 + SeTakeOwnershipPrivilege，失败时提示提权）。</summary>
    public FixResult TakeOwnership(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            var sec = dir.GetAccessControl(AccessControlSections.Owner);
            using var identity = WindowsIdentity.GetCurrent();
            sec.SetOwner(identity.User!);
            dir.SetAccessControl(sec);
            return new FixResult(true, $"✔ 所有者已改为 {identity.Name}；随后可执行“授予修改权限”。");
        }
        catch (UnauthorizedAccessException)
        {
            return new FixResult(false,
                "✘ 取得所有权被拒绝：需要管理员权限（提升后的进程拥有 SeTakeOwnership 特权）。" +
                "请点击“以管理员重启”后重试，或在管理员终端执行 takeown 命令。");
        }
        catch (Exception ex)
        {
            return new FixResult(false, $"✘ 取得所有权失败：{ex.Message}");
        }
    }

    private static FixResult Denied(string action) => new(false,
        $"✘ {action}被拒绝：当前账户无权修改该目录的安全描述符（WRITE_DAC）。" +
        "请以管理员身份重启工具集后重试，或复制下方的 icacls 命令到管理员终端执行。");
}
