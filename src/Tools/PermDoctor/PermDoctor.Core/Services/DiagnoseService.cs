using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using PermDoctor.Core.Models;

namespace PermDoctor.Core.Services;

/// <summary>
/// 诊断：对一个目录执行“ACL 读取 + 有效权限计算 + 写入实测”，
/// 产出问题清单（缺哪些权限、为什么）、修复方案与等效 icacls 命令。
/// </summary>
public sealed class DiagnoseService(AclService acl)
{
    /// <summary>安装/写入场景需要的权限位。</summary>
    private const FileSystemRights RequiredForInstall =
        FileSystemRights.CreateDirectories | FileSystemRights.CreateFiles | FileSystemRights.Write |
        FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Modify | FileSystemRights.FullControl;

    public DiagnosisReport Diagnose(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        var exists = Directory.Exists(path);
        if (!exists)
        {
            return new DiagnosisReport
            {
                Path = path,
                Exists = false,
                EffectiveRightsText = "—",
                WriteTest = WriteTestResult.NotRun,
                Problems = ["目录不存在。若安装程序本应创建它，请先检查父目录的写权限（对父目录运行诊断）。"],
                FixCommands = FixCommands(path),
            };
        }

        var info = acl.ReadAcl(path);
        var effective = acl.EffectiveRights(info);
        var (writeTest, writeErr) = acl.TestWrite(path);

        // 缺失权限：所需位中当前用户一个都没有的
        var missing = new List<string>();
        if (!effective.HasFlag(FileSystemRights.CreateDirectories) && !effective.HasFlag(FileSystemRights.Write)
            && !effective.HasFlag(FileSystemRights.Modify) && !effective.HasFlag(FileSystemRights.FullControl))
        {
            missing.Add("创建文件夹/写入（当前用户只有读取类权限，安装程序无法在此创建目录 → 错误 5）");
        }

        // 问题清单（按严重程度）
        var problems = new List<string>();
        var orphans = info.Entries.Where(e => e.IsOrphan).ToList();
        if (writeTest == WriteTestResult.CreateDenied)
        {
            problems.Add($"✘ 实测创建子目录被拒绝：{writeErr ?? "拒绝访问"} —— 与安装程序“错误 5”一致。");
        }
        else if (writeTest == WriteTestResult.CleanupDenied)
        {
            problems.Add("△ 可以创建子目录，但删除失败（残留清理权限缺失）。");
        }
        else if (writeTest == WriteTestResult.Error)
        {
            problems.Add($"△ 写入实测异常：{writeErr}");
        }

        if (missing.Count > 0)
        {
            problems.Add($"✘ 当前用户缺少写入权限：{string.Join("；", missing)}");
        }

        if (orphans.Count > 0)
        {
            var detail = string.Join("；", orphans.Select(o => o.Sid));
            problems.Add($"△ 发现 {orphans.Count} 条【孤儿 SID】授权 —— 这些账户来自旧系统/其他电脑，本机无法识别，" +
                         $"它们的权限对你无效：{detail}。" +
                         "若目录是从别处拷贝或系统重装过，这正是你失去权限的原因（见下方权限明细表标 ⚠ 的行）。");
        }

        if (!info.InheritanceEnabled)
        {
            problems.Add("△ 该目录已断开继承（不接收父目录权限），父目录的授权在这里不生效。");
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (info.Owner != identity.Name)
        {
            problems.Add($"△ 所有者是 {info.Owner}（不是当前用户 {identity.Name}），修改权限可能需要管理员。");
        }

        if (problems.Count == 0 && writeTest == WriteTestResult.Ok)
        {
            problems.Add("✔ 未发现问题：权限正常，可创建/删除子目录。");
        }

        return new DiagnosisReport
        {
            Path = path,
            Exists = true,
            Owner = info.Owner,
            InheritanceEnabled = info.InheritanceEnabled,
            EffectiveRights = effective,
            EffectiveRightsText = AclService.RightsText(effective),
            WriteTest = writeTest,
            WriteTestError = writeErr,
            MissingRights = missing,
            Problems = problems,
            Entries = info.Entries,
            FixCommands = FixCommands(path),
        };
    }

    /// <summary>等效命令行方案（可在管理员终端直接执行；UI 提供一键执行与复制）。</summary>
    public static IReadOnlyList<string> FixCommands(string path)
    {
        var user = WindowsIdentity.GetCurrent().Name;
        return
        [
            $":: 1) 给当前用户授予修改权限（含子项，推荐先试）",
            $"icacls \"{path}\" /grant \"{user}:(OI)(CI)M\" /T",
            $":: 2) 若仍拒绝 → 取得所有权后再授权（需要管理员）",
            $"takeown /F \"{path}\" /R /D Y",
            $"icacls \"{path}\" /grant \"{user}:(OI)(CI)F\" /T",
            $":: 3) 恢复从父目录继承（会保留父目录的授权结构）",
            $"icacls \"{path}\" /reset /T",
        ];
    }
}
