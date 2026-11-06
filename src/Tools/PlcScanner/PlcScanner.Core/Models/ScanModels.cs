namespace PlcScanner.Core.Models;

/// <summary>被发现的设备（UI 行模型）。</summary>
public sealed class DeviceItem
{
    public required uint IpNet { get; init; }
    public required string Ip { get; init; }
    public required string Mac { get; init; }

    /// <summary>开放端口（端口探测后更新）。</summary>
    public string Ports { get; set; } = "-";

    /// <summary>备注（本机 / ★ 疑似 SC 设备…）。</summary>
    public string Note { get; set; } = "";

    public bool IsLocal { get; init; }

    public bool IsScDevice => Note.Contains("SC 设备");
}
