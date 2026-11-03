using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using PermDoctor.Core.Models;

namespace PermDoctor.Core.Services;

/// <summary>
/// 目录 ACL 读取与有效权限计算。
/// 有效权限按“当前用户 + 所属组 SID 集合”匹配规则累加，Deny 优先扣除；
/// 另提供真实的创建/删除临时子目录实测（最贴近安装程序“错误 5”场景）。
/// </summary>
public sealed class AclService
{
    /// <summary>读取目录 ACL（所有者、继承状态、全部规则），并标注孤儿 SID 与是否匹配当前用户。</summary>
    public DirectoryAclInfo ReadAcl(string path)
    {
        var dir = new DirectoryInfo(path);
        // 只读 DACL + 所有者：读 Audit（SACL）需要 SeSecurityPrivilege（管理员），普通权限会抛异常
        var sec = dir.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);

        // 当前用户 + 所属组的 SID 集合（有效权限的匹配基础）
        using var identity = WindowsIdentity.GetCurrent();
        var mySids = new HashSet<string>(identity.Groups.Select(g => g.Value), StringComparer.OrdinalIgnoreCase)
        {
            identity.User!.Value,
        };

        var entries = new List<AclEntryView>();
        foreach (FileSystemAccessRule rule in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var sid = rule.IdentityReference.Value;
            bool orphan = false;
            string name;
            try
            {
                name = rule.IdentityReference.Translate(typeof(NTAccount)).Value;
            }
            catch (IdentityNotMappedException)
            {
                // 本机/域都无法映射：典型于旧系统残留授权（目录从别的电脑拷来、系统重装后）
                orphan = true;
                name = sid;
            }

            entries.Add(new AclEntryView
            {
                Identity = name,
                Sid = sid,
                IsOrphan = orphan,
                Rights = rule.FileSystemRights,
                RightsText = RightsText(rule.FileSystemRights),
                TypeText = rule.AccessControlType == AccessControlType.Allow ? "允许" : "拒绝",
                AppliesTo = AppliesToText(rule),
                IsInherited = rule.IsInherited,
                AppliesToSelf = !rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly),
                AppliesToCurrentUser = mySids.Contains(sid),
                Inheritance = rule.InheritanceFlags,
                Propagation = rule.PropagationFlags,
            });
        }

        // 排序：匹配当前用户的条目最前（决定你的有效权限），孤儿 SID 次之，其余按名称
        entries = [.. entries
            .OrderByDescending(e => e.AppliesToCurrentUser)
            .ThenBy(e => e.IsOrphan ? 0 : 1)
            .ThenBy(e => e.Identity, StringComparer.OrdinalIgnoreCase)];

        return new DirectoryAclInfo(
            sec.GetOwner(typeof(NTAccount))?.Value ?? "<未知>",
            !sec.AreAccessRulesProtected,
            entries,
            mySids);
    }

    /// <summary>计算当前用户对目录的有效权限（匹配用户+组，Deny 优先）。</summary>
    public FileSystemRights EffectiveRights(DirectoryAclInfo acl)
    {
        var allowed = default(FileSystemRights);
        var denied = default(FileSystemRights);
        foreach (var e in acl.Entries)
        {
            if (!e.AppliesToCurrentUser) continue;
            if (!e.AppliesToSelf) continue;   // IO（仅继承）规则不作用于本文件夹
            if (e.TypeText == "允许") allowed |= e.Rights;
            else denied |= e.Rights;
        }
        return allowed & ~denied;
    }

    /// <summary>真实写入实测：创建并删除临时子目录（安装程序报“错误 5”的直接复现）。</summary>
    public (WriteTestResult Result, string? Error) TestWrite(string path)
    {
        var probe = System.IO.Path.Combine(path, ".permdoctor-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(probe);
            try
            {
                Directory.Delete(probe);
                return (WriteTestResult.Ok, null);
            }
            catch (Exception ex)
            {
                return (WriteTestResult.CleanupDenied, ex.Message);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return (WriteTestResult.CreateDenied, ex.Message);
        }
        catch (Exception ex)
        {
            return (WriteTestResult.Error, ex.Message);
        }
    }

    /// <summary>FileSystemRights → 友好文本（按最大权限归并，模仿资源管理器叫法）。</summary>
    public static string RightsText(FileSystemRights r)
    {
        if (r.HasFlag(FileSystemRights.FullControl)) return "完全控制";
        if (r.HasFlag(FileSystemRights.Modify)) return "修改";
        if (r.HasFlag(FileSystemRights.Write) && r.HasFlag(FileSystemRights.ReadAndExecute)) return "读取和写入";
        if (r.HasFlag(FileSystemRights.ReadAndExecute) || r == FileSystemRights.Read) return "读取和执行";
        if (r.HasFlag(FileSystemRights.Write)) return "写入";
        // 复合特例：目录的“写入”实际是 CreateDirectories|CreateFiles 等位组合
        if (r.HasFlag(FileSystemRights.CreateDirectories)) return "创建文件夹/附加数据";
        if (r.HasFlag(FileSystemRights.ListDirectory)) return "列出文件夹内容";
        return r.ToString();
    }

    /// <summary>继承/传播标志 → “应用于”文本（对应 icacls 的 (OI)(CI)(IO)）。</summary>
    public static string AppliesToText(FileSystemAccessRule rule)
    {
        bool oi = rule.InheritanceFlags.HasFlag(InheritanceFlags.ObjectInherit);    // 文件
        bool ci = rule.InheritanceFlags.HasFlag(InheritanceFlags.ContainerInherit); // 子文件夹
        bool io = rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly);      // 仅子项

        return (oi, ci, io) switch
        {
            (true, true, false) => "此文件夹、子文件夹和文件",
            (true, true, true) => "仅子文件夹和文件",
            (false, true, false) => "此文件夹和子文件夹",
            (true, false, false) => "此文件夹和文件",
            (false, false, true) => "仅子项",
            _ => "仅此文件夹",
        };
    }
}

/// <summary>目录 ACL 快照。</summary>
/// <param name="Owner">所有者（NTAccount）。</param>
/// <param name="InheritanceEnabled">是否启用从父目录继承。</param>
/// <param name="Entries">规则明细（含孤儿/匹配标注）。</param>
/// <param name="CurrentUserSids">当前用户 + 所属组 SID 集合。</param>
public sealed record DirectoryAclInfo(
    string Owner,
    bool InheritanceEnabled,
    IReadOnlyList<AclEntryView> Entries,
    HashSet<string> CurrentUserSids);
