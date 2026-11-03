using System.Collections.ObjectModel;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetRouteList.Core.Services;
using Toolkit.App.Services;
using Toolkit.App.Views;
using Toolkit.Shell;

namespace Toolkit.App.ViewModels;

/// <summary>主页工具卡片 ViewModel（Module 为 null 表示"更多工具开发中"占位卡）。</summary>
public partial class ToolCardViewModel : ObservableObject
{
    private readonly ToolModule? _module;
    private readonly MainViewModel _main;
    private Bitmap? _bgImage;   // 当前背景图位图（换肤时释放旧的）

    public ToolCardViewModel(ToolModule? module, MainViewModel main)
    {
        _module = module;
        _main = main;
    }

    public ToolModule? Module => _module;
    public bool Enabled => _module is not null;

    /// <summary>外观配置的持久化键（工具 Id；占位卡用固定键）。</summary>
    public string Key => _module?.Id ?? MainViewModel.PlaceholderKey;
    public string Glyph => _module?.Glyph ?? "🧩";
    public string Title => _module?.Title ?? "更多工具开发中";
    public string Description => _module?.Description ?? "工具集会持续集成更多系统管理工具。";

    /// <summary>卡片默认背景色（未自定义时使用）。</summary>
    public const string DefaultCardColor = "#2E9E68";

    /// <summary>卡片背景（颜色或图片画刷；null = 默认绿色）。</summary>
    [ObservableProperty]
    private IBrush? _cardBackground;

    /// <summary>是否设置了自定义背景（决定文字用白色还是主题前景色，见 App.axaml 样式）。</summary>
    [ObservableProperty]
    private bool _hasCustomBackground;

    /// <summary>是否处于图片背景模式（叠加半透明遮罩保证文字可读）。</summary>
    [ObservableProperty]
    private bool _hasImageBackground;

    [RelayCommand]
    private void Open()
    {
        if (_module is { } m) _main.OpenTool(m);
    }

    /// <summary>按持久化配置刷新卡片外观（null = 恢复主题默认）。</summary>
    public void RefreshAppearance(CardAppearance? appearance)
    {
        _bgImage?.Dispose();
        _bgImage = null;
        switch (appearance?.Kind)
        {
            case CardAppearanceKind.Color when appearance.Color is { } hex:
                CardBackground = new SolidColorBrush(Color.Parse(hex));
                HasCustomBackground = true;
                HasImageBackground = false;
                break;
            case CardAppearanceKind.Image when appearance.ImagePath is { } path && File.Exists(path):
                _bgImage = new Bitmap(path);
                CardBackground = new ImageBrush(_bgImage) { Stretch = Stretch.UniformToFill };
                HasCustomBackground = true;
                HasImageBackground = true;
                break;
            default:
                // 默认绿色底 + 白字（用户要求；同时保证背景层 Border 始终可命中）
                CardBackground = new SolidColorBrush(Color.Parse(DefaultCardColor));
                HasCustomBackground = true;
                HasImageBackground = false;
                break;
        }
    }

    /// <summary>设为预设/自定义纯色背景。</summary>
    [RelayCommand]
    private void SetColor(string hex) =>
        _main.SetCardAppearance(Key, new CardAppearance { Kind = CardAppearanceKind.Color, Color = hex });

    /// <summary>打开取色对话框自定义颜色。</summary>
    [RelayCommand]
    private async Task CustomColor()
    {
        try
        {
            var current = CardBackground as SolidColorBrush;
            if (await CardColorWindow.PickAsync(current?.Color) is { } picked)
                SetColor(picked.ToString());   // Color.ToString() = "#AARRGGBB"，Color.Parse 可直接解析
        }
        catch (Exception ex)
        {
            _main.Log("系统", $"✘ 打开取色对话框失败：{ex.Message}");
        }
    }

    /// <summary>选择本地图片作为背景（复制到用户目录，原图移动不影响显示）。</summary>
    [RelayCommand]
    private async Task PickImage()
    {
        try
        {
            if (AppDialogs.Owner is not { } owner) return;
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择卡片背景图片",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("图片") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"] },
                ],
            });
            if (files.Count == 0) return;

            await using var src = await files[0].OpenReadAsync();
            var path = CardAppearanceStore.SaveImage(Key, src, Path.GetExtension(files[0].Name));
            _main.SetCardAppearance(Key, new CardAppearance { Kind = CardAppearanceKind.Image, ImagePath = path });
        }
        catch (Exception ex)
        {
            _main.Log("系统", $"✘ 设置卡片背景图片失败：{ex.Message}");
        }
    }

    /// <summary>恢复主题默认外观。</summary>
    [RelayCommand]
    private void ResetAppearance() => _main.SetCardAppearance(Key, null);
}

/// <summary>
/// 宿主主窗口 ViewModel：主页卡片导航、工具切换、全局权限状态与提权。
/// 实现 <see cref="IToolHost"/>，作为宿主能力注入各工具模块。
/// </summary>
public partial class MainViewModel : ObservableObject, IToolHost
{
    private HomeView _home = null!;
    private string _activeToolTitle = "";
    private Dictionary<string, CardAppearance> _cardAppearances = [];

    /// <summary>占位卡（"更多工具开发中"）外观配置的持久化键。</summary>
    public const string PlaceholderKey = "placeholder";

    public ObservableCollection<ToolCardViewModel> Cards { get; } = [];

