using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTransferTool.Core;

namespace FileTransferTool.Ui.Views;

/// <summary>站点编辑弹窗 VM：表单 + 校验 + 就地「测试连接」（用当前填写内容，不必先保存）。</summary>
public partial class SiteEditWindowViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> Protocols = ["FTP", "SFTP", "SCP"];

    /// <summary>编辑目标（新增时为新建的空站点）。</summary>
    public TransferSite Model { get; }

    public bool IsNew { get; }

    [ObservableProperty] private string _nameInput = "";
    [ObservableProperty] private string _hostInput = "";
    [ObservableProperty] private string _portInput = "";
    [ObservableProperty] private string _usernameInput = "";
    [ObservableProperty] private string _passwordInput = "";
    [ObservableProperty] private string _keyPathInput = "";
    [ObservableProperty] private bool _passive = true;
    [ObservableProperty] private int _protocolIndex;

    /// <summary>校验错误提示（红色显示在保存按钮上方；为空表示无错误）。</summary>
    [ObservableProperty] private string _errorText = "";

    /// <summary>测试连接结果文本（按钮旁就地显示）。</summary>
    [ObservableProperty] private string _testResult = "";

    /// <summary>测试结果状态（样式着色：ok=绿 fail=红 busy=灰）。</summary>
    [ObservableProperty] private string _testState = "";

    [ObservableProperty] private bool _testing;

    public SiteEditWindowViewModel(TransferSite model, bool isNew)
    {
        Model = model;
        IsNew = isNew;
        NameInput = model.Name;
        HostInput = model.Host;
        PortInput = (model.Port > 0 ? model.Port : DefaultPortOf(model.Protocol)).ToString();
        UsernameInput = model.Username;
        PasswordInput = model.Password;
        KeyPathInput = model.KeyPath ?? "";
        Passive = model.Passive;
        ProtocolIndex = (int)model.Protocol;
    }

    private static int DefaultPortOf(TransferProtocol protocol) => protocol == TransferProtocol.Ftp ? 21 : 22;

    /// <summary>协议切换时端口框跟随默认值（FTP 21 / SFTP·SCP 22）：框内为空或仍是某协议默认值时视为"跟随"。</summary>
    partial void OnProtocolIndexChanged(int value)
    {
        var def = DefaultPortOf((TransferProtocol)value);
        if (string.IsNullOrWhiteSpace(PortInput)
            || (int.TryParse(PortInput.Trim(), out var p) && (p == 21 || p == 22)))
        {
            PortInput = def.ToString();
        }
    }

    /// <summary>校验并写回站点（失败抛中文异常）。</summary>
    public TransferSite Build()
    {
        if (string.IsNullOrWhiteSpace(NameInput)) throw new InvalidOperationException("站点名称不能为空");
        var site = BuildTestSite();
        site.Name = NameInput.Trim();
        return site;
    }

    /// <summary>用当前表单内容构造站点（测试连接用；只校验主机，名称可为空）。</summary>
    private TransferSite BuildTestSite()
    {
        if (string.IsNullOrWhiteSpace(HostInput)) throw new InvalidOperationException("主机地址不能为空");
        var port = 0;
        if (!string.IsNullOrWhiteSpace(PortInput)
            && (!int.TryParse(PortInput.Trim(), out port) || port is < 1 or > 65535))
            throw new InvalidOperationException("端口须为 1-65535 的整数（留空用协议默认）");
        Model.Host = HostInput.Trim();
        Model.Port = port;
        Model.Username = UsernameInput.Trim();
        Model.Password = PasswordInput;
        Model.KeyPath = string.IsNullOrWhiteSpace(KeyPathInput) ? null : KeyPathInput.Trim();
        Model.Passive = Passive;
        Model.Protocol = (TransferProtocol)ProtocolIndex;
        if (string.IsNullOrWhiteSpace(Model.Name)) Model.Name = $"{Model.ProtocolText}://{Model.Host}";
        return Model;
    }

    /// <summary>用当前填写内容测试连通性与认证（后台执行，结果就地显示并写传输记录）。</summary>
    [RelayCommand]
    private void TestConnection()
    {
        TransferSite site;
        try
        {
            site = BuildTestSite();
            ErrorText = "";
        }
        catch (Exception ex)
        {
            ErrorText = "⚠ " + ex.Message;
            return;
        }

        Testing = true;
        TestState = "busy";
        TestResult = $"⏳ 正在测试 {site.ProtocolText} {site.Host}:{site.EffectivePort} …";
        Task.Run(() =>
        {
            string state, text;
            try
            {
                FileTransferTool.Core.TransferServices.TestConnection(site);
                state = "ok";
                text = $"✅ 连接成功：{site.ProtocolText} {site.Host}:{site.EffectivePort}（用户 {site.Username}）";
                TransferMcpRuntime.Instance.RaiseDone($"✅ {text}");
            }
            catch (Exception ex)
            {
                state = "fail";
                text = $"❌ 连接失败：{ex.Message}";
                TransferMcpRuntime.Instance.RaiseError(
                    $"❌ {site.ProtocolText} 连接测试失败：{site.Host}:{site.EffectivePort} — {ex.Message}");
            }
            Dispatcher.UIThread.Post(() =>
            {
                Testing = false;
                TestState = state;
                TestResult = text;
            });
        });
    }
}

/// <summary>站点编辑弹窗：PickAsync 弹出模态窗，保存返回站点、取消返回 null。</summary>
public partial class SiteEditWindow : Window
{
    public SiteEditWindow()
    {
        InitializeComponent();
    }

    private void OkClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var site = ((SiteEditWindowViewModel)DataContext!).Build();
            Close(site);
        }
        catch (Exception ex)
        {
            // 校验失败：弹窗内红色提示 + 标题同步，窗口保持打开（直接关掉=放弃保存）
            Title = "站点编辑 — 保存失败";
            ((SiteEditWindowViewModel)DataContext!).ErrorText = "⚠ " + ex.Message;
        }
    }

    private void CancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(null);

    /// <summary>浏览选择私钥文件，路径填入输入框。</summary>
    private async void BrowseKeyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "选择私钥文件（如 id_rsa / .pem）",
            AllowMultiple = false,
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("所有文件") { Patterns = ["*"] }],
        });
        if (files.Count == 0 || DataContext is not SiteEditWindowViewModel vm) return;
        vm.KeyPathInput = files[0].TryGetLocalPath() ?? files[0].Path.ToString();
    }

    /// <summary>弹出编辑窗：existing 为 null 表示新增（protocol 指定初始协议）。</summary>
    public static async Task<TransferSite?> PickAsync(Window owner, TransferSite? existing, TransferProtocol protocol)
    {
        var site = existing ?? new TransferSite { Protocol = protocol };
        var vm = new SiteEditWindowViewModel(site, existing is null);
        var win = new SiteEditWindow { DataContext = vm };
        return await win.ShowDialog<TransferSite?>(owner);
    }
}
