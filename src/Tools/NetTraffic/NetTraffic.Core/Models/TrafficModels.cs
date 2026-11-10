using System.ComponentModel;
namespace NetTraffic.Core.Models;

/// <summary>进程流量快照（UI 行模型；INPC 支持速率/累计实时刷新）。</summary>
public sealed class ProcessTraffic : INotifyPropertyChanged
{
    public required int Pid { get; init; }

    private string _name = "";
    /// <summary>进程名（不含扩展名；缓存命中后可更新）。</summary>
    public string Name { get => _name; set { _name = value; Pc(nameof(Name)); } }

    private double _downRate;
    private double _upRate;
    private long _downTotal;
    private long _upTotal;
    private int _connections;

    /// <summary>下载速率（KB/s，UI 1 秒快照）。</summary>
    public double DownRate { get => _downRate; set { _downRate = value; Pc(nameof(DownRate)); Pc(nameof(DownText)); } }

    /// <summary>上传速率（KB/s）。</summary>
    public double UpRate { get => _upRate; set { _upRate = value; Pc(nameof(UpRate)); Pc(nameof(UpText)); } }

    /// <summary>会话累计下载字节。</summary>
    public long DownTotal { get => _downTotal; set { _downTotal = value; Pc(nameof(DownTotal)); Pc(nameof(DownTotalText)); } }

    public long UpTotal { get => _upTotal; set { _upTotal = value; Pc(nameof(UpTotal)); Pc(nameof(UpTotalText)); } }

    /// <summary>当前 TCP 连接数（含 UDP 近似不计）。</summary>
    public int Connections { get => _connections; set { _connections = value; Pc(nameof(Connections)); } }

    /// <summary>是否已退出（进程不在但仍在收尾事件）。</summary>
    public bool Exited { get; set; }

    // 展示文本
    public string DownText => FormatSpeed(DownRate);
    public string UpText => FormatSpeed(UpRate);
    public string DownTotalText => FormatBytes(DownTotal);
    public string UpTotalText => FormatBytes(UpTotal);

    public static string FormatSpeed(double kbPerSec) => kbPerSec switch
    {
        >= 1024 * 1024 => $"{kbPerSec / 1024 / 1024:F2} GB/s",
        >= 1024 => $"{kbPerSec / 1024:F1} MB/s",
        >= 1 => $"{kbPerSec:F1} KB/s",
        > 0.05 => $"{kbPerSec * 1024:F0} B/s",
        _ => "—",
    };

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{(double)bytes / (1 << 30):F2} GB",
        >= 1 << 20 => $"{(double)bytes / (1 << 20):F1} MB",
        >= 1 << 10 => $"{(double)bytes / (1 << 10):F1} KB",
        > 0 => $"{bytes} B",
        _ => "—",
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Pc(string name) => PropertyChanged?.Invoke(this, new(name));
}
