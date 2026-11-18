using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Toolkit.Shell.Mcp;

/// <summary>
/// MCP over streamable HTTP 服务器的通用骨架（HttpListener + JSON-RPC 2.0，仅监听本机回环）。
/// 子类只需提供工具清单与执行逻辑；接入令牌校验（McpAccessStore）、浏览器 Origin 校验、
/// 协议分发（initialize/tools/list/tools/call/ping/通知 202）与响应组装都在基类。
/// </summary>
public abstract class McpHttpServer : IDisposable
{
    private readonly object _toolLock = new();
    private HttpListener _listener = new();
    private Thread? _thread;
    private long _calls;

    /// <summary>监听端口。</summary>
    public int Port { get; }

    public bool IsRunning { get; private set; }

    /// <summary>对外通告的主机名（127.0.0.1 前缀不可用时回退 localhost）。</summary>
    public string AdvertisedHost { get; private set; } = "127.0.0.1";

    public string EndpointPath => "/mcp";

    /// <summary>不含令牌的服务端点 URL（完整接入 URL 由宿主设置按令牌拼接）。</summary>
    public string EndpointUrl => $"http://{AdvertisedHost}:{Port}{EndpointPath}";

    /// <summary>本次运行期间工具调用累计次数。</summary>
    public long TotalCalls => _calls;

    /// <summary>服务器级异常上报（运行时转监控日志）。</summary>
    public event Action<string>? ServerError;

    protected McpHttpServer(int port) => Port = port;

    // ===== 子类实现 =====

    /// <summary>serverInfo.name。</summary>
    protected abstract string ServerName { get; }

    /// <summary>serverInfo.version。</summary>
    protected abstract string ServerVersion { get; }

    /// <summary>initialize 返回的整体使用说明（AI 客户端会放进上下文）。</summary>
    protected abstract string Instructions { get; }

    /// <summary>tools/list 的工具定义（name/description/inputSchema）。</summary>
    protected abstract JsonArray BuildTools();

    /// <summary>执行一个工具（抛异常 = 工具级错误，转 isError 返回）。</summary>
    protected abstract string ExecuteTool(string name, JsonElement args);

    /// <summary>工具执行前的记录钩子（如写入监控日志"AI 调用 xxx"）。</summary>
    protected virtual void OnToolCalled(string name, JsonElement args, string result, bool isError) { }

    // ===== 生命周期 =====

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
        _thread = new Thread(AcceptLoop) { IsBackground = true, Name = $"McpHttp-{Port}" };
        _thread.Start();
    }

    public void Stop()
    {
        IsRunning = false;
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }

    public void Dispose() => Stop();

    // ===== HTTP / JSON-RPC =====

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
                catch { /* 单请求异常不影响服务器 */ }
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
                ServerError?.Invoke("MCP 请求处理异常：" + ex.Message);
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
            try
            {
                text = ExecuteTool(name!, args);
                isError = false;
                _calls++;
            }
            catch (Exception ex)
            {
                text = "错误：" + ex.Message;
                isError = true;
            }
            OnToolCalled(name!, args, text, isError);
        }
        return MakeResult(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = isError,
        });
    }

    private JsonObject BuildInitialize(JsonElement root)
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
            ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = ServerVersion },
            ["instructions"] = Instructions,
        };
    }

    // ===== 响应组装与参数读取辅助（子类可用） =====

    public static string MakeResult(JsonElement? id, JsonNode result) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id is { } e ? JsonNode.Parse(e.GetRawText()) : null,
            ["result"] = result,
        }.ToJsonString();

    public static string MakeError(JsonElement? id, int code, string message) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id is { } e ? JsonNode.Parse(e.GetRawText()) : null,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        }.ToJsonString();

    public static string? ArgString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    public static int ArgInt(JsonElement args, string name, int fallback)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var n) ? n : fallback,
            JsonValueKind.String when int.TryParse(v.GetString(), out var n) => n,
            _ => fallback,
        };
    }

    public static bool ArgBool(JsonElement args, string name, bool fallback)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(v.GetString(), out var b) => b,
            _ => fallback,
        };
    }
}
