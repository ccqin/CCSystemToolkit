using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Toolkit.Shell.Mcp;

namespace SerialMcpTool.Core;

/// <summary>
/// 串口 MCP 运行时（进程级单例）：持有串口服务与收发监控，实现 <see cref="IMcpServiceDefinition"/>
/// 作为「串口能力」注册到 McpServiceRegistry——由聚合网关（McpGatewayRuntime）统一对外，
/// 本类不再监听端口。串口与监控状态跨视图存活。
/// </summary>
public sealed class SerialMcpRuntime : IMcpServiceDefinition
{
    private const int MaxHistory = 1000;

    private readonly object _historyLock = new();
    private readonly List<SerialTrafficItem> _history = [];
    private bool _enabled = true;
    private long _calls;

    private SerialMcpRuntime() => Serial.Traffic += Record;

    public static SerialMcpRuntime Instance { get; } = new();

    /// <summary>串口服务（UI 与 MCP 工具共用同一实例）。</summary>
    public SerialPortService Serial { get; } = new();

    /// <summary>每次收发/提示触发（UI 线程外，订阅方自行调度）。</summary>
    public event Action<SerialTrafficItem>? Traffic;

    /// <summary>重进工具视图时的历史回放（最近 MaxHistory 条快照）。</summary>
    public SerialTrafficItem[] Snapshot()
    {
        lock (_historyLock) return [.. _history];
    }

    /// <summary>清空监控历史。</summary>
    public void ClearHistory()
    {
        lock (_historyLock) _history.Clear();
    }

    private void Record(SerialTrafficItem item)
    {
        lock (_historyLock)
        {
            _history.Add(item);
            while (_history.Count > MaxHistory) _history.RemoveAt(0);
        }
        Traffic?.Invoke(item);
    }

    internal void RaiseInfo(string message) =>
        Record(new SerialTrafficItem { Direction = SerialDirection.Info, Message = message });

    internal void RaiseError(string message) =>
        Record(new SerialTrafficItem { Direction = SerialDirection.Error, Message = message });

    private void RaiseAiCall(string message) =>
        Record(new SerialTrafficItem { Direction = SerialDirection.AiCall, Message = message });

    // ===== IMcpServiceDefinition（能力） =====

    public string Id => "serial";
    public string Name => "串口 MCP";
    public string Description => "AI 客户端经 MCP 协议打开串口、收发数据（经聚合网关对外；行历史容量可配置）";

    public bool Enabled => _enabled;

    public long TotalCalls => _calls;

