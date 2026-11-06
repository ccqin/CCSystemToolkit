using System.ComponentModel;

namespace PlcScanner.Core.Models;

/// <summary>被发现的设备（UI 行模型；Ports/Note 变更通知 UI 刷新）。</summary>
public sealed class DeviceItem : INotifyPropertyChanged
{
    public required uint IpNet { get; init; }
    public required string Ip { get; init; }
    public required string Mac { get; init; }

    private string _ports = "-";
    private string _note = "";

    /// <summary>开放端口（端口探测后更新）。</summary>
    public string Ports
    {
        get => _ports;
        set { _ports = value; PropertyChanged?.Invoke(this, new(nameof(Ports))); }
    }

    /// <summary>备注（本机 / ★ 疑似 SC 设备…）。</summary>
    public string Note
    {
        get => _note;
        set { _note = value; PropertyChanged?.Invoke(this, new(nameof(Note))); }
    }

    public bool IsLocal { get; init; }

    public bool IsScDevice => Note.Contains("★");

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>写入探测结果并按工控常见端口识别设备类型（扫描期与单行探测共用）。</summary>
    public void ApplyPorts(int[] open)
    {
        Ports = open.Length > 0 ? string.Join(",", open) : "-";
        if (IsLocal) { Note = "本机"; return; }
        Note = open switch
        {
            _ when open.Contains(21) && open.Contains(23) => "★ 疑似 SC 设备（Telnet+FTP）",
            _ when open.Contains(102) => "★ 西门子 S7 PLC（102）",
            _ when open.Contains(502) => "★ Modbus TCP 设备（502）",
            _ when open.Contains(44818) => "★ EtherNet/IP 设备（44818）",
            _ when open.Contains(80) || open.Contains(443) => "Web 管理界面",
            _ => "",
        };
    }
}
