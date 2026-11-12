using System.IO.Ports;
using System.Management;
using System.Text;

namespace SerialMcpTool.Core;

/// <summary>
/// 串口服务：枚举/打开/关闭/收发，全部收发以 <see cref="Traffic"/> 事件通知（卡片监控与 MCP 服务器共用）。
///
/// 读取路径不依赖 <see cref="SerialPort.DataReceived"/> 事件（实测 com0com 虚拟串口下事件循环存在
/// 触发丢失：应答字节到达驱动缓冲后事件不再上报），等待/读取一律主动轮询驱动缓冲（BytesToRead + Read），
/// 事件仅作为无人等待时的异步搬运补充。UI 线程外触发（串口线程、HTTP 线程），订阅方自行调度到 UI 线程。
/// </summary>
public sealed class SerialPortService
{
    private readonly object _sync = new();
    private readonly ManualResetEventSlim _dataEvent = new(false);
    private readonly List<byte> _pending = [];   // 串口收到、尚未被读走的数据
    private SerialPort? _port;

    /// <summary>每次收发/提示触发一条（方向 + 数据/消息）。</summary>
    public event Action<SerialTrafficItem>? Traffic;

    public bool IsOpen { get { lock (_sync) return _port?.IsOpen == true; } }
    public string PortName { get { lock (_sync) return _port?.PortName ?? ""; } }
    public int BaudRate { get; private set; }

    /// <summary>参数摘要（如 "8N1"）。</summary>
    public string SettingsText { get; private set; } = "";

    public long TotalSent { get; private set; }
    public long TotalReceived { get; private set; }
    public int PendingCount { get { lock (_sync) return _pending.Count; } }

    /// <summary>系统提示行（进卡片监控列表）。</summary>
    public void RaiseInfo(string message) =>
        Traffic?.Invoke(new SerialTrafficItem { Direction = SerialDirection.Info, Message = message });

    /// <summary>错误提示行（进卡片监控列表）。</summary>
    public void RaiseError(string message) =>
        Traffic?.Invoke(new SerialTrafficItem { Direction = SerialDirection.Error, Message = message });

    /// <summary>枚举本机串口名（按名称排序）。</summary>
    public static List<string> GetPortNames() =>
        SerialPort.GetPortNames()
            .Select(n => n.TrimEnd(':'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>WMI 查询串口设备描述（Win32_SerialPort；失败时返回空表，调用方降级为仅名称）。</summary>
    public Dictionary<string, string> GetPortDescriptions()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceID, Caption, Description FROM Win32_SerialPort");
            foreach (var obj in searcher.Get())
            {
                using var mo = (ManagementObject)obj;
                if (mo["DeviceID"] is not string id || id.Length == 0) continue;
                var text = mo["Caption"] as string;
                if (string.IsNullOrWhiteSpace(text)) text = mo["Description"] as string;
                if (!string.IsNullOrWhiteSpace(text)) result[id] = text;
            }
        }
        catch
        { /* WMI 不可用时静默降级为仅名称 */ }
        return result;
    }

