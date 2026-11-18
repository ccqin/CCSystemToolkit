using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTransferTool.Core;
using FileTransferTool.Ui.Views;
using Toolkit.Shell;

namespace FileTransferTool.Ui.ViewModels;

/// <summary>视图 XAML 用的小转换器。</summary>
public static class Converters
{
    /// <summary>集合数量 → 是否为空。</summary>
    public static readonly FuncValueConverter<int, bool> CountIsZero =
        new(c => c == 0);

    /// <summary>字符串非空 → 可见（弹窗错误提示等）。</summary>
    public static readonly FuncValueConverter<string?, bool> StringNotNullOrEmpty =
        new(s => !string.IsNullOrEmpty(s));
}

/// <summary>站点行 VM。</summary>
public partial class SiteRowViewModel : ObservableObject
{
    public required TransferSite Model { get; init; }

    public string Name => Model.Name;
    public string ProtocolText => Model.ProtocolText;
    public string HostText => $"{Model.Host}:{Model.EffectivePort}";
    public string Username => Model.Username;

    /// <summary>认证方式摘要（密码显示 ***，私钥显示文件名）。</summary>
    public string AuthText => !string.IsNullOrWhiteSpace(Model.KeyPath)
        ? Path.GetFileName(Model.KeyPath)
        : string.IsNullOrEmpty(Model.Password) ? "（无）" : "***";

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ProtocolText));
        OnPropertyChanged(nameof(HostText));
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(AuthText));
    }
}

/// <summary>传输监控行 VM。</summary>
public partial class TransferLogItemViewModel : ObservableObject
{
    public required TransferLogItem Model { get; init; }
    public string TimeText => Model.TimeText;
    public string Tag => Model.Tag;

    public string KindText => Model.Kind switch
    {
        TransferLogKind.Done => "传输",
        TransferLogKind.Error => "错误",
        TransferLogKind.AiCall => "AI调用",
        _ => "系统",
    };

    public string Message => Model.Message;
}

/// <summary>远程浏览列表行 VM。</summary>
public partial class BrowseItemViewModel : ObservableObject
{
    public required string Name { get; init; }
    public long Size { get; init; }
    public bool IsDir { get; init; }

    public string TypeText => IsDir ? "📁 目录" : "📄 文件";
    public string SizeText => IsDir ? "" : TransferMcpRuntime.FormatBytes(Size);
}

/// <summary>
/// 文件传输工具主 VM：站点管理（协议是站点属性，添加/编辑时在弹窗选择）+ 手动传输 + 远程浏览 + 传输记录。
/// 运行时由 TransferMcpRuntime 单例承载，切换视图不中断。
/// </summary>
public partial class FileTransferViewModel : ObservableObject
{
    private const int MaxItems = 1000;
    private readonly IToolHost _host;
    private readonly TransferMcpRuntime _rt = TransferMcpRuntime.Instance;
    private bool _detached;

    public static readonly IReadOnlyList<string> Directions = ["上传（本地→远程）", "下载（远程→本地）"];

    /// <summary>宿主窗口（剪贴板/弹窗用，视图注入）。</summary>
    public Window? Owner { get; set; }

    public FileTransferViewModel(IToolHost host)
    {
        _host = host;
        foreach (var item in _rt.Snapshot())
            Items.Add(new TransferLogItemViewModel { Model = item });
        ReloadSites();
        _rt.Traffic += OnTraffic;
        _rt.StateChanged += OnStateChanged;
        Toolkit.Shell.Mcp.McpGatewayRuntime.Instance.StateChanged += OnStateChanged;
        TransferSiteStore.Changed += OnSiteStoreChanged;
        RefreshStatus();
    }

    /// <summary>视图脱离时退订（运行时后台继续）。</summary>
    public void Detach()
    {
        if (_detached) return;
        _detached = true;
        _rt.Traffic -= OnTraffic;
        _rt.StateChanged -= OnStateChanged;
        Toolkit.Shell.Mcp.McpGatewayRuntime.Instance.StateChanged -= OnStateChanged;
        TransferSiteStore.Changed -= OnSiteStoreChanged;
    }

    // ===== 站点（协议是站点属性，一张表统一管理） =====

