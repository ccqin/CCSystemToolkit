using System.IO.Ports;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Toolkit.Shell.Mcp;

namespace SerialMcpTool.Core;

/// <summary>
/// 串口 MCP 服务器：streamable HTTP 传输（POST JSON-RPC 2.0 到 /mcp），仅监听本机回环。
/// 接入需令牌：URL 以 ?token= 携带，由宿主「MCP 服务」设置统一管理（McpAccessStore）。
/// 所有工具调用与收发内容都通过 <see cref="SerialPortService"/> 的 Traffic 事件进卡片监控列表。
/// </summary>
public sealed class McpServer
{
    private readonly SerialPortService _serial;
    private readonly Action? _onToolCall;
    private readonly object _toolLock = new();
    private HttpListener _listener = new();
    private Thread? _thread;

    public McpServer(SerialPortService serial, int port, Action? onToolCall = null)
    {
        _serial = serial;
        _onToolCall = onToolCall;
        Port = port;
    }

    /// <summary>监听端口。</summary>
    public int Port { get; }

    public bool IsRunning { get; private set; }

    /// <summary>对外通告的主机名（正常注册 127.0.0.1 前缀时为 IP；仅 localhost 前缀可用时为 localhost）。</summary>
    public string AdvertisedHost { get; private set; } = "127.0.0.1";

    /// <summary>不含令牌的服务端点 URL（完整接入 URL 在宿主设置中按令牌拼接）。</summary>
    public string EndpointUrl => $"http://{AdvertisedHost}:{Port}/mcp";

