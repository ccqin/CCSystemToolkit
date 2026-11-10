using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace NetTraffic.Core.Services;

/// <summary>进程详情（路径/公司/版本/启动时间/命令行）。</summary>
public sealed record ProcessDetail(
    string Name, int Pid, string? Path, string? Company, string? Description,
    string? Version, DateTime? StartTime, string? CommandLine);

/// <summary>一条 TCP 连接（本地/远程/状态）。</summary>
public sealed record ConnectionInfo(string Local, string Remote, string State);

/// <summary>进程详情与连接查询。</summary>
public static class ProcessDetailService
{
    public static ProcessDetail GetDetail(int pid, string fallbackName)
    {
        string? path = null, company = null, description = null, version = null, cmdline = null;
        DateTime? start = null;
        string name = fallbackName;
        try
        {
            using var p = Process.GetProcessById(pid);
            name = p.ProcessName;
            try { path = p.MainModule?.FileName; } catch { }
            try { start = p.StartTime; } catch { }
            if (path is not null)
            {
                var fv = FileVersionInfo.GetVersionInfo(path);
                company = fv.CompanyName;
                description = fv.FileDescription;
                version = fv.FileVersion;
            }
            cmdline = QueryCommandLine(pid);
        }
        catch
        {
        }
        return new ProcessDetail(name, pid, path, company, description, version, start, cmdline);
    }

    /// <summary>命令行（WMI 查询；失败返回 null）。</summary>
    private static string? QueryCommandLine(int pid)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (var obj in searcher.Get())
            {
                if (obj["CommandLine"] is string s) return s;
            }
        }
        catch
        {
        }
        return null;
    }

    /// <summary>该 PID 的全部 TCP 连接（本地/远程/状态）。</summary>
    public static List<ConnectionInfo> GetConnections(int pid)
    {
        var result = new List<ConnectionInfo>();
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0);
        if (size <= 0 || size > 4_000_000) return result;

        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, 2, 5, 0) != 0) return result;
            int count = Marshal.ReadInt32(buf);
            int rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            IntPtr p = buf + sizeof(int);
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(p);
                if (row.owningPid == pid)
                {
                    result.Add(new ConnectionInfo(
                        $"{IpToString(row.dwLocalAddr)}:{PortToShort(row.dwLocalPort)}",
                        $"{IpToString(row.dwRemoteAddr)}:{PortToShort(row.dwRemotePort)}",
                        StateText(row.dwState)));
                }
                p += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return result;
    }

    private static string IpToString(uint ip) =>
        $"{ip & 0xFF}.{(ip >> 8) & 0xFF}.{(ip >> 16) & 0xFF}.{(ip >> 24) & 0xFF}";

    private static int PortToShort(uint port) => (int)((port & 0xFF) << 8 | port >> 8);

    private static string StateText(uint state) => state switch
    {
        1 => "关闭",
        2 => "监听",
        3 => "SYN 发送",
        4 => "SYN 接收",
        5 => "已建立",
        6 => "FIN 等待1",
        7 => "FIN 等待2",
        8 => "关闭中",
        9 => "关闭确认",
        10 => "等待关闭",
        11 => "关闭等待",
        12 => "最后确认",
        _ => $"状态{state}",
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public int owningPid;
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool bOrder,
        int ulAf, int tableClass, int reserved);
}
