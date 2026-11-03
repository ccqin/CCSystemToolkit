using Avalonia.Controls;
using NetRouteList.Core.Models;
using NetRouteList.Core.Services;
using NetRouteList.Ui.ViewModels;

namespace NetRouteList.Ui.Views;

/// <summary>
/// 路由管理工具根视图（原独立程序的主窗口，现作为工具集中的一个工具嵌入宿主窗口）。
/// code-behind 负责把各页面 VM 的请求事件接到对话框、UAC 检查与文件选择器上。
/// </summary>
public partial class NetRouteListView : UserControl
{
    /// <summary>对话框的父窗口：宿主主窗口。</summary>
    private Window OwnerWindow => TopLevel.GetTopLevel(this) as Window ?? Dialogs.Owner;

    private bool _loaded;

    public NetRouteListView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireUp();
        AttachedToVisualTree += (_, _) =>
        {
            Dialogs.Owner = OwnerWindow;
            if (_loaded || DataContext is not NetRouteListViewModel vm) return;
            _loaded = true;
            vm.CurrentPage = vm.Dashboard;   // 确保初始停在“总览”
            vm.RefreshAll();
        };
    }

    private bool _wired;

    private void WireUp()
    {
        if (_wired || DataContext is not NetRouteListViewModel vm) return;
        _wired = true;

        // ---------- 总览：一键修复 ----------
        vm.Dashboard.RepairRequest += async issueVm =>
        {
            if (issueVm.Issue.Repair is not { } repair) return;
            if (!vm.IsElevated)
            {
                await Dialogs.Confirm("需要管理员权限", "修复操作要修改系统路由表，当前是普通权限。\n请先点击左下角按钮以管理员身份重启本工具，再执行修复。");
                return;
            }

            bool optionValue = repair.OptionDefault;
            if (repair.OptionText is { } optionText)
            {
                var (confirmed, checked_) = await Dialogs.ConfirmWithCheck(
                    repair.Title, repair.ConfirmText, optionText, repair.OptionDefault,
                    repair.Preview, "执行修复");
                if (!confirmed) return;
                optionValue = checked_;
            }
            else
            {
                var preview = repair.Preview(repair.OptionDefault) is { Count: > 0 } previewLines
                    ? previewLines.ToList()
                    : new List<string> { "（无可预览命令）" };
                if (!await Dialogs.Confirm(repair.Title, repair.ConfirmText, preview, "执行修复")) return;
            }

            var results = repair.Execute(optionValue);
            var lines = results.Select(r =>
                (r.Success ? "✔ " : "✘ ") + r.Message + (r.EquivalentCommand is { } c ? $"   [{c}]" : "")).ToList();
            foreach (var r in results) vm.AddLog((r.Success ? "✔ " : "✘ ") + r.Message);
            await Dialogs.Report("修复结果", $"完成 {results.Count} 项操作：成功 {results.Count(r => r.Success)} / 失败 {results.Count(r => !r.Success)}。", lines);
            vm.RefreshAll();
        };

        // ---------- 路由表：添加 / 编辑 / 删除 ----------
        vm.RoutesPage.EditRequested += async original =>
        {
            if (!vm.IsElevated)
            {
                await Dialogs.Confirm("需要管理员权限", "添加/修改路由需要管理员权限。\n请先点击左下角按钮以管理员身份重启本工具。");
                return;
            }
            if (original is not null && original.Entry.IsLoopback)
            {
                await Dialogs.Confirm("不允许修改", "环回路由是系统核心路由，本工具拒绝修改它。");
                return;
            }

            var adapters = new System.Collections.ObjectModel.ObservableCollection<Core.Models.AdapterInfo>(
                vm.RoutesPage.Snapshot?.Adapters ?? vm.TakeSnapshot().Adapters);
            var editVm = new RouteEditViewModel(adapters, original);
            var win = new RouteEditWindow { DataContext = editVm };
            await win.ShowDialog(OwnerWindow);
            if (win.Result is not { } res) return;

            var results = res.Original is { } old
                ? vm.Routes.UpdateRoute(old, res.Mutation)
                : new List<RouteOpResult> { vm.Routes.AddRoute(res.Mutation) };
            vm.RoutesPage.ApplyMutationResult(results);
        };

        vm.RoutesPage.DeleteRequested += async row =>
        {
            if (!vm.IsElevated)
            {
                await Dialogs.Confirm("需要管理员权限", "删除路由需要管理员权限。\n可复制下方的等效命令到管理员命令行手动执行：" + row.DeleteCommandPreview);
                return;
            }
            var detail = new List<string>
            {
                row.DeleteCommandPreview,
                "",
                "提示：系统/DHCP 自动生成的路由删掉后可能被自动重建；手动添加的不会。",
            };
            if (!await Dialogs.Confirm("删除路由", $"确定删除这条路由吗？\n\n{row.Entry}", detail, "删除")) return;

            var result = vm.Routes.DeleteRoute(row.Entry);
            vm.RoutesPage.ApplyMutationResult(new[] { result });
        };

        // ---------- 网卡 → 路由跳转 ----------
        vm.AdaptersPage.ViewRoutesRequested += async row =>
        {
            vm.RoutesPage.InterfaceFilter = row.Info.Name;
            vm.CurrentPage = vm.RoutesPage;
        };

        // ---------- 备份 ----------
        vm.BackupsPage.CreateRequested += async () =>
        {
            if (await Dialogs.CreateBackup() is not { } input) return;
            try
            {
                var snap = vm.Backups.CreateBackup(input.name, input.note);
                vm.AddLog($"✔ 已创建备份 “{snap.Name}”（{snap.Routes.Count} 条路由）");
                vm.BackupsPage.Reload();
                vm.StatusText = $"已创建备份：{snap.Name}";
            }
            catch (Exception ex)
            {
                vm.AddLog("✘ 创建备份失败：" + ex.Message);
                await Dialogs.Confirm("创建失败", ex.Message);
            }
        };

        vm.BackupsPage.RestoreRequested += async item =>
        {
            if (!vm.IsElevated)
            {
                await Dialogs.Confirm("需要管理员权限", "应用（恢复）备份会修改系统路由表，需要管理员权限。\n请先点击左下角按钮以管理员身份重启本工具。");
                return;
            }
            var vm2 = new RestoreWindowViewModel { Snapshot = item.Snapshot };
            var win = new RestoreWindow { DataContext = vm2 };
            await win.ShowDialog(OwnerWindow);
            if (win.Options is not { } options) return;

            try
            {
                var report = vm.Backups.Restore(item.Snapshot, options);
                var lines = new List<string>();
                if (report.SafetySnapshotPath is { } p)
                    lines.Add($"安全快照已保存：{System.IO.Path.GetFileName(p)}（出问题可用它退回）");
                lines.Add("");
                foreach (var r in report.Results)
                    lines.Add($"{TagText(r.Action)} {r.Route}" +
                              (string.IsNullOrEmpty(r.Message) ? "" : $"\n      {r.Message}"));
                vm.AddLog($"已应用备份 “{report.BackupName}”：新增 {report.Added} / 更新 {report.Updated} / 删除 {report.Deleted} / 跳过 {report.Skipped} / 失败 {report.Failed}");
                await Dialogs.Report("恢复结果",
                    $"备份“{report.BackupName}”应用完成：新增 {report.Added} · 更新 {report.Updated} · 删除 {report.Deleted} · 已存在 {report.Same} · 跳过 {report.Skipped} · 失败 {report.Failed}。",
                    lines);
                vm.RefreshAll();
            }
            catch (Exception ex)
            {
                vm.AddLog("✘ 恢复失败：" + ex.Message);
                await Dialogs.Confirm("恢复失败", ex.Message);
            }
        };

        vm.BackupsPage.DeleteRequested += async item =>
        {
            if (!await Dialogs.Confirm("删除备份", $"确定删除备份“{item.Name}”（{item.CreatedAt}）吗？\n此操作只删除备份文件，不影响系统路由。", null, "删除")) return;
            try
            {
                vm.Backups.Delete(item.Snapshot);
                vm.AddLog($"已删除备份 “{item.Name}”");
                vm.BackupsPage.Reload();
            }
            catch (Exception ex)
            {
                await Dialogs.Confirm("删除失败", ex.Message);
            }
        };

        vm.BackupsPage.DiffRequested += async item =>
        {
            try
            {
                var diff = vm.Backups.Diff(item.Snapshot);
                var lines = new List<string>();
                lines.Add($"◆ 备份中有、当前没有（恢复时添加）：{diff.OnlyInBackup.Count} 条");
                lines.AddRange(diff.OnlyInBackup.Select(b => "  + " + b.Display));
                lines.Add("");
                lines.Add($"◆ 当前有、备份没有（完全恢复时删除）：{diff.OnlyInCurrent.Count} 条");
                lines.AddRange(diff.OnlyInCurrent.Select(c => "  - " + c));
                lines.Add("");
                lines.Add($"◆ 两边都有但跃点不同（恢复时更新）：{diff.Different.Count} 条");
                lines.AddRange(diff.Different.Select(d => $"  ~ 备份跃点 {d.Backup.Metric} ↔ 当前 {d.Current.Metric}   {d.Backup.Display}"));
                await Dialogs.Report($"备份对比：{item.Name}", $"与当前路由表对比完成。", lines);
            }
            catch (Exception ex)
            {
                await Dialogs.Confirm("对比失败", ex.Message);
            }
        };

        vm.BackupsPage.ExportRequested += async () =>
        {
            if (vm.BackupsPage.Selected is not { } sel)
            {
                await Dialogs.Confirm("未选择备份", "请先在左侧选择要导出的备份。");
                return;
            }
            var file = await OwnerWindow.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "导出备份",
                SuggestedFileName = "netroutelist-backup.json",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("JSON 备份") { Patterns = new[] { "*.json" } },
                },
            });
            if (file is null) return;
            try
            {
                vm.Backups.Export(sel.Snapshot, file.Path.LocalPath);
                vm.AddLog($"✔ 已导出备份到 {file.Path.LocalPath}");
                await Dialogs.Confirm("导出成功", "备份已导出：" + file.Path.LocalPath);
            }
            catch (Exception ex)
            {
                await Dialogs.Confirm("导出失败", ex.Message);
            }
        };

        vm.BackupsPage.ImportRequested += async () =>
        {
            var files = await OwnerWindow.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "导入备份 JSON",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("JSON 备份") { Patterns = new[] { "*.json" } },
                },
            });
            if (files.Count == 0) return;
            try
            {
                var snap = vm.Backups.Import(files[0].Path.LocalPath);
                vm.AddLog($"✔ 已导入备份 “{snap.Name}”（{snap.Routes.Count} 条路由）");
                vm.BackupsPage.Reload();
                vm.StatusText = $"已导入备份：{snap.Name}";
            }
            catch (Exception ex)
            {
                await Dialogs.Confirm("导入失败", ex.Message);
            }
        };
    }

    private static string TagText(RouteRestoreAction a) => a switch
    {
        RouteRestoreAction.Added => "＋",
        RouteRestoreAction.Deleted => "－",
        RouteRestoreAction.Updated => "~",
        RouteRestoreAction.Same => "＝",
        RouteRestoreAction.Skipped => "·",
        RouteRestoreAction.Failed => "✘",
        _ => "?",
    };
}