    /// <summary>打开串口（失败抛带中文原因的 InvalidOperationException）。</summary>
    public void Open(string portName, int baudRate, int dataBits, Parity parity, StopBits stopBits)
    {
        lock (_sync)
        {
            if (_port?.IsOpen == true)
                throw new InvalidOperationException($"串口 {_port.PortName} 已打开，请先关闭再打开其他串口");
            SerialPort p;
            try
            {
                p = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
                {
                    ReadTimeout = 50,
                    WriteTimeout = 3000,
                    ReadBufferSize = 1 << 16,
                    WriteBufferSize = 1 << 16,
                };
                p.Open();
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException($"打开 {portName} 失败：拒绝访问——串口通常已被其他程序占用", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"打开 {portName} 失败：串口不存在或不可用（{ex.Message}）", ex);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"打开 {portName} 失败：参数无效（{ex.Message}）", ex);
            }
            p.DataReceived += OnDataReceived;
            _port = p;
            BaudRate = baudRate;
            SettingsText = dataBits + (parity switch
            {
                Parity.Even => "E",
                Parity.Odd => "O",
                _ => "N",
            }) + stopBits switch
            {
                StopBits.Two => "2",
                StopBits.OnePointFive => "1.5",
                _ => "1",
            };
            // 打开前积压在驱动缓冲的数据（如虚拟串口对端先写入的）主动搬入
            ReadDriverIntoPending();
        }
        RaiseInfo($"已打开串口 {portName} @ {baudRate}（{SettingsText}）");
    }

    /// <summary>关闭串口（未打开时无操作）。</summary>
    public void Close()
    {
        SerialPort? old;
        lock (_sync)
        {
            old = _port;
            _port = null;
        }
        if (old is null) return;
        var name = old.PortName;
        try { old.DataReceived -= OnDataReceived; } catch { }
        try { if (old.IsOpen) old.Close(); } catch { }
        old.Dispose();
        lock (_sync) _pending.Clear();
        RaiseInfo($"已关闭串口 {name}");
    }

    /// <summary>发送数据（失败抛带中文原因的异常）；direction 区分 AI 下发 / 界面手动。</summary>
    public void Write(byte[] data, SerialDirection direction)
    {
        SerialPort? p;
        lock (_sync) p = _port;
        if (p is null || !p.IsOpen)
            throw new InvalidOperationException("串口未打开，无法发送（先调用 open_serial_port 或在界面打开串口）");
        try
        {
            p.Write(data, 0, data.Length);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException("串口写入超时", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("串口写入失败：" + ex.Message, ex);
        }
        TotalSent += data.Length;
        Traffic?.Invoke(new SerialTrafficItem { Direction = direction, Data = data });
    }

    /// <summary>当前接收缓冲区位置（发送后等待应答时以此为界，只收发送之后到达的数据）。</summary>
    public int PendingMark() { lock (_sync) return _pending.Count; }

    /// <summary>
    /// 收集 mark 之后到达的应答：每轮先主动轮询驱动缓冲再查接收缓冲；
    /// 静默 quietMs 判定完整或总超时 waitMs 截止；收到的数据从缓冲区移除。
    /// </summary>
    public byte[] CollectResponse(int mark, int waitMs, int quietMs = 60)
    {
        var result = new List<byte>();
        long lastArrival = Environment.TickCount64;
        var have = false;
        var deadline = Environment.TickCount64 + Math.Max(0, waitMs);
        while (true)
        {
            ReadDriverIntoPending();
            lock (_sync)
            {
                var avail = _pending.Count - mark;
                if (avail > 0)
                {
                    var chunk = new byte[avail];
                    _pending.CopyTo(mark, chunk, 0, avail);
                    _pending.RemoveRange(mark, avail);
                    result.AddRange(chunk);
                    have = true;
                    lastArrival = Environment.TickCount64;
                }
            }
            var now = Environment.TickCount64;
            if (have && now - lastArrival >= quietMs) break;
            if (now >= deadline) break;
            _dataEvent.Wait(12);
            _dataEvent.Reset();
        }
        return [.. result];
    }

    /// <summary>
    /// 读取串口返回的数据：timeoutMs 小于等于 0 立即返回缓冲数据（可能为空）；
    /// 大于 0 表示最多等待新数据的毫秒数（有数据后按 60ms 静默期收满一段）。
    /// </summary>
    public byte[] ReadData(int timeoutMs)
    {
        ReadDriverIntoPending();
        lock (_sync)
        {
            var buffered = _pending.ToArray();
            if (buffered.Length > 0)
            {
                _pending.Clear();
                return buffered;
            }
            if (timeoutMs <= 0) return [];
        }
        return CollectResponse(0, timeoutMs);
    }

    /// <summary>
    /// 从驱动缓冲主动读取全部可用字节并入接收缓冲（等待/读取路径与 DataReceived 事件共用；
    /// 事件循环存在触发丢失的驱动场景，因此主路径全部主动轮询）。
    /// </summary>
    private int ReadDriverIntoPending()
    {
        byte[]? chunk = null;
        lock (_sync)
        {
            if (_port is not { IsOpen: true } p) return 0;
            var buf = new List<byte>();
            var tmp = new byte[8192];
            while (p.BytesToRead > 0 && buf.Count < (1 << 20))
            {
                var want = Math.Min(p.BytesToRead, tmp.Length);
                int n;
                try { n = p.Read(tmp, 0, want); }
                catch { break; }
                if (n <= 0) break;
                for (var i = 0; i < n; i++) buf.Add(tmp[i]);
            }
            if (buf.Count > 0)
            {
                chunk = buf.ToArray();
                _pending.AddRange(chunk);
                TotalReceived += chunk.Length;
                _dataEvent.Set();
            }
        }
        if (chunk is { Length: > 0 })
            Traffic?.Invoke(new SerialTrafficItem { Direction = SerialDirection.FromPort, Data = chunk });
        return chunk?.Length ?? 0;
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e) => ReadDriverIntoPending();
}
