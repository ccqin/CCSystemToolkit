using System.Collections.ObjectModel;
using NetTraffic.Core.Services;

namespace NetTraffic.Ui.ViewModels;

/// <summary>进程详情对话框 VM。</summary>
public sealed class TrafficDetailViewModel
{
    public ProcessDetail Detail { get; }
    public string DownTotal { get; }
    public string UpTotal { get; }
    public ObservableCollection<ConnectionInfo> Connections { get; }

    public TrafficDetailViewModel(ProcessDetail detail, string downTotal, string upTotal, List<ConnectionInfo> connections)
    {
        Detail = detail;
        DownTotal = downTotal;
        UpTotal = upTotal;
        Connections = new ObservableCollection<ConnectionInfo>(connections);
    }
}
