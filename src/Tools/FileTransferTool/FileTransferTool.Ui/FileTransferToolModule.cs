using Avalonia.Controls;
using FileTransferTool.Core;
using FileTransferTool.Ui.ViewModels;
using FileTransferTool.Ui.Views;
using Toolkit.Shell;
using Toolkit.Shell.Mcp;

namespace FileTransferTool.Ui;

/// <summary>文件传输工具模块：FTP / SFTP / SCP 站点管理与传输（含 MCP 服务，AI 可直接调用）。</summary>
public sealed class FileTransferToolModule : ToolModule
{
    /// <summary>工具显示名（统一日志来源标记）。</summary>
    public const string ToolTitle = "文件传输";

    static FileTransferToolModule() => McpServiceRegistry.Register(TransferMcpRuntime.Instance);

    public override string Id => "filetransfer";
    public override string Title => ToolTitle;
    public override string Glyph => "📤";

    public override string Description =>
        "FTP / SFTP / SCP 文件传输（按协议分页管理站点），内置传输 MCP 服务器：AI 客户端经 MCP 协议上传/下载文件，卡片实时显示传输记录。服务启停/接入令牌在顶栏「MCP 服务」中统一管理。";

    public override UserControl CreateView() => new FileTransferView { DataContext = new FileTransferViewModel(Host) };
}
