using System.Diagnostics;
using System.IO;
using FluentFTP;
using Renci.SshNet;

namespace FileTransferTool.Core;

/// <summary>一次传输的结果（字节数与耗时由实际文件测得）。</summary>
public sealed record TransferResult(long Bytes, double ElapsedMs);

/// <summary>三协议传输实现：FTP（FluentFTP 同步客户端）、SFTP / SCP（SSH.NET，密码或私钥认证）。</summary>
public static class TransferServices
{
    /// <summary>执行一次上传/下载；失败抛带中文原因的异常。</summary>
    public static TransferResult Transfer(TransferSite site, TransferDirection dir, string localPath, string remotePath)
    {
        var sw = Stopwatch.StartNew();
        var bytes = site.Protocol switch
        {
            TransferProtocol.Ftp => Ftp(site, dir, localPath, remotePath),
            TransferProtocol.Sftp => Sftp(site, dir, localPath, remotePath),
            TransferProtocol.Scp => Scp(site, dir, localPath, remotePath),
            _ => throw new InvalidOperationException($"未知协议：{site.Protocol}"),
        };
        sw.Stop();
        return new TransferResult(bytes, sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>列远程目录（SCP 协议本身不支持列目录）。</summary>
    public static List<(string Name, long Size, bool IsDir)> ListRemote(TransferSite site, string path)
    {
        return site.Protocol switch
        {
            TransferProtocol.Ftp => FtpList(site, path),
            TransferProtocol.Sftp => SftpList(site, path),
            _ => throw new InvalidOperationException("SCP 协议不支持列目录（改用 FTP/SFTP，或直接 transfer_file）"),
        };
    }

    /// <summary>测试站点连通性与认证（FTP 登录 / SFTP·SCP SSH 认证；失败抛带原因的异常）。</summary>
    public static void TestConnection(TransferSite site)
    {
        switch (site.Protocol)
        {
            case TransferProtocol.Ftp:
            {
                using var client = new FtpClient(site.Host)
                {
                    Port = site.EffectivePort,
                    Credentials = new System.Net.NetworkCredential(site.Username, site.Password),
                    Config = new FtpConfig { ConnectTimeout = 8000, EncryptionMode = FtpEncryptionMode.None },
                };
                client.Connect();
                if (!client.IsConnected) throw new InvalidOperationException("FTP 服务器连接失败（主机/端口/防火墙）");
                break;
            }
            case TransferProtocol.Sftp:
            {
                using var sftp = new SftpClient(SshConnection(site));
                sftp.Connect();
                if (!sftp.IsConnected) throw new InvalidOperationException("SFTP 服务器连接失败（主机/端口/认证）");
                break;
            }
            default:
            {
                using var scp = new ScpClient(SshConnection(site));
                scp.Connect();
                if (!scp.IsConnected) throw new InvalidOperationException("SCP 服务器连接失败（主机/端口/认证）");
                break;
            }
        }
    }

    // ===== 连接创建 =====

    private static ConnectionInfo SshConnection(TransferSite site)
    {
        if (string.IsNullOrWhiteSpace(site.Host))
            throw new InvalidOperationException("站点未配置主机地址（Host）");
        AuthenticationMethod auth;
        if (!string.IsNullOrWhiteSpace(site.KeyPath))
        {
            if (!File.Exists(site.KeyPath))
                throw new InvalidOperationException($"私钥文件不存在：{site.KeyPath}");
            auth = new PrivateKeyAuthenticationMethod(site.Username, new PrivateKeyFile(site.KeyPath));
        }
        else
        {
            auth = new PasswordAuthenticationMethod(site.Username, site.Password);
        }
        return new ConnectionInfo(site.Host, site.EffectivePort, site.Username, auth)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    // ===== FTP =====

    private static long Ftp(TransferSite site, TransferDirection dir, string localPath, string remotePath)
    {
        using var client = new FtpClient(site.Host)
        {
            Port = site.EffectivePort,
            Credentials = new System.Net.NetworkCredential(site.Username, site.Password),
            Config = new FtpConfig { ConnectTimeout = 15000, EncryptionMode = FtpEncryptionMode.None },
        };
        client.Connect();
        if (!client.IsConnected) throw new InvalidOperationException("FTP 服务器连接失败");
        try
        {
            if (dir == TransferDirection.Upload)
            {
                var size = new FileInfo(localPath).Length;
                var status = client.UploadFile(localPath, remotePath, FtpRemoteExists.Overwrite, createRemoteDir: true);
                if (status == FtpStatus.Failed) throw new InvalidOperationException("FTP 上传失败（检查路径与权限）");
                return size;
            }
            var ok = client.DownloadFile(localPath, remotePath, FtpLocalExists.Overwrite);
            if (ok == FtpStatus.Failed) throw new InvalidOperationException("FTP 下载失败（远程文件不存在或无权限）");
            return new FileInfo(localPath).Length;
        }
        finally
        {
            try { client.Disconnect(); } catch { }
        }
    }

    private static List<(string, long, bool)> FtpList(TransferSite site, string path)
    {
        using var client = new FtpClient(site.Host)
        {
            Port = site.EffectivePort,
            Credentials = new System.Net.NetworkCredential(site.Username, site.Password),
            Config = new FtpConfig { ConnectTimeout = 15000, EncryptionMode = FtpEncryptionMode.None },
        };
        client.Connect();
        try
        {
            return client.GetListing(path)
                .Select(i => (i.Name, i.Size, i.Type == FtpObjectType.Directory))
                .ToList();
        }
        finally
        {
            try { client.Disconnect(); } catch { }
        }
    }

    // ===== SFTP =====

    private static long Sftp(TransferSite site, TransferDirection dir, string localPath, string remotePath)
    {
        using var sftp = new SftpClient(SshConnection(site));
        sftp.Connect();
        if (!sftp.IsConnected) throw new InvalidOperationException("SFTP 服务器连接失败");
        try
        {
            if (dir == TransferDirection.Upload)
            {
                var size = new FileInfo(localPath).Length;
                // 确保远程目录存在（逐级创建，已存在忽略）
                CreateSftpDir(sftp, Path.GetDirectoryName(remotePath.Replace('/', '\\'))?.Replace('\\', '/') ?? "");
                using var fs = File.OpenRead(localPath);
                sftp.UploadFile(fs, remotePath, true);
                return size;
            }
            using var outFs = File.Create(localPath);
            sftp.DownloadFile(remotePath, outFs);
            outFs.Close();
            return new FileInfo(localPath).Length;
        }
        finally
        {
            try { sftp.Disconnect(); } catch { }
        }
    }

    private static void CreateSftpDir(SftpClient sftp, string dirPath)
    {
        if (string.IsNullOrWhiteSpace(dirPath) || dirPath == "/") return;
        var parts = dirPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cur = "";
        foreach (var part in parts)
        {
            cur += "/" + part;
            try
            {
                if (!sftp.Exists(cur)) sftp.CreateDirectory(cur);
            }
            catch
            { /* 并发/权限竞态：已存在则忽略 */ }
        }
    }

    private static List<(string, long, bool)> SftpList(TransferSite site, string path)
    {
        using var sftp = new SftpClient(SshConnection(site));
        sftp.Connect();
        try
        {
            return sftp.ListDirectory(string.IsNullOrWhiteSpace(path) ? "." : path)
                .Where(f => f.Name is not "." and not "..")
                .Select(f => (f.Name, f.Length, f.IsDirectory))
                .ToList();
        }
        finally
        {
            try { sftp.Disconnect(); } catch { }
        }
    }

    // ===== SCP =====

    private static long Scp(TransferSite site, TransferDirection dir, string localPath, string remotePath)
    {
        using var scp = new ScpClient(SshConnection(site));
        scp.Connect();
        if (!scp.IsConnected) throw new InvalidOperationException("SCP 服务器连接失败");
        try
        {
            if (dir == TransferDirection.Upload)
            {
                var info = new FileInfo(localPath);
                scp.Upload(info, remotePath);   // 远程可为目录或完整路径
                return info.Length;
            }
            // 下载：localPath 为目标文件路径（父目录须存在）
            var local = localPath.EndsWith('/') || localPath.EndsWith('\\')
                ? Path.Combine(localPath, Path.GetFileName(remotePath))
                : localPath;
            using var fs = File.Create(local);
            scp.Download(remotePath, fs);
            fs.Close();
            return new FileInfo(local).Length;
        }
        finally
        {
            try { scp.Disconnect(); } catch { }
        }
    }
}
