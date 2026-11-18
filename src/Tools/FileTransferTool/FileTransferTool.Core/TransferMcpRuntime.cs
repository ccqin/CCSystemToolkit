using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Toolkit.Shell.Mcp;

namespace FileTransferTool.Core;

/// <summary>
/// 文件传输 MCP 运行时（进程级单例）：实现 <see cref="IMcpServiceDefinition"/> 作为「文件传输能力」
/// 注册到 McpServiceRegistry——由聚合网关统一对外，本类不再监听端口。
/// </summary>
public sealed class TransferMcpRuntime : IMcpServiceDefinition
{
    private const int MaxHistory = 1000;
    private readonly object _historyLock = new();
    private readonly List<TransferLogItem> _history = [];
    private bool _enabled = true;
    private long _calls;

    private TransferMcpRuntime() { }

    public static TransferMcpRuntime Instance { get; } = new();

    /// <summary>每次传输/提示触发一条（UI 线程外，订阅方自行调度）。</summary>
    public event Action<TransferLogItem>? Traffic;

    public TransferLogItem[] Snapshot()
    {
        lock (_historyLock) return [.. _history];
    }

    public void ClearHistory()
    {
        lock (_historyLock) _history.Clear();
    }

    private void Record(TransferLogItem item)
    {
        lock (_historyLock)
        {
            _history.Add(item);
            while (_history.Count > MaxHistory) _history.RemoveAt(0);
        }
        Traffic?.Invoke(item);
    }

    public void RaiseInfo(string message) =>
        Record(new TransferLogItem { Kind = TransferLogKind.Info, Message = message });

    public void RaiseAiCall(string message) =>
        Record(new TransferLogItem { Kind = TransferLogKind.AiCall, Message = message });

    public void RaiseError(string message) =>
        Record(new TransferLogItem { Kind = TransferLogKind.Error, Message = message });

    public void RaiseDone(string message) =>
        Record(new TransferLogItem { Kind = TransferLogKind.Done, Message = message });

    /// <summary>执行一次传输并写监控日志（UI 手动与 MCP 调用共用）。</summary>
    public TransferResult ExecuteTransfer(TransferSite site, TransferDirection dir, string localPath, string remotePath, string source)
    {
        var dirText = dir == TransferDirection.Upload ? "上传" : "下载";
        try
        {
            var result = TransferServices.Transfer(site, dir, localPath, remotePath);
            var speed = result.ElapsedMs > 1 ? $"（{result.Bytes / 1024.0 / 1024.0 / (result.ElapsedMs / 1000.0):F2} MB/s）" : "";
            RaiseDone($"✅ {site.ProtocolText} {dirText} {site.Name}（{site.Host}:{site.EffectivePort}）"
                + $" {Path.GetFileName(localPath)} ↔ {remotePath} · {FormatBytes(result.Bytes)} · {result.ElapsedMs:F0}ms {speed}");
            return result;
        }
        catch (Exception ex)
        {
            RaiseError($"❌ {site.ProtocolText} {dirText} {site.Name} · {localPath} ↔ {remotePath} 失败：{ex.Message}");
            throw;
        }
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1 << 20 => $"{bytes / 1024.0 / 1024.0:F2}MB",
        >= 1 << 10 => $"{bytes / 1024.0:F1}KB",
        _ => $"{bytes}B",
    };

    // ===== IMcpServiceDefinition（能力） =====

    public string Id => "transfer";
    public string Name => "文件传输 MCP";
    public string Description => "AI 客户端经 MCP 协议做 FTP/SFTP/SCP 文件上传下载（经聚合网关对外）";

    public bool Enabled => _enabled;

    public long TotalCalls => _calls;

