namespace StartupMgr.Core.Models;

/// <summary>服务启动类型（注册表 Start 值）。</summary>
public enum ServiceStartType
{
    Boot,               // 0（驱动）
    System,             // 1（驱动）
    Automatic,          // 2 自动
    Manual,             // 3 手动
    Disabled,           // 4 禁用
}

/// <summary>一个 Windows 服务（用户态，不含纯驱动）。</summary>
public sealed class ServiceEntry
{
    public required string Name { get; init; }
    public string DisplayName { get; init; } = "";
    public string? Description { get; init; }
    public ServiceStartType StartType { get; set; }

    /// <summary>自动(延迟)启动。</summary>
    public bool Delayed { get; init; }

    /// <summary>ImagePath（如 svchost -k netsvcs）。</summary>
    public required string ImagePath { get; init; }

    /// <summary>svchost 服务的宿主 DLL（Parameters\ServiceDll）。</summary>
    public string? ServiceDll { get; init; }

    /// <summary>运行账户（LocalSystem/NetworkService…）。</summary>
    public string? Account { get; init; }

    public string? TargetPath { get; init; }
    public string? Publisher { get; init; }
    public bool IsResidual { get; init; }
    public bool IsSystem { get; init; }

    public string StartTypeText => (StartType, Delayed) switch
    {
        (ServiceStartType.Automatic, true) => "自动·延迟",
        (ServiceStartType.Automatic, false) => "自动",
        (ServiceStartType.Manual, _) => "手动",
        (ServiceStartType.Disabled, _) => "禁用",
        (ServiceStartType.Boot, _) => "Boot",
        _ => "System",
    };

    public string BadgeText => IsResidual ? "⚠ 残留" : IsSystem ? "✔ 系统" : "";
    public string SourceName => IsSystem ? "Microsoft / 系统" : Publisher ?? "未知来源";

    public int SortOrder => StartType switch
    {
        ServiceStartType.Automatic => Delayed ? 1 : 0,
        ServiceStartType.Manual => 2,
        _ => 3,
    };
}
