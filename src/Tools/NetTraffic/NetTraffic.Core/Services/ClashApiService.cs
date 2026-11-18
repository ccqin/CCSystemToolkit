using System.Net.Http;
using System.Text.Json;

namespace NetTraffic.Core.Services;

/// <summary>一条 Clash（FlClash/Clash 内核）活跃连接：谁（进程）→ 哪里（目标）→ 走哪个节点、多少流量。</summary>
public sealed class ClashConnection
{
    public string Id { get; init; } = "";
    /// <summary>发起连接的本机进程（谁在走代理——关键归因）。</summary>
    public string Process { get; init; } = "";
    public string ProcessPath { get; init; } = "";
    /// <summary>目标域名（无 SNI 时为空，用 Destination）。</summary>
    public string Host { get; init; } = "";
    /// <summary>目标 地址:端口。</summary>
    public string Destination { get; init; } = "";
    public string Network { get; init; } = "";
    /// <summary>命中的分流规则。</summary>
    public string Rule { get; init; } = "";
    /// <summary>出站链（经过的代理节点，最前面为实际出口）。</summary>
    public string Chain { get; init; } = "";
    public long Download { get; init; }
    public long Upload { get; init; }
    public DateTime Start { get; init; }
}

/// <summary>代理连接分析（按进程聚合）。</summary>
public sealed class ClashProcessAgg
{
    public string Process { get; init; } = "";
    public int Connections { get; init; }
    public long Download { get; init; }
    public long Upload { get; init; }
}

/// <summary>
/// Clash 内核 RESTful API（external-controller，FlClash 默认 127.0.0.1:9090，可能在设置里改过/有 secret）。
/// GET /connections 返回全部活跃连接的进程/目标/规则/节点/流量——回答"FlClashCore 的流量究竟是谁发起的"。
/// </summary>
public static class ClashApiService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public sealed class ClashResult
    {
        public List<ClashConnection> Connections { get; init; } = [];
        public List<ClashProcessAgg> ByProcess { get; init; } = [];
        /// <summary>内核自启动以来的累计总流量。</summary>
        public long TotalDownload { get; init; }
        public long TotalUpload { get; init; }
    }

    /// <summary>查询活跃连接并按进程聚合（baseUri 形如 http://127.0.0.1:9090；secret 可为空）。</summary>
    public static async Task<ClashResult> GetConnectionsAsync(string baseUri, string secret)
    {
        var url = baseUri.TrimEnd('/') + "/connections";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(secret))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret.Trim());
        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var conns = new List<ClashConnection>();
        if (root.TryGetProperty("connections", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in list.EnumerateArray())
            {
                var meta = c.TryGetProperty("metadata", out var m) ? m : default;
                string Str(string k) => meta.ValueKind == JsonValueKind.Object
                    && meta.TryGetProperty(k, out var v)
                    && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var host = Str("host");
                var dst = Str("destinationIP");
                var dstPort = Str("destinationPort");
                var dest = host.Length > 0 ? $"{host}:{dstPort}" : $"{dst}:{dstPort}".TrimEnd(':');
                conns.Add(new ClashConnection
                {
                    Id = GetString(c, "id"),
                    Process = Str("process"),
                    ProcessPath = Str("processPath"),
                    Host = host,
                    Destination = dest,
                    Network = Str("network"),
                    Rule = GetString(c, "rule"),
                    Chain = c.TryGetProperty("chains", out var chains) && chains.ValueKind == JsonValueKind.Array
                        ? string.Join(" → ", chains.EnumerateArray().Select(x => x.GetString() ?? ""))
                        : "",
                    Download = GetLong(c, "download"),
                    Upload = GetLong(c, "upload"),
                    Start = DateTime.TryParse(GetString(c, "start"), out var st) ? st : DateTime.Now,
                });
            }
        }

        var byProcess = conns
            .GroupBy(c => c.Process.Length > 0 ? c.Process : "（未知进程）")
            .Select(g => new ClashProcessAgg
            {
                Process = g.Key,
                Connections = g.Count(),
                Download = g.Sum(c => c.Download),
                Upload = g.Sum(c => c.Upload),
            })
            .OrderByDescending(a => a.Download + a.Upload)
            .ToList();

        return new ClashResult
        {
            Connections = conns.OrderByDescending(c => c.Download + c.Upload).ToList(),
            ByProcess = byProcess,
            TotalDownload = GetLong(root, "download"),
            TotalUpload = GetLong(root, "upload"),
        };

        static string GetString(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        static long GetLong(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
    }
}