    public ObservableCollection<SiteRowViewModel> Sites { get; } = [];

    [ObservableProperty] private SiteRowViewModel? _siteSel;

    private void ReloadSites()
    {
        var keep = SiteSel?.Model.Id;
        Sites.Clear();
        foreach (var s in TransferSiteStore.Sites)
            Sites.Add(new SiteRowViewModel { Model = s });
        SiteSel = Sites.FirstOrDefault(s => s.Model.Id == keep) ?? Sites.FirstOrDefault();

        var keepBrowse = BrowseSiteSel?.Model.Id;
        BrowseSites.Clear();
        foreach (var site in TransferSiteStore.Sites)
            BrowseSites.Add(new SiteRowViewModel { Model = site });
        BrowseSiteSel = BrowseSites.FirstOrDefault(s => s.Model.Id == keepBrowse) ?? BrowseSites.FirstOrDefault();

        RefreshStatus();
    }

    private void OnSiteStoreChanged() => Dispatcher.UIThread.Post(ReloadSites);

    [RelayCommand]
    private void RefreshSites() => ReloadSites();

    [RelayCommand]
    private async Task AddSite()
    {
        if (Owner is not { } owner) return;
        if (await SiteEditWindow.PickAsync(owner, null, TransferProtocol.Ftp) is { } site)
        {
            try
            {
                TransferSiteStore.Add(site);
                _host.Log(FileTransferToolModule.ToolTitle, $"✔ 已新增站点 {site.ProtocolText}「{site.Name}」");
            }
            catch (Exception ex)
            {
                _host.Log(FileTransferToolModule.ToolTitle, "✘ " + ex.Message);
            }
        }
    }

    [RelayCommand]
    private async Task EditSite(SiteRowViewModel? row)
    {
        if (row is null || Owner is not { } owner) return;
        if (await SiteEditWindow.PickAsync(owner, row.Model, row.Model.Protocol) is { } site)
        {
            try
            {
                TransferSiteStore.Update(site);
                _host.Log(FileTransferToolModule.ToolTitle, $"✔ 已更新站点「{site.Name}」");
            }
            catch (Exception ex)
            {
                _host.Log(FileTransferToolModule.ToolTitle, "✘ " + ex.Message);
            }
        }
    }

    [RelayCommand]
    private void RemoveSite(SiteRowViewModel? row)
    {
        if (row is null) return;
        TransferSiteStore.Remove(row.Model.Id);
        _host.Log(FileTransferToolModule.ToolTitle, $"已删除站点「{row.Model.Name}」");
    }

    // ===== 手动传输（站点协议决定走 FTP/SFTP/SCP） =====

    [ObservableProperty] private int _dirIndex;
    [ObservableProperty] private string _localPath = "";
    [ObservableProperty] private string _remotePath = "";

    [RelayCommand]
    private void ManualTransfer()
    {
        if (SiteSel is not { } sel)
        {
            _host.Log(FileTransferToolModule.ToolTitle, "△ 请先选择站点");
            return;
        }
        var dir = DirIndex == 1 ? TransferDirection.Download : TransferDirection.Upload;
        if (dir == TransferDirection.Upload && !File.Exists(LocalPath))
        {
            _host.Log(FileTransferToolModule.ToolTitle, "✘ 本地文件不存在：" + LocalPath);
            return;
        }
        try
        {
            var r = _rt.ExecuteTransfer(sel.Model, dir, LocalPath, RemotePath, "手动");
            _host.Log(FileTransferToolModule.ToolTitle,
                $"✔ {sel.Model.ProtocolText} {(dir == TransferDirection.Upload ? "上传" : "下载")}完成：{TransferMcpRuntime.FormatBytes(r.Bytes)} / {r.ElapsedMs:F0}ms");
        }
        catch (Exception ex)
        {
            _host.Log(FileTransferToolModule.ToolTitle, "✘ " + ex.Message);
        }
    }

    // ===== 远程浏览（FTP/SFTP；SCP 协议不支持列目录） =====

    /// <summary>可浏览的站点（SCP 不支持列目录，不进此列表）。</summary>
    public ObservableCollection<SiteRowViewModel> BrowseSites { get; } = [];

