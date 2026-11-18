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
    private readonly List<byte> _pending = [];   // 串口收到、尚未被读走的数据（原始字节，read/send 等待语义用）
    private readonly List<byte> _lineBuf = [];    // 监控显示的行缓冲（收到换行符才整行显示）
    private readonly List<byte[]> _lineHistory = []; // 已完成行的历史（最近 MaxLineHistory 行，按行号查询用）
    private bool _lastWasCr;
    private long _lastByteAt;
    private long _lineCounter;                   // 累计接收行号（串口打开起从 1 计，关闭串口时清零）
    private SerialPort? _port;
    private Timer? _pollTimer;                   // 常驻低频轮询（DataReceived 事件丢失兜底）

    /// <summary>接收行历史保留条数（0 = 全部缓存不限制；打开串口时设置）。</summary>
    public int LineHistoryLimit { get; private set; }

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

    /// <summary>AI 调用记录行（每次 MCP tools/call 的参数摘要，进卡片监控列表）。</summary>
    public void RaiseAiCall(string message) =>
        Traffic?.Invoke(new SerialTrafficItem { Direction = SerialDirection.AiCall, Message = message });

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

    /// <summary>打开串口（失败抛带中文原因的 InvalidOperationException；lineHistory：行历史保留条数，0=全部缓存）。</summary>
    public void Open(string portName, int baudRate, int dataBits, Parity parity, StopBits stopBits, int lineHistory = 0)
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
                    // 与 Xshell/串口助手默认一致：DTR/RTS 拉有效。
                    // .NET 默认两者全关，而不少嵌入式调试串口的使能/复位脚接在 DTR/RTS 上——
                    // 信号无效时设备完全不收发（实测 Xshell 可通、本工具收不到的字面案例）。
                    DtrEnable = true,
                    RtsEnable = true,
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
            LineHistoryLimit = lineHistory < 0 ? 0 : lineHistory;
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
            // 常驻低频轮询兜底：DataReceived 事件在部分驱动（com0com 实测）下会触发丢失，
            // 设备应答/主动上报数据可能永远进不了监控列表——等待窗口内另有高频轮询，
            // 这里保证任何时刻到达的数据都在 50ms 内被搬进接收缓冲并显示
            _pollTimer?.Dispose();
            _pollTimer = new Timer(_ => { try { ReadDriverIntoPending(); } catch { } }, null, 0, 50);
        }
        RaiseInfo($"已打开串口 {portName} @ {baudRate}（{SettingsText}）");
    }

    /// <summary>关闭串口（未打开时无操作）。</summary>
    public void Close()
    {
        Timer? timer;
        SerialPort? old;
        lock (_sync)
        {
            timer = _pollTimer;
            _pollTimer = null;
            old = _port;
            _port = null;
        }
        timer?.Dispose();
        if (old is null) return;
        var name = old.PortName;
        try { old.DataReceived -= OnDataReceived; } catch { }
        try { if (old.IsOpen) old.Close(); } catch { }
        old.Dispose();
        lock (_sync)
        {
            _pending.Clear();
            _lineBuf.Clear();
            _lineHistory.Clear();
            _lineCounter = 0;
            _lastWasCr = false;
        }
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
    /// 监控显示按行拆分：每完整一行（\r\n / \n / \r 结束）触发一条 FromPort 记录，
    /// 未收满的尾巴留行缓冲，空闲 150ms 后兜底显示（避免长输出永远停在半行）。
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
                SplitLines(chunk);
            }
            else if (_lineBuf.Count > 0 && Environment.TickCount64 - _lastByteAt > 150)
            {
                // 行缓冲尾巴：无新数据超过 150ms，按一条不完整行显示
                EmitLine();
            }
        }
        return chunk?.Length ?? 0;
    }

    /// <summary>按行拆分并逐行触发 FromPort 记录（\r\n 合并为一个行结束；需持有 _sync）。</summary>
    private void SplitLines(byte[] chunk)
    {
        _lastByteAt = Environment.TickCount64;
        foreach (var b in chunk)
        {
            if (b == (byte)'\r')
            {
                EmitLine();
                _lastWasCr = true;
            }
            else if (b == (byte)'\n')
            {
                if (_lastWasCr)
                {
                    _lastWasCr = false;   // \r\n 合并为一个行结束
                }
                else
                {
                    EmitLine();
                }
            }
            else
            {
                _lastWasCr = false;
                _lineBuf.Add(b);
            }
        }
    }

    /// <summary>把行缓冲作为一条 FromPort 记录触发并写入行历史（空行也计入，保持设备输出的行号对齐）。</summary>
    private void EmitLine()
    {
        var line = _lineBuf.ToArray();
        _lineBuf.Clear();
        _lastWasCr = false;
        _lineHistory.Add(line);
        if (LineHistoryLimit > 0 && _lineHistory.Count > LineHistoryLimit) _lineHistory.RemoveAt(0);
        _lineCounter++;
        Traffic?.Invoke(new SerialTrafficItem { Direction = SerialDirection.FromPort, Data = line });
    }

    /// <summary>累计接收行号（串口打开起计）。</summary>
    public long LineCount { get { lock (_sync) return _lineCounter; } }

    /// <summary>历史中最早一条的行号（无历史时为 0）。</summary>
    public long FirstLineNo
    {
        get
        {
            lock (_sync) return _lineHistory.Count == 0 ? 0 : _lineCounter - _lineHistory.Count + 1;
        }
    }

    /// <summary>读取最近 count 条接收行（带行号，超出现有历史时按现有条数返回）。</summary>
    public (long No, byte[] Data)[] GetRecentLines(int count)
    {
        if (count <= 0) return [];
        lock (_sync)
        {
            if (count > _lineHistory.Count) count = _lineHistory.Count;
            var result = new List<(long, byte[])>();
            var firstNo = _lineCounter - _lineHistory.Count + 1;
            for (var i = _lineHistory.Count - count; i < _lineHistory.Count; i++)
                result.Add((firstNo + i, _lineHistory[i]));
            return [.. result];
        }
    }

    /// <summary>按行号范围读取接收行（from/to 自动收敛到历史窗口内，from 超出最新行号时返回空）。</summary>
    public (long No, byte[] Data)[] GetLines(long from, long to)
    {
        lock (_sync)
        {
            if (_lineHistory.Count == 0) return [];
            var firstNo = _lineCounter - _lineHistory.Count + 1;
            var lastNo = _lineCounter;
            if (from < firstNo) from = firstNo;
            if (to > lastNo) to = lastNo;
            var result = new List<(long, byte[])>();
            for (var n = from; n <= to; n++)
                result.Add((n, _lineHistory[(int)(n - firstNo)]));
            return [.. result];
        }
    }

    /// <summary>清空未读接收缓冲（行历史保留，便于继续按行号查询）。</summary>
    public void ClearBuffer() { lock (_sync) _pending.Clear(); }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e) => ReadDriverIntoPending();
}