    public void Start()
    {
        if (IsRunning) return;
        var listener = new HttpListener();
        try
        {
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            listener.Prefixes.Add($"http://localhost:{Port}/");
            listener.Start();
        }
        catch (HttpListenerException)
        {
            // 回退：部分环境 127.0.0.1 前缀需 URL ACL 保留，localhost 前缀始终可用
            listener.Close();
            listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{Port}/");
            try
            {
                listener.Start();
                AdvertisedHost = "localhost";
            }
            catch (Exception ex)
            {
                listener.Close();
                throw new InvalidOperationException($"MCP 服务器启动失败（端口 {Port}）：{ex.Message}——端口可能已被占用", ex);
            }
        }
        catch (Exception ex)
        {
            listener.Close();
            throw new InvalidOperationException($"MCP 服务器启动失败（端口 {Port}）：{ex.Message}", ex);
        }
        _listener = listener;
        IsRunning = true;
        _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "SerialMcpHttp" };
        _thread.Start();
    }

    public void Stop()
    {
        IsRunning = false;
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }

    private void AcceptLoop()
    {
        while (IsRunning)
        {
            HttpListenerContext ctx;
            try { ctx = _listener.GetContext(); }
            catch
            {
                if (IsRunning) continue;
                break;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Handle(ctx); }
                catch
                { /* 单请求异常不影响服务器 */ }
            });
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var res = ctx.Response;
            try
            {
                // 浏览器防 drive-by：Origin 非本机一律拒绝
                var origin = req.Headers["Origin"];
                if (!string.IsNullOrEmpty(origin) && !IsLocalOrigin(origin))
                {
                    res.StatusCode = 403;
                    Close(res);
                    return;
                }

                // 接入令牌校验（宿主「MCP 服务」设置里管理）
                var token = req.QueryString["token"];
                if (string.IsNullOrEmpty(token) || !McpAccessStore.Validate(token))
                {
                    WriteJson(res, 401, new JsonObject
                    {
                        ["error"] = "unauthorized",
                        ["message"] = "接入令牌缺失或已失效：请在「系统管理工具集」顶栏「MCP 服务」中复制带令牌的接入 URL",
                    }.ToJsonString());
                    return;
                }

                if (req.HttpMethod != "POST")
                {
                    // GET（服务端推送流）/ DELETE（会话终止）不提供：无会话、无推送
                    res.StatusCode = 405;
                    Close(res);
                    return;
                }

                string body;
                using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();
                var (status, json) = Dispatch(body);
                if (status == 202)
                {
                    res.StatusCode = 202;   // 通知：无响应体
                    Close(res);
                    return;
                }
                WriteJson(res, status, json);
            }
            catch (Exception ex)
            {
                try { WriteJson(res, 500, MakeError(null, -32603, "Internal error: " + ex.Message)); }
                catch { }
                _serial.RaiseError("MCP 请求处理异常：" + ex.Message);
            }
        }
        catch { }
    }

    private static void Close(HttpListenerResponse res)
    {
        try { res.ContentLength64 = 0; res.Close(); } catch { }
    }

    private static void WriteJson(HttpListenerResponse res, int status, string json)
    {
        res.StatusCode = status;
        res.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.OutputStream.Close();
    }

    private static bool IsLocalOrigin(string origin)
    {
        try
        {
            var host = new Uri(origin).Host;
            return host is "localhost" or "127.0.0.1" or "::1";
        }
        catch { return false; }
    }

    // ===== JSON-RPC 2.0 分发 =====

    private (int status, string json) Dispatch(string body)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch { return (200, MakeError(null, -32700, "Parse error")); }
        using var _ = doc;
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return (200, MakeError(null, -32600, "Invalid Request"));
        if (!root.TryGetProperty("method", out var m) || m.ValueKind != JsonValueKind.String)
            return (200, MakeError(null, -32600, "Invalid Request"));
        var method = m.GetString()!;
        JsonElement? id = null;
        if (root.TryGetProperty("id", out var idEl)
            && idEl.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            id = idEl;

        switch (method)
        {
            case "initialize":
                return (200, MakeResult(id, BuildInitialize(root)));
            case "ping":
                return (200, MakeResult(id, new JsonObject()));
            case "tools/list":
                return (200, MakeResult(id, new JsonObject { ["tools"] = BuildTools() }));
            case "tools/call":
                return (200, HandleCall(root, id));
        }
        if (method.StartsWith("notifications/", StringComparison.Ordinal))
            return (202, "");   // 通知无响应体
        return (200, MakeError(id, -32601, $"Method not found: {method}"));
    }

    private string HandleCall(JsonElement root, JsonElement? id)
    {
        string? name = null;
        var args = default(JsonElement);
        if (root.TryGetProperty("params", out var ps) && ps.ValueKind == JsonValueKind.Object)
        {
            if (ps.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                name = n.GetString();
            if (ps.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object)
                args = a;
        }
        if (string.IsNullOrEmpty(name))
            return MakeError(id, -32602, "Invalid params: missing tool name");

        string text;
        bool isError;
        lock (_toolLock)
        {
            _serial.RaiseInfo($"AI 调用 {name}{SummarizeArgs(args)}");
            try
            {
                text = ExecuteTool(name!, args);
                isError = false;
                _onToolCall?.Invoke();
            }
            catch (Exception ex)
            {
                text = "错误：" + ex.Message;
                isError = true;
                _serial.RaiseError($"AI 调用 {name} 失败：{ex.Message}");
            }
        }
        return MakeResult(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = isError,
        });
    }

    private static string SummarizeArgs(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object) return "()";
        var json = args.GetRawText();
        return json.Length > 90 ? $"({json[..90]}…)" : $"({json})";
    }

    private static string MakeResult(JsonElement? id, JsonNode result) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id is { } e ? JsonNode.Parse(e.GetRawText()) : null,
            ["result"] = result,
        }.ToJsonString();

    private static string MakeError(JsonElement? id, int code, string message) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id is { } e ? JsonNode.Parse(e.GetRawText()) : null,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        }.ToJsonString();

    private static JsonObject BuildInitialize(JsonElement root)
    {
        var protocol = "2025-06-18";
        if (root.TryGetProperty("params", out var ps)
            && ps.ValueKind == JsonValueKind.Object
            && ps.TryGetProperty("protocolVersion", out var pv)
            && pv.ValueKind == JsonValueKind.String
            && pv.GetString() is { Length: > 0 } v
            && v is "2024-11-05" or "2025-03-26" or "2025-06-18")
        {
            protocol = v;
        }
        return new JsonObject
        {
            ["protocolVersion"] = protocol,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = "ccsystemtoolkit-serial-mcp", ["version"] = "1.0.0" },
            ["instructions"] = "串口 MCP 服务。典型流程：list_serial_ports 查询串口 → open_serial_port 打开（默认 115200 8N1）→ "
                + "send_serial_data 收发（wait_response_ms 控制应答等待，默认 500ms）→ read_serial_data 读取后续数据 → close_serial_port 关闭。"
                + "文本发送支持 \\r \\n \\t \\xNN 转义；二进制用 format=hex。所有收发内容也会实时显示在工具集的串口卡片中。",
        };
    }

    private static JsonArray BuildTools() =>
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
            ["description"] = "向串口发送数据，并默认等待串口应答（wait_response_ms，默认 500，0 表示不等待）。文本格式支持 \\r \\n \\t \\xNN 转义；发送二进制用 format=hex。",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["data"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "要发送的内容：format=text 时为文本（如 \"AT\\r\\n\"）；format=hex 时为十六进制（如 \"41 0D 0A\"）",
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
                },
                ["required"] = new JsonArray("data"),
            },
        },
        new JsonObject
        {
            ["name"] = "read_serial_data",
            ["description"] = "读取串口返回的数据。timeout_ms 为 0 立即返回缓冲区数据（可能为空）；大于 0 表示最多等待新数据的毫秒数。",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["timeout_ms"] = new JsonObject
                    {
                        ["type"] = "integer", ["default"] = 0,
                        ["description"] = "等待新数据的毫秒数（0 = 立即返回缓冲数据）",
                    },
                },
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

    // ===== 工具实现 =====

    private string ExecuteTool(string name, JsonElement args)
    {
        switch (name)
        {
            case "list_serial_ports": return ListPorts();
            case "open_serial_port": return OpenPort(args);
            case "close_serial_port": return ClosePort();
            case "send_serial_data": return SendData(args);
            case "read_serial_data": return ReadData(args);
            case "get_serial_status": return GetStatus();
            default: throw new InvalidOperationException($"未知工具：{name}");
        }
    }

    private string ListPorts()
    {
        var names = SerialPortService.GetPortNames();
        if (names.Count == 0) return "本机未检测到可用串口。";
        var descs = _serial.GetPortDescriptions();
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
        var port = ArgString(args, "port")
            ?? throw new InvalidOperationException("缺少参数 port（串口名，如 COM3；可先用 list_serial_ports 查询）");
        var baud = ArgInt(args, "baud_rate", 115200);
        var dataBits = ArgInt(args, "data_bits", 8);
        if (dataBits is not (7 or 8)) dataBits = 8;
        var parity = (ArgString(args, "parity") ?? "none").Trim().ToLowerInvariant() switch
        {
            "even" or "偶" or "偶校验" => Parity.Even,
            "odd" or "奇" or "奇校验" => Parity.Odd,
            _ => Parity.None,
        };
        var stopBits = (ArgString(args, "stop_bits") ?? "one").Trim().ToLowerInvariant() switch
        {
            "2" or "two" => StopBits.Two,
            "1.5" or "one_point_five" => StopBits.OnePointFive,
            _ => StopBits.One,
        };
        _serial.Open(port, baud, dataBits, parity, stopBits);
        return $"已打开串口 {port}：{baud} 波特率，{_serial.SettingsText}。现在可用 send_serial_data 收发数据。";
    }

    private string ClosePort()
    {
        if (!_serial.IsOpen)
        {
            _serial.Close();
            return "串口未打开，无需关闭。";
        }
        var name = _serial.PortName;
        _serial.Close();
        return $"已关闭串口 {name}。";
    }

    private string SendData(JsonElement args)
    {
        var data = ArgString(args, "data")
            ?? throw new InvalidOperationException("缺少参数 data（要发送的内容）");
        var format = (ArgString(args, "format") ?? "text").Trim().ToLowerInvariant();
        var wait = ArgInt(args, "wait_response_ms", 500);
        var bytes = format is "hex" or "h" ? SerialText.HexToBytes(data) : SerialText.UnescapeText(data);
        if (bytes.Length == 0) throw new InvalidOperationException("发送内容为空");

        var mark = _serial.PendingMark();
        _serial.Write(bytes, SerialDirection.AiToPort);
        if (wait <= 0) return $"已向串口发送 {bytes.Length} 字节。";

        var resp = _serial.CollectResponse(mark, wait);
        if (resp.Length == 0) return $"已向串口发送 {bytes.Length} 字节；等待 {wait}ms 未收到应答。";
        return $"已发送 {bytes.Length} 字节；收到应答 {resp.Length} 字节：\n{SerialText.EscapeText(resp)}\nHEX: {SerialText.ToHex(resp)}";
    }

    private string ReadData(JsonElement args)
    {
        var timeout = ArgInt(args, "timeout_ms", 0);
        var data = _serial.ReadData(timeout);
        if (data.Length == 0)
            return timeout > 0 ? $"等待 {timeout}ms 未收到串口数据。" : "串口接收缓冲区当前没有数据。";
        return $"读取到 {data.Length} 字节：\n{SerialText.EscapeText(data)}\nHEX: {SerialText.ToHex(data)}";
    }

    private string GetStatus()
    {
        var sb = new StringBuilder();
        if (_serial.IsOpen)
        {
            sb.AppendLine($"串口：已打开 {_serial.PortName} @ {_serial.BaudRate}（{_serial.SettingsText}）");
            sb.AppendLine($"未读缓冲：{_serial.PendingCount} 字节");
        }
        else
        {
            sb.AppendLine("串口：未打开");
        }
        sb.AppendLine($"累计收发：已发送 {_serial.TotalSent} 字节 / 已接收 {_serial.TotalReceived} 字节");
        sb.Append($"MCP 服务：运行中 {EndpointUrl}");
        return sb.ToString();
    }

    private static string? ArgString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static int ArgInt(JsonElement args, string name, int fallback)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var n) ? n : fallback,
            JsonValueKind.String when int.TryParse(v.GetString(), out var n) => n,
            _ => fallback,
        };
    }
}