    public event Action? StateChanged;

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        RaiseInfo(enabled ? "文件传输能力已启用（工具进入网关清单）" : "文件传输能力已停用（工具从网关清单移除）");
        StateChanged?.Invoke();
    }

    public JsonArray BuildTools()
    {
        return
        [
            new JsonObject
            {
                ["name"] = "list_transfer_sites",
                ["description"] = "列出已保存的传输站点（名称/协议/主机:端口/用户名，不含密码）。",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(),
                },
            },
            new JsonObject
            {
                ["name"] = "transfer_file",
                ["description"] = "按站点名上传/下载文件。上传时 SFTP 自动逐级创建远程目录；direction: upload=本地→远程，download=远程→本地。",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["site"] = new JsonObject { ["type"] = "string", ["description"] = "站点名（先用 list_transfer_sites 查询）" },
                        ["direction"] = new JsonObject
                        {
                            ["type"] = "string", ["enum"] = new JsonArray("upload", "download"), ["default"] = "upload",
                            ["description"] = "传输方向",
                        },
                        ["local_path"] = new JsonObject { ["type"] = "string", ["description"] = "本地文件绝对路径" },
                        ["remote_path"] = new JsonObject { ["type"] = "string", ["description"] = "远程路径" },
                    },
                    ["required"] = new JsonArray("site", "local_path", "remote_path"),
                },
            },
            new JsonObject
            {
                ["name"] = "transfer_file_direct",
                ["description"] = "不依赖站点直接传输（连接参数随调用给出）。protocol: ftp/sftp/scp；sftp/scp 用 password 或 key_path 认证。",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["protocol"] = new JsonObject
                        {
                            ["type"] = "string", ["enum"] = new JsonArray("ftp", "sftp", "scp"),
                            ["description"] = "传输协议",
                        },
                        ["host"] = new JsonObject { ["type"] = "string", ["description"] = "主机地址" },
                        ["username"] = new JsonObject { ["type"] = "string", ["description"] = "用户名" },
                        ["password"] = new JsonObject { ["type"] = "string", ["description"] = "密码（与 key_path 二选一）" },
                        ["key_path"] = new JsonObject { ["type"] = "string", ["description"] = "私钥文件路径（sftp/scp；优先于密码）" },
                        ["port"] = new JsonObject { ["type"] = "integer", ["default"] = 0, ["description"] = "端口（0=协议默认：FTP 21 / SSH 22）" },
                        ["passive"] = new JsonObject { ["type"] = "boolean", ["default"] = true, ["description"] = "FTP 被动模式" },
                        ["direction"] = new JsonObject
                        {
                            ["type"] = "string", ["enum"] = new JsonArray("upload", "download"), ["default"] = "upload",
                            ["description"] = "传输方向",
                        },
                        ["local_path"] = new JsonObject { ["type"] = "string", ["description"] = "本地文件绝对路径" },
                        ["remote_path"] = new JsonObject { ["type"] = "string", ["description"] = "远程路径" },
                    },
                    ["required"] = new JsonArray("protocol", "host", "username", "local_path", "remote_path"),
                },
            },
            new JsonObject
            {
                ["name"] = "list_remote_dir",
                ["description"] = "列出站点远程目录的文件与子目录（FTP/SFTP；SCP 协议不支持列目录）。",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["site"] = new JsonObject { ["type"] = "string", ["description"] = "站点名" },
                        ["path"] = new JsonObject { ["type"] = "string", ["default"] = "", ["description"] = "远程目录" },
                    },
                    ["required"] = new JsonArray("site"),
                },
            },
            new JsonObject
            {
                ["name"] = "get_transfer_status",
                ["description"] = "查询文件传输服务当前状态（站点数、本次运行传输次数）。",
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
            "list_transfer_sites" => ListSites(),
            "transfer_file" => TransferBySite(args),
            "transfer_file_direct" => TransferDirect(args),
            "list_remote_dir" => ListRemoteDir(args),
            "get_transfer_status" => GetStatus(),
            _ => throw new InvalidOperationException($"未知工具：{name}"),
        };
        _calls++;
        return result;
    }

    public void OnToolCalled(string name, JsonElement args, string result, bool isError)
    {
        var brief = result.Length > 120 ? result[..120] + "…" : result;
        RaiseAiCall($"AI 调用 {name}{SummarizeArgs(args)} → {(isError ? brief : "完成")}");
    }

    private static string SummarizeArgs(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object) return "()";
        var json = args.GetRawText();
        // 密码等敏感参数不进监控日志
        if (args.TryGetProperty("password", out _))
        {
            var obj = JsonObject.Parse(json)!.AsObject();
            obj["password"] = "***";
            json = obj.ToJsonString();
        }
        return json.Length > 160 ? $"({json[..160]}…)" : $"({json})";
    }

    // ===== 工具实现 =====

    private string ListSites()
    {
        var sites = TransferSiteStore.Sites;
        if (sites.Count == 0)
            return "还没有保存任何站点。用 transfer_file_direct 直接传连接参数，或让用户在工具集「文件传输」界面添加站点。";
        var sb = new StringBuilder();
        sb.AppendLine($"共 {sites.Count} 个站点：");
        foreach (var s in sites)
            sb.Append("- ").Append(s.Name).Append($" [{s.ProtocolText}] ").Append(s.Username)
              .Append('@').Append(s.Host).Append(':').Append(s.EffectivePort)
              .AppendLine(string.IsNullOrWhiteSpace(s.KeyPath) ? "" : $"（私钥 {s.KeyPath}）");
        return sb.ToString().TrimEnd();
    }

    private string TransferBySite(JsonElement args)
    {
        var siteName = McpHttpServer.ArgString(args, "site")
            ?? throw new InvalidOperationException("缺少参数 site（站点名，可先用 list_transfer_sites 查询）");
        var site = TransferSiteStore.Find(siteName)
            ?? throw new InvalidOperationException($"站点「{siteName}」不存在（可先用 list_transfer_sites 查询，或用 transfer_file_direct 直接传）");
        var dir = ParseDirection(McpHttpServer.ArgString(args, "direction"));
        var local = McpHttpServer.ArgString(args, "local_path") ?? throw new InvalidOperationException("缺少参数 local_path（本地文件绝对路径）");
        var remote = McpHttpServer.ArgString(args, "remote_path") ?? throw new InvalidOperationException("缺少参数 remote_path（远程路径）");
        if (dir == TransferDirection.Upload && !File.Exists(local))
            throw new InvalidOperationException($"本地文件不存在：{local}");
        var result = ExecuteTransfer(site, dir, local, remote, "AI");
        return $"✅ {site.ProtocolText} {(dir == TransferDirection.Upload ? "上传" : "下载")}完成：{local} ↔ {remote}，"
            + $"{FormatBytes(result.Bytes)}，耗时 {result.ElapsedMs:F0}ms。";
    }

    private string TransferDirect(JsonElement args)
    {
        var protocol = (McpHttpServer.ArgString(args, "protocol") ?? "").Trim().ToLowerInvariant() switch
        {
            "ftp" => TransferProtocol.Ftp,
            "sftp" => TransferProtocol.Sftp,
            "scp" => TransferProtocol.Scp,
            _ => throw new InvalidOperationException("参数 protocol 必须是 ftp/sftp/scp"),
        };
        var host = McpHttpServer.ArgString(args, "host") ?? throw new InvalidOperationException("缺少参数 host");
        var user = McpHttpServer.ArgString(args, "username") ?? throw new InvalidOperationException("缺少参数 username");
        var site = new TransferSite
        {
            Name = $"{protocol}://{user}@{host}",
            Protocol = protocol,
            Host = host,
            Port = McpHttpServer.ArgInt(args, "port", 0),
            Username = user,
            Password = McpHttpServer.ArgString(args, "password") ?? "",
            KeyPath = McpHttpServer.ArgString(args, "key_path"),
            Passive = McpHttpServer.ArgBool(args, "passive", true),
        };
        var dir = ParseDirection(McpHttpServer.ArgString(args, "direction"));
        var local = McpHttpServer.ArgString(args, "local_path") ?? throw new InvalidOperationException("缺少参数 local_path");
        var remote = McpHttpServer.ArgString(args, "remote_path") ?? throw new InvalidOperationException("缺少参数 remote_path");
        if (dir == TransferDirection.Upload && !File.Exists(local))
            throw new InvalidOperationException($"本地文件不存在：{local}");
        var result = ExecuteTransfer(site, dir, local, remote, "AI");
        return $"✅ {site.ProtocolText} {(dir == TransferDirection.Upload ? "上传" : "下载")}完成：{local} ↔ {remote}，"
            + $"{FormatBytes(result.Bytes)}，耗时 {result.ElapsedMs:F0}ms。";
    }

    private string ListRemoteDir(JsonElement args)
    {
        var siteName = McpHttpServer.ArgString(args, "site")
            ?? throw new InvalidOperationException("缺少参数 site（站点名）");
        var site = TransferSiteStore.Find(siteName)
            ?? throw new InvalidOperationException($"站点「{siteName}」不存在");
        var path = McpHttpServer.ArgString(args, "path") ?? "";
        var items = TransferServices.ListRemote(site, path);
        if (items.Count == 0) return $"目录 {path} 为空。";
        var sb = new StringBuilder();
        sb.AppendLine($"目录 {path} 共 {items.Count} 项：");
        foreach (var (name, size, isDir) in items)
            sb.Append("- ").Append(isDir ? "[目录] " : "").Append(name)
              .AppendLine(isDir ? "" : $"（{FormatBytes(size)}）");
        return sb.ToString().TrimEnd();
    }

    private string GetStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"站点：{TransferSiteStore.Sites.Count} 个（list_transfer_sites 查看详情）");
        sb.AppendLine($"本次运行工具调用：{TotalCalls} 次");
        sb.Append("说明：FTP 走 FluentFTP（被动模式默认开），SFTP/SCP 走 SSH.NET（密码或私钥认证）；每次传输实时显示在工具集传输记录中。");
        return sb.ToString();
    }

    private static TransferDirection ParseDirection(string? s) =>
        s is { Length: > 0 } && s.Trim().ToLowerInvariant() is "download" or "下载" or "dl"
            ? TransferDirection.Download
            : TransferDirection.Upload;
}
