using System.Diagnostics;
using Microsoft.Win32.TaskScheduler;
using StartupMgr.Core.Models;

namespace StartupMgr.Core.Services;

/// <summary>
/// 计划任务扫描器：基于 dahall/TaskScheduler（任务计划程序 COM API 封装，
/// 与 taskschd.msc 同一通道——TaskCache 注册表与 Tasks 目录在加固系统上对普通用户锁死时依然可用）。
/// 枚举：强类型触发器 / 动作 / 状态 / 上次与下次运行时间；操作：Enabled 开关与删除。
/// </summary>
public sealed class TaskScanner
{
    private static readonly Dictionary<TaskTriggerType, string> TriggerNames = new()
    {
        [TaskTriggerType.Logon] = "登录",
        [TaskTriggerType.Boot] = "开机",
        [TaskTriggerType.Time] = "一次",
        [TaskTriggerType.Daily] = "定时",
        [TaskTriggerType.Weekly] = "定时",
        [TaskTriggerType.Monthly] = "定时",
        [TaskTriggerType.MonthlyDOW] = "定时",
        [TaskTriggerType.Event] = "事件",
        [TaskTriggerType.Idle] = "空闲",
        [TaskTriggerType.SessionStateChange] = "会话",
        [TaskTriggerType.Registration] = "注册",
    };

    public List<TaskEntry> ScanAll(Action<string>? progress = null)
    {
        var entries = new List<TaskEntry>();
        progress?.Invoke("查询计划任务（COM）…");

        var ts = new TaskService();
        foreach (var task in EnumerateTasks(ts.RootFolder))
        {
            try
            {
                var def = task.Definition;

                var triggers = def.Triggers
                    .Select(t => TriggerNames.TryGetValue(t.TriggerType, out var label) ? label : t.TriggerType.ToString())
                    .Distinct()
                    .ToList();

                var exec = def.Actions.OfType<ExecAction>().FirstOrDefault(a => !string.IsNullOrEmpty(a.Path));
                var command = exec?.Path;
                var arguments = exec?.Arguments;
                var expanded = string.IsNullOrEmpty(command) ? null : Environment.ExpandEnvironmentVariables(command!);
                var target = expanded is null ? null : StartupScanner.ExtractTarget(expanded);
                var (publisher, residual) = InspectTarget(target);

                entries.Add(new TaskEntry
                {
                    Path = task.Path,
                    Triggers = triggers,
                    Command = expanded,
                    Arguments = string.IsNullOrEmpty(arguments) ? null : arguments,
                    State = task.State switch
                    {
                        TaskState.Disabled => 3,
                        TaskState.Running => 1,
                        TaskState.Queued => 2,
                        _ => 0,
                    },
                    LastRunText = task.LastRunTime > new DateTime(2000, 1, 1)
                        ? task.LastRunTime.ToString("yyyy-MM-dd HH:mm") : "",
                    NextRunText = task.NextRunTime > new DateTime(2000, 1, 1)
                        ? task.NextRunTime.ToString("yyyy-MM-dd HH:mm") : "",
                    TargetPath = target,
                    Publisher = publisher,
                    IsResidual = residual,
                    IsSystem = task.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
                                || publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true,
                });
            }
            catch
            {
                // 单任务失败（ACL/COM 拒绝）不阻断
            }
        }

        return [.. entries
            .OrderBy(e => e.IsLogonTrigger ? 0 : 1)
            .ThenBy(e => e.IsSystem ? 1 : 0)
            .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>递归枚举全部任务（含子文件夹）。</summary>
    private static IEnumerable<Microsoft.Win32.TaskScheduler.Task> EnumerateTasks(TaskFolder folder)
    {
        foreach (var task in folder.Tasks)
        {
            yield return task;
        }
        foreach (var sub in folder.SubFolders)
        {
            foreach (var task in EnumerateTasks(sub))
            {
                yield return task;
            }
        }
    }

    /// <summary>启用/禁用（COM，系统任务需管理员）/ 删除。</summary>
    public StartupResult Change(StartupMgrTaskAction action, string path)
    {
        try
        {
            var ts = new TaskService();
            var task = ts.GetTask(path);
            if (task is null) return new StartupResult(false, $"✘ 未找到任务「{path}」。");

            switch (action)
            {
                case StartupMgrTaskAction.Disable:
                    task.Enabled = false;
                    return new StartupResult(true, $"✔ 已禁用「{path}」。");
                case StartupMgrTaskAction.Enable:
                    task.Enabled = true;
                    return new StartupResult(true, $"✔ 已启用「{path}」。");
                default:
                    var folderPath = System.IO.Path.GetDirectoryName(path);
                    var folder = string.IsNullOrEmpty(folderPath) ? ts.RootFolder : ts.GetFolder(folderPath);
                    folder.DeleteTask(System.IO.Path.GetFileName(path));
                    return new StartupResult(true, $"✔ 已删除「{path}」。");
            }
        }
        catch (UnauthorizedAccessException)
        {
            return new StartupResult(false, "✘ 被拒绝：系统任务需要管理员权限（以管理员重启后重试）。");
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            return new StartupResult(false, $"✘ 系统拒绝（{ex.Message}）：系统任务需要管理员权限。");
        }
        catch (Exception ex)
        {
            return new StartupResult(false, $"✘ 操作失败：{ex.Message}");
        }
    }

    private static (string? Publisher, bool Residual) InspectTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return (null, false);
        try
        {
            if (!File.Exists(target)) return (null, true);
            var info = FileVersionInfo.GetVersionInfo(target);
            return (string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName.Trim(), false);
        }
        catch
        {
            return (null, false);
        }
    }
}

public enum StartupMgrTaskAction
{
    Enable,
    Disable,
    Delete,
}