    /// <summary>当前内容：主页卡片墙或某个工具的根视图。</summary>
    [ObservableProperty]
    private Control? _currentView;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    private bool _isHome = true;

    public string HeaderTitle => IsHome ? "系统管理工具集" : _activeToolTitle;

    public bool IsElevated { get; } = ElevationHelper.IsElevated();

    public string PrivilegeText => IsElevated
        ? "管理员 · 完整功能可用"
        : "普通权限 · 部分功能只读";
    public string PrivilegeColor => IsElevated ? "#107C10" : "#D13438";

    [ObservableProperty]
    private string _statusText = "就绪。点击工具卡片开始使用。";

    /// <summary>统一日志：所有工具经 IToolHost.Log 归集到这里，主页展示（最新在前）。</summary>
    public ObservableCollection<ToolLogEntry> Logs { get; } = [];

    private const int MaxLogEntries = 500;
    private const string SystemSource = "系统";

    string IToolHost.StatusText
    {
        get => StatusText;
        set => StatusText = value;
    }

    /// <summary>注册工具清单并注入主页视图（App 启动时调用一次）；同时恢复各卡片的自定义背景。</summary>
    public void InitializeTools(IReadOnlyList<ToolModule> tools, HomeView home)
    {
        _home = home;
        _cardAppearances = CardAppearanceStore.Load();

        foreach (var t in tools)
        {
            var card = new ToolCardViewModel(t, this);
            if (_cardAppearances.TryGetValue(t.Id, out var a)) card.RefreshAppearance(a);
            Cards.Add(card);
        }

        var placeholder = new ToolCardViewModel(null, this);
        if (_cardAppearances.TryGetValue(PlaceholderKey, out var pa)) placeholder.RefreshAppearance(pa);
        Cards.Add(placeholder);   // 占位卡
        CurrentView = home;
    }

    /// <summary>
    /// 设置（或清除）某张卡片的自定义背景：更新内存配置 → 立即持久化 → 刷新卡片显示。
    /// </summary>
    public void SetCardAppearance(string key, CardAppearance? appearance)
    {
        if (appearance is null) _cardAppearances.Remove(key);
        else _cardAppearances[key] = appearance;
        CardAppearanceStore.Save(_cardAppearances);
        Cards.FirstOrDefault(c => c.Key == key)?.RefreshAppearance(appearance);
    }

    public void OpenTool(ToolModule tool)
    {
        _activeToolTitle = tool.Title;
        IsHome = false;
        CurrentView = tool.CreateView();
        tool.OnEnter();
        StatusText = $"已进入工具：{tool.Title}";
        Log(SystemSource, $"已进入工具：{tool.Title}");
    }

    [RelayCommand]
    private void GoHome()
    {
        IsHome = true;
        CurrentView = _home;
        StatusText = "就绪。";
    }

    /// <summary>以管理员身份重启宿主程序（UAC 确认后当前实例退出，重启后回到主页）。</summary>
    [RelayCommand]
    public void ElevateRestart()
    {
        if (IsElevated)
        {
            StatusText = "当前已是管理员权限。";
            return;
        }
        if (ElevationHelper.RestartElevated(out var error))
        {
            Environment.Exit(0);
        }
        else
        {
            StatusText = error;
            Log(SystemSource, "✘ " + error);
        }
    }

    /// <summary>归集一条日志（IToolHost.Log 的实现，工具与宿主自身动作共用）。
    /// 消息以 ✔ / ✘ 开头自动识别为成功 / 失败级别。</summary>
    public void Log(string source, string message)
    {
        var level = message.StartsWith("✘") ? ToolLogLevel.Error
            : message.StartsWith("✔") ? ToolLogLevel.Success
            : ToolLogLevel.Info;
        Logs.Insert(0, new ToolLogEntry { Source = source, Message = message, Level = level });
        while (Logs.Count > MaxLogEntries) Logs.RemoveAt(Logs.Count - 1);
    }

    [RelayCommand]
    private void ClearLogs() => Logs.Clear();

    /// <summary>主题循环索引：0=跟随系统 1=浅色 2=深色。</summary>
    private int _themeIndex;

    [ObservableProperty]
    private string _themeText = "🌈 跟随系统";

    /// <summary>循环切换主题变体：跟随系统 → 浅色 → 深色（RequestedThemeVariant，DynamicResource 即时刷新）。</summary>
    [RelayCommand]
    private void CycleTheme()
    {
        try
        {
            _themeIndex = (_themeIndex + 1) % 3;
            var name = _themeIndex switch
            {
                1 => "浅色",
                2 => "深色",
                _ => "跟随系统",
            };
            if (Application.Current is { } app)
            {
                app.RequestedThemeVariant = _themeIndex switch
                {
                    1 => ThemeVariant.Light,
                    2 => ThemeVariant.Dark,
                    _ => ThemeVariant.Default,
                };
            }
            ThemeText = _themeIndex switch
            {
                1 => "🌈 浅色",
                2 => "🌙 深色",
                _ => "🌈 跟随系统",
            };
            StatusText = $"主题已切换：{name}";
            Log(SystemSource, $"主题已切换：{name}");
        }
        catch (Exception ex)
        {
            StatusText = "主题切换失败：" + ex.Message;
            Log(SystemSource, "✘ 主题切换失败：" + ex.Message);
        }
    }

    void IToolHost.Log(string source, string message) => Log(source, message);
}
