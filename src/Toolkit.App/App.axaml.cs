using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MenuClean.Ui;
using NetRouteList.Ui;
using PermDoctor.Ui;
using ShellExt.Ui;
using Toolkit.App.Tools;
using Toolkit.App.ViewModels;
using Toolkit.App.Views;

namespace Toolkit.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var main = new MainViewModel();
            // 工具清单：新增工具时在此注册一个 ToolModule
            main.InitializeTools(
            [
                new NetRouteListToolModule { Host = main },
                new PermDoctorToolModule { Host = main },
                new MenuCleanToolModule { Host = main },
                new ShellExtToolModule { Host = main },
                new LogsToolModule(main) { Host = main },
                // 未来工具在此追加……
            ], new HomeView { DataContext = main });
            desktop.MainWindow = new MainWindow { DataContext = main };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