    /// <summary>远程目录内容。</summary>
    public ObservableCollection<BrowseItemViewModel> BrowseItems { get; } = [];

    [ObservableProperty] private SiteRowViewModel? _browseSiteSel;
    [ObservableProperty] private string _browsePath = "/";
    [ObservableProperty] private string _browseLocal = "";
    [ObservableProperty] private BrowseItemViewModel? _browseSel;

    /// <summary>浏览页顶部提示（选中 SCP 站点时显示指引；空=正常）。</summary>
    [ObservableProperty] private string _browseHint = "";

    partial void OnBrowseSiteSelChanged(SiteRowViewModel? value)
    {
        BrowseItems.Clear();
        BrowseHint = value?.Model.Protocol == TransferProtocol.Scp
            ? "⚠ SCP 协议不支持列目录（协议限制）。设备的 sshd 若开启了 SFTP 子系统（绝大多数默认开启），编辑该站点把协议改为 SFTP 即可浏览，连接参数不变。"
            : "";
        if (value is not null) BrowsePath = "/";
    }

    [RelayCommand]
    private void GoTo() => LoadBrowse(BrowsePath);

    [RelayCommand]
    private void Up()
    {
        if (string.IsNullOrWhiteSpace(BrowsePath) || BrowsePath == "/") return;
        var parent = BrowsePath.TrimEnd('/');
        var idx = parent.LastIndexOf('/');
        BrowsePath = idx <= 0 ? "/" : parent[..idx];
        LoadBrowse(BrowsePath);
    }

    [RelayCommand]
    private void RefreshBrowse() => LoadBrowse(BrowsePath);

    /// <summary>双击/打开选中的目录（文件不进入）。</summary>
    [RelayCommand]
    public void OpenBrowseItem()
    {
        if (BrowseSel is not { IsDir: true }) return;
        BrowsePath = (BrowsePath.TrimEnd('/') + "/" + BrowseSel.Name).Replace("//", "/");
        LoadBrowse(BrowsePath);
    }