    public event Action? StateChanged;

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        RaiseInfo(enabled ? "串口能力已启用（工具进入网关清单）" : "串口能力已停用（工具从网关清单移除）");
        StateChanged?.Invoke();
    }

    public JsonArray BuildTools()
    {
        return
        [
            new JsonObject
            {
                ["name"] = "list_serial_ports",
                ["description"] = "列出本机所有可用串口（含设备描述）。操作串口前先调用它确认串口名。",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(),
                },
            },
            new JsonObject
            {
                ["name"] = "open_serial_port",
                ["description"] = "打开串口。默认 115200 波特率、8 数据位、无校验、1 停止位（8N1）。串口不存在或被其他程序占用时会失败。",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["port"] = new JsonObject { ["type"] = "string", ["description"] = "串口名，如 COM3（先用 list_serial_ports 查询）" },
                        ["baud_rate"] = new JsonObject { ["type"] = "integer", ["default"] = 115200, ["description"] = "波特率" },
                        ["data_bits"] = new JsonObject { ["type"] = "integer", ["default"] = 8, ["description"] = "数据位 7 或 8" },
                        ["parity"] = new JsonObject
                        {
                            ["type"] = "string", ["enum"] = new JsonArray("none", "even", "odd"), ["default"] = "none",
                            ["description"] = "校验位",
                        },
                        ["stop_bits"] = new JsonObject
                        {
                            ["type"] = "string", ["enum"] = new JsonArray("one", "two", "one_point_five"), ["default"] = "one",
                            ["description"] = "停止位",
                        },
                        ["line_history"] = new JsonObject
                        {
                            ["type"] = "integer", ["default"] = 0,
                            ["description"] = "接收行历史保留条数：0=全部缓存（默认）；大于 0 时只保留最近 N 行（超出淘汰最旧行）",
                        },
                    },
                    ["required"] = new JsonArray("port"),
                },
            },
            new JsonObject
            {
                ["name"] = "close_serial_port",
                ["description"] = "关闭当前串口。",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(),
                },
            },
            new JsonObject
            {
                ["name"] = "send_serial_data",
                ["description"] = "向串口发送数据，并默认等待串口应答（wait_response_ms，默认 500，0 表示不等待）。文本格式支持 \\r \\n \\t \\xNN 转义；发送二进制用 format=hex。newline=true 时文本末尾自动附加 \\r\\n（多数 shell/固件要换行才执行）。",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["data"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "要发送的内容：format=text 时为文本（如 \"AT\"，配合 newline 自动补换行）；format=hex 时为十六进制（如 \"41 0D 0A\"）",
                        },
                        ["format"] = new JsonObject
                        {
                            ["type"] = "string", ["enum"] = new JsonArray("text", "hex"), ["default"] = "text",
                            ["description"] = "内容格式",
                        },
                        ["wait_response_ms"] = new JsonObject
                        {
                            ["type"] = "integer", ["default"] = 500,
                            ["description"] = "发送后等待串口应答的毫秒数（收到数据后再静默 60ms 即返回完整一段）；0 表示只发不收",
                        },
                        ["newline"] = new JsonObject
                        {
                            ["type"] = "boolean", ["default"] = false,
                            ["description"] = "text 模式下内容末尾自动附加 \\r\\n（末尾已有换行则不重复附加）",
                        },
                    },
                    ["required"] = new JsonArray("data"),
                },
            },
            new JsonObject
            {
                ["name"] = "read_serial_data",
                ["description"] = "读取串口返回的数据。默认读接收缓冲区；timeout_ms 大于 0 时最多等待新数据的毫秒数。lines 大于 0 时改为返回最近 N 条接收行（带行号，单次最多 5000 行）；from_line/to_line 按行号范围返回（如查看第 5-10 行）。行历史容量在 open_serial_port 的 line_history 配置（默认 0=全部缓存）。",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["timeout_ms"] = new JsonObject
                        {
                            ["type"] = "integer", ["default"] = 0,
                            ["description"] = "实时读：等待新数据的毫秒数（0 = 立即返回缓冲数据）",
                        },
                        ["lines"] = new JsonObject
                        {
                            ["type"] = "integer", ["default"] = 0,
                            ["description"] = "返回最近 N 条接收行（带行号，如 lines=20 看最后 20 行）",
                        },
                        ["from_line"] = new JsonObject
                        {
                            ["type"] = "integer", ["default"] = 0,
                            ["description"] = "按行号读取的起点（与 to_line 配合）",
                        },
                        ["to_line"] = new JsonObject
                        {
                            ["type"] = "integer", ["default"] = 0,
                            ["description"] = "按行号读取的终点（与 from_line 配合）",
                        },
                    },
                },
            },
            new JsonObject
            {
                ["name"] = "clear_serial_buffer",
                ["description"] = "清空串口未读接收缓冲（丢弃设备已返回但尚未读取的数据；行历史保留不受影响）。处理脏数据后重新等待应答时使用。",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(),
                },
            },
            new JsonObject
            {
                ["name"] = "get_serial_status",
                ["description"] = "查询串口与服务当前状态（串口设置、未读缓冲字节数、累计收发字节数）。",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(),
                },
            },
        ];
    }

    public string ExecuteTool(string name, JsonElement args)
    {
        var result = name switch
        {
            "list_serial_ports" => ListPorts(),
            "open_serial_port" => OpenPort(args),
            "close_serial_port" => ClosePort(),
            "send_serial_data" => SendData(args),
            "read_serial_data" => ReadData(args),
            "clear_serial_buffer" => ClearBuffer(),
            "get_serial_status" => GetStatus(),
            _ => throw new InvalidOperationException($"未知工具：{name}"),
        };
        _calls++;
        return result;
    }

    public void OnToolCalled(string name, JsonElement args, string result, bool isError)
    {
        RaiseAiCall(isError
            ? $"AI 调用 {name}{SummarizeArgs(args)} 失败：{result}"
            : $"AI 调用 {name}{SummarizeArgs(args)}");
    }

    private static string SummarizeArgs(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object) return "()";
        var json = args.GetRawText();
        return json.Length > 90 ? $"({json[..90]}…)" : $"({json})";
    }

    // ===== 工具实现（原独立 MCP 服务器迁移而来） =====

    /// <summary>单次 lines 读取的软上限（防止一次响应返回过多行）。</summary>
    private const int MaxLinesPerRead = 5000;

    private string HistoryDesc => Serial.LineHistoryLimit > 0
        ? $"保留最近 {Serial.LineHistoryLimit} 行"
        : "全部缓存（未限制条数）";

    private string ListPorts()
    {
        var names = SerialPortService.GetPortNames();
        if (names.Count == 0) return "本机未检测到可用串口。";
        var descs = Serial.GetPortDescriptions();
        var sb = new StringBuilder();
        sb.AppendLine($"本机可用串口共 {names.Count} 个：");
        foreach (var n in names)
        {
            sb.Append("- ").Append(n);
            if (descs.TryGetValue(n, out var d) && !string.IsNullOrWhiteSpace(d))
                sb.Append("：").Append(d);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private string OpenPort(JsonElement args)
    {
        var port = McpHttpServer.ArgString(args, "port")
            ?? throw new InvalidOperationException("缺少参数 port（串口名，如 COM3；可先用 list_serial_ports 查询）");
        var baud = McpHttpServer.ArgInt(args, "baud_rate", 115200);
        var dataBits = McpHttpServer.ArgInt(args, "data_bits", 8);
        if (dataBits is not (7 or 8)) dataBits = 8;
        var parity = (McpHttpServer.ArgString(args, "parity") ?? "none").Trim().ToLowerInvariant() switch
        {
            "even" or "偶" or "偶校验" => Parity.Even,
            "odd" or "奇" or "奇校验" => Parity.Odd,
            _ => Parity.None,
        };
        var stopBits = (McpHttpServer.ArgString(args, "stop_bits") ?? "one").Trim().ToLowerInvariant() switch
        {
            "2" or "two" => StopBits.Two,
            "1.5" or "one_point_five" => StopBits.OnePointFive,
            _ => StopBits.One,
        };
        var lineHistory = McpHttpServer.ArgInt(args, "line_history", 0);
        if (lineHistory < 0) lineHistory = 0;
        Serial.Open(port, baud, dataBits, parity, stopBits, lineHistory);
        return $"已打开串口 {port}：{baud} 波特率，{Serial.SettingsText}。行历史{HistoryDesc}。现在可用 send_serial_data 收发数据。";
    }

    private string ClosePort()
    {
        if (!Serial.IsOpen)
        {
            Serial.Close();
            return "串口未打开，无需关闭。";
        }
        var name = Serial.PortName;
        Serial.Close();
        return $"已关闭串口 {name}。";
    }

    private string SendData(JsonElement args)
    {
        var data = McpHttpServer.ArgString(args, "data")
            ?? throw new InvalidOperationException("缺少参数 data（要发送的内容）");
        var format = (McpHttpServer.ArgString(args, "format") ?? "text").Trim().ToLowerInvariant();
        var wait = McpHttpServer.ArgInt(args, "wait_response_ms", 500);
        var newline = McpHttpServer.ArgBool(args, "newline", false);
        byte[] bytes;
        if (format is "hex" or "h")
        {
            bytes = SerialText.HexToBytes(data);
        }
        else
        {
            if (newline
                && !data.EndsWith('\r') && !data.EndsWith('\n')
                && !data.EndsWith("\\r") && !data.EndsWith("\\n"))
            {
                data += "\\r\\n";
            }
            bytes = SerialText.UnescapeText(data);
        }
        if (bytes.Length == 0) throw new InvalidOperationException("发送内容为空");

        var mark = Serial.PendingMark();
        Serial.Write(bytes, SerialDirection.AiToPort);
        if (wait <= 0) return $"已向串口发送 {bytes.Length} 字节。";

        var resp = Serial.CollectResponse(mark, wait);
        if (resp.Length == 0) return $"已向串口发送 {bytes.Length} 字节；等待 {wait}ms 未收到应答。";
        return $"已发送 {bytes.Length} 字节；收到应答 {resp.Length} 字节：\n{SerialText.EscapeText(resp)}\nHEX: {SerialText.ToHex(resp)}";
    }

    private string ReadData(JsonElement args)
    {
        var lines = McpHttpServer.ArgInt(args, "lines", 0);
        if (lines > 0)
        {
            if (lines > MaxLinesPerRead) lines = MaxLinesPerRead;
            return FormatHistory(Serial.GetRecentLines(lines), $"最近 {lines} 行");
        }
        var fromLine = McpHttpServer.ArgInt(args, "from_line", 0);
        if (fromLine > 0)
        {
            var toLine = McpHttpServer.ArgInt(args, "to_line", 0);
            if (toLine < fromLine) toLine = fromLine;
            if (toLine - fromLine + 1 > MaxLinesPerRead) toLine = fromLine + MaxLinesPerRead - 1;
            return FormatHistory(Serial.GetLines(fromLine, toLine), $"第 {fromLine}-{toLine} 行");
        }

        var timeout = McpHttpServer.ArgInt(args, "timeout_ms", 0);
        var data = Serial.ReadData(timeout);
        if (data.Length == 0)
            return timeout > 0 ? $"等待 {timeout}ms 未收到串口数据。" : "串口接收缓冲区当前没有数据。";
        return $"读取到 {data.Length} 字节：\n{SerialText.EscapeText(data)}\nHEX: {SerialText.ToHex(data)}";
    }

    private string ClearBuffer()
    {
        var n = Serial.PendingCount;
        Serial.ClearBuffer();
        return $"已清空串口接收缓冲（丢弃未读 {n} 字节；行历史保留不受影响）。";
    }

    private string GetStatus()
    {
        var sb = new StringBuilder();
        if (Serial.IsOpen)
        {
            sb.AppendLine($"串口：已打开 {Serial.PortName} @ {Serial.BaudRate}（{Serial.SettingsText}）");
            sb.AppendLine($"未读缓冲：{Serial.PendingCount} 字节");
            sb.AppendLine($"接收行号：累计 {Serial.LineCount} 行（行历史{HistoryDesc}，可 read_serial_data(lines/from_line) 按行号读取）");
        }
        else
        {
            sb.AppendLine("串口：未打开");
        }
        sb.AppendLine($"累计收发：已发送 {Serial.TotalSent} 字节 / 已接收 {Serial.TotalReceived} 字节");
        return sb.ToString().TrimEnd();
    }

    private string FormatHistory((long No, byte[] Data)[] hist, string what)
    {
        if (hist.Length == 0)
            return $"行历史中没有满足条件的数据（{what}）。串口打开后收到的每一行都带行号缓存（{HistoryDesc}），当前累计 {Serial.LineCount} 行（最早保留到第 {Serial.FirstLineNo} 行）。";
        var sb = new StringBuilder();
        sb.Append($"串口返回 {what}，共 {hist.Length} 行（当前累计 {Serial.LineCount} 行，{HistoryDesc}）：");
        foreach (var (no, line) in hist)
            sb.Append('\n').Append("行").Append(no).Append(": ").Append(SerialText.EscapeText(line));
        return sb.ToString();
    }
}
