namespace StartupMgr.Core.Models;

/// <summary>启动项来源位置。</summary>
public enum StartupLocation
{
    HkcuRun,        // HKCU\...\CurrentVersion\Run
    HklmRun,        // HKLM\...\CurrentVersion\Run
    HklmRun32,      // HKLM\WOW6432Node\...\Run（32 位视图）
    HkcuRunOnce,
    HklmRunOnce,
    UserFolder,     // 用户启动文件夹
    CommonFolder,   // 全局启动文件夹（需管理员删）
}

public enum StartupStatus
{
    Enabled,
    Disabled,       // StartupApproved 标记禁用（任务管理器同一机制）
}

/// <summary>一个开机启动项。</summary>
public sealed class StartupEntry
{
    public required string Name { get; init; }

    /// <summary>完整命令行（或启动文件夹的文件路径）。</summary>
    public required string Command { get; init; }

    public StartupLocation Location { get; init; }
    public StartupStatus Status { get; set; }

    /// <summary>提取的目标 exe（残留检测/来源）。</summary>
    public string? TargetPath { get; init; }

    public string? Publisher { get; init; }

    /// <summary>目标文件不存在（卸载残留）。</summary>
    public bool IsResidual { get; init; }

    /// <summary>注册表路径（文件夹项为文件路径）。</summary>
    public required string SourcePath { get; init; }

    public bool IsSystem => Publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true
                            || TargetPath?.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase) == true;

    public string LocationText => Location switch
    {
        StartupLocation.HkcuRun => "HKCU·Run",
        StartupLocation.HklmRun => "HKLM·Run",
        StartupLocation.HklmRun32 => "HKLM·Run32",
        StartupLocation.HkcuRunOnce => "HKCU·RunOnce",
        StartupLocation.HklmRunOnce => "HKLM·RunOnce",
        StartupLocation.UserFolder => "用户启动夹",
        _ => "全局启动夹",
    };

    public string StatusText => Status == StartupStatus.Disabled ? "已禁用" : "启用";
    public bool IsEnabled => Status != StartupStatus.Disabled;
    public string SourceName => IsSystem ? "Microsoft / 系统" : Publisher ?? "未知来源";
}