    private void LoadBrowse(string path)
    {
        if (BrowseSiteSel is not { } site) return;
        if (site.Model.Protocol == TransferProtocol.Scp) return;   // SCP 不支持列目录，提示已在页面上
        Task.Run(() =>
        {
            try
            {
                var items = TransferServices.ListRemote(site.Model, path);
                Dispatcher.UIThread.Post(() =>
                {
                    BrowseItems.Clear();
                    foreach (var (name, size, isDir) in items.OrderBy(i => !i.IsDir).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
                        BrowseItems.Add(new BrowseItemViewModel { Name = name, Size = size, IsDir = isDir });
                });
            }
            catch (Exception ex)
            {
                _rt.RaiseError($"❌ 列目录失败 {site.Model.Name}:{path} — {ex.Message}");
                Dispatcher.UIThread.Post(() =>
                {
                    BrowseItems.Clear();
                    _host.Log(FileTransferToolModule.ToolTitle, "✘ 列目录失败：" + ex.Message);
                });
            }
        });
    }

    /// <summary>把当前浏览目录（或选中文件的完整路径）填到手动传输表单并选中该站点。</summary>
    [RelayCommand]
    private void UseAsUploadTarget()
    {
        if (BrowseSiteSel is not { } site) return;
        var dir = BrowsePath.TrimEnd('/');
        RemotePath = BrowseSel is { IsDir: false } ? $"{dir}/{BrowseSel.Name}" : dir;
        SiteSel = Sites.FirstOrDefault(s => s.Model.Id == site.Model.Id);
        _host.Log(FileTransferToolModule.ToolTitle, $"已把 {RemotePath} 填入传输表单的远程路径（站点 {site.Model.Name}）");
    }

    /// <summary>下载当前选中的远程文件到浏览页指定的本地路径。</summary>
    [RelayCommand]
    private void DownloadSelected()
    {
        if (BrowseSiteSel is not { } site) return;
        if (BrowseSel is not { IsDir: false } file)
        {
            _host.Log(FileTransferToolModule.ToolTitle, "△ 请先在列表中选中一个文件（目录不能下载）");
            return;
        }
        if (string.IsNullOrWhiteSpace(BrowseLocal))
        {
            _host.Log(FileTransferToolModule.ToolTitle, "△ 请先填写本地保存路径");
            return;
        }
        var remote = (BrowsePath.TrimEnd('/') + "/" + file.Name).Replace("//", "/");
        try
        {
            var r = _rt.ExecuteTransfer(site.Model, TransferDirection.Download, BrowseLocal, remote, "手动");
            _host.Log(FileTransferToolModule.ToolTitle,
                $"✔ 下载完成：{TransferMcpRuntime.FormatBytes(r.Bytes)} / {r.ElapsedMs:F0}ms");
        }
        catch (Exception ex)
        {
            _host.Log(FileTransferToolModule.ToolTitle, "✘ " + ex.Message);
        }
    }

    // ===== 传输记录 =====

    public ObservableCollection<TransferLogItemViewModel> Items { get; } = [];

    [ObservableProperty] private string _statsText = "0 条";

    [RelayCommand]
    private void ClearLog()
    {
        Items.Clear();
        _rt.ClearHistory();
        RefreshStatus();
    }

    /// <summary>复制全部传输记录。</summary>
    [RelayCommand]
    private async Task CopyAllLog()
    {
        if (Items.Count == 0)
        {
            _host.Log(FileTransferToolModule.ToolTitle, "△ 传输记录为空");
            return;
        }
        if (Owner is not { } owner) return;
        if (TopLevel.GetTopLevel(owner)?.Clipboard is not { } clip) return;
        var sb = new StringBuilder();
        foreach (var item in Items)
            sb.Append(item.TimeText).Append('\t').Append(item.KindText).Append('\t').AppendLine(item.Message);
        await clip.SetTextAsync(sb.ToString());
        _host.Log(FileTransferToolModule.ToolTitle, $"✔ 已复制 {Items.Count} 条传输记录");
    }

    // ===== MCP 服务状态（聚合网关） =====

    [ObservableProperty] private bool _mcpRunning;
    [ObservableProperty] private string _mcpStatusText = "未启动";
    [ObservableProperty] private string _mcpUrl = "";
    public string McpStateTag => McpRunning ? "Running" : "Stopped";

    [RelayCommand]
    private void StartMcp()
    {
        try
        {
            Toolkit.Shell.Mcp.McpGatewayRuntime.Instance.Start();
            _rt.SetEnabled(true);
            _host.Log(FileTransferToolModule.ToolTitle, $"✔ MCP 网关已启动：{Toolkit.Shell.Mcp.McpGatewayRuntime.Instance.EndpointUrl}（传输工具已启用）");
        }
        catch (Exception ex)
        {
            _host.Log(FileTransferToolModule.ToolTitle, "✘ " + ex.Message);
            _rt.RaiseError(ex.Message);
        }
        RefreshStatus();
    }

    [RelayCommand]
    private void StopMcp()
    {
        _rt.SetEnabled(false);
        _host.Log(FileTransferToolModule.ToolTitle, "文件传输工具已停用（网关与其他工具不受影响，整体启停在顶栏「MCP 服务」）");
        RefreshStatus();
    }

    [RelayCommand]
    private void OpenMcpSettings() => _host.ShowMcpSettings();

    // ===== 事件与状态 =====

    private void OnTraffic(TransferLogItem item)
    {
        if (_detached) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_detached) return;
            Items.Add(new TransferLogItemViewModel { Model = item });
            while (Items.Count > MaxItems) Items.RemoveAt(0);
            RefreshStatus();
        });
    }

    private void OnStateChanged()
    {
        if (_detached) return;
        Dispatcher.UIThread.Post(RefreshStatus);
    }

    private void RefreshStatus()
    {
        var gateway = Toolkit.Shell.Mcp.McpGatewayRuntime.Instance;
        McpRunning = gateway.IsRunning && _rt.Enabled;
        McpStatusText = !gateway.IsRunning ? "未启动" : _rt.Enabled ? "运行中" : "已停用";
        McpUrl = gateway.IsRunning ? gateway.EndpointUrl : "";
        OnPropertyChanged(nameof(McpStateTag));
        StatsText = $"共 {Items.Count} 条记录 · 站点 {TransferSiteStore.Sites.Count} 个";
    }
}
