using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using NetTraffic.Core.Models;

namespace NetTraffic.Core.Services;

/// <summary>
/// ETW 实时流量监控（任务管理器"网络"归因同款机制）：
/// 内核 NETWORK_TCPIP 事件（TCP/UDP 的 send/recv）带 PID 与字节数，按进程累加；
/// UI 定时快照（快照间隔的增量即速率）。需要管理员权限（内核 ETW session）。
/// </summary>
public sealed class EtwTrafficMonitor : IDisposable
{
    private TraceEventSession? _session;
    private Thread? _thread;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>PID → 累计字节数（收/发），UI 快照用。</summary>
    private readonly ConcurrentDictionary<int, (long Down, long Up)> _totals = new();

    /// <summary>PID → 进程名缓存。</summary>
    private readonly ConcurrentDictionary<int, string> _names = new();

    private long _lastSnapshotTick;

    public bool IsRunning => _session is not null;

    /// <summary>启动监控；非管理员或会话已存在时抛异常/返回 false。</summary>
    public bool Start(out string error)
    {
        error = "";
        if (_session is not null) return true;

        if (TraceEventSession.IsElevated() != true)
        {
            error = "需要管理员权限（内核 ETW 会话）。请以管理员重启工具集后再启动监控。";
            return false;
        }
        try
        {
            _session = new TraceEventSession("SystemToolkitNetTraffic", null)
            {
                StopOnDispose = true,
            };
            // 内核 provider：TCP/IP + UDP 事件（任务管理器网络归因同源）
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
        }
        catch (Exception ex)
        {
            _session?.Dispose();
            _session = null;
            error = $"启动 ETW 会话失败：{ex.Message}";
            return false;
        }

        _lastSnapshotTick = Environment.TickCount64;
        var source = _session.Source;
        _thread = new Thread(() =>
        {
            try
            {
                var kernel = source.Kernel;
                // TCP（IPv4+IPv6）
                kernel.TcpIpSend += e => Add(e.ProcessID, up: e.size);
                kernel.TcpIpRecv += e => Add(e.ProcessID, down: e.size);
                kernel.TcpIpSendIPV6 += e => Add(e.ProcessID, up: e.size);
                kernel.TcpIpRecvIPV6 += e => Add(e.ProcessID, down: e.size);
                // UDP（IPv4+IPv6）
                kernel.UdpIpSend += e => Add(e.ProcessID, up: e.size);
                kernel.UdpIpRecv += e => Add(e.ProcessID, down: e.size);
                kernel.UdpIpSendIPV6 += e => Add(e.ProcessID, up: e.size);
                kernel.UdpIpRecvIPV6 += e => Add(e.ProcessID, down: e.size);
                source.Process();
            }
            catch (Exception)
            {
                // 会话停止（Dispose）时正常退出
            }
        })
        { IsBackground = true, Name = "NetTraffic-EtwReader" };
        _thread.Start();
        return true;
    }

    /// <summary>把累计字节累加到 PID。</summary>
    private void Add(int pid, int down = 0, int up = 0)
    {
        if (pid <= 0) return;
        _names.TryAdd(pid, "");
        _totals.AddOrUpdate(pid,
            _ => (down, up),
            (_, v) => (v.Down + down, v.Up + up));
    }

    /// <summary>
    /// 取快照：返回按下载速率排序的进程列表；同时更新传入字典（Pid→行模型）的速率/累计/进程名。
    /// secondsWindow 用真实间隔（TickCount 差），避免定时器抖动。
    /// </summary>
    public List<ProcessTraffic> Snapshot(
        IReadOnlyDictionary<int, ProcessTraffic> view,
        out double totalDownKb, out double totalUpKb)
    {
        long now = Environment.TickCount64;
        double seconds = Math.Max(0.2, (now - _lastSnapshotTick) / 1000.0);
        _lastSnapshotTick = now;

        totalDownKb = totalUpKb = 0;
        var result = new List<ProcessTraffic>();
        foreach (var (pid, (downTotal, upTotal)) in _totals)
        {
            long prevDown = 0, prevUp = 0;
            if (view.TryGetValue(pid, out var row))
            {
                prevDown = row.DownTotal;
                prevUp = row.UpTotal;
            }

            double downRate = Math.Max(0, (downTotal - prevDown) / 1024.0 / seconds);
            double upRate = Math.Max(0, (upTotal - prevUp) / 1024.0 / seconds);

            var item = view.TryGetValue(pid, out var existing) ? existing : null;
            if (item is null)
            {
                item = new ProcessTraffic { Pid = pid, Name = ResolveName(pid) };
            }
            item.Name = ResolveName(pid);
            item.DownRate = downRate;
            item.UpRate = upRate;
            item.DownTotal = downTotal;
            item.UpTotal = upTotal;
            item.Exited = item.Exited || (downRate == 0 && upRate == 0 && !ProcessExists(pid));

            totalDownKb += downRate;
            totalUpKb += upRate;
            // 只保留有活动或累计非零的进程
            if (downRate > 0.05 || upRate > 0.05 || downTotal > 0 || upTotal > 0)
                result.Add(item);
        }

        return [.. result.OrderByDescending(r => r.DownRate).ThenByDescending(r => r.UpRate)];
    }

    private static bool ProcessExists(int pid)
    {
        try { Process.GetProcessById(pid); return true; }
        catch { return false; }
    }

    private string ResolveName(int pid)
    {
        if (_names.TryGetValue(pid, out var cached) && cached.Length > 0) return cached;
        string name;
        try { name = Process.GetProcessById(pid).ProcessName; }
        catch { name = $"PID {pid}"; }
        _names[pid] = name;
        return name;
    }

    /// <summary>清零会话累计（速率统计继续）。</summary>
    public void ResetTotals() => _totals.Clear();

    public void Stop()
    {
        try
        {
            _cts.Cancel();
            _session?.Dispose();
        }
        catch { }
        _session = null;
        _thread = null;
    }

    public void Dispose() => Stop();
}
