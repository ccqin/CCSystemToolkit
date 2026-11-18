using System.Text;

namespace SerialMcpTool.Core;

/// <summary>串口收发方向（卡片监控列表与 MCP 日志共用）。</summary>
public enum SerialDirection
{
    /// <summary>AI（经 MCP）下发到串口。</summary>
    AiToPort,

    /// <summary>界面手动下发到串口。</summary>
    ManualToPort,

    /// <summary>串口返回的数据。</summary>
    FromPort,

    /// <summary>系统提示（打开/关闭串口、服务器启停等）。</summary>
    Info,

    /// <summary>AI 调用记录（每次 tools/call 的参数摘要与结果）。</summary>
    AiCall,

    /// <summary>错误提示。</summary>
    Error,
}

/// <summary>一条串口收发/提示记录（卡片监控数据源；Data 为 null 表示纯提示行）。</summary>
public sealed class SerialTrafficItem
{
    public DateTime Time { get; init; } = DateTime.Now;
    public SerialDirection Direction { get; init; }

    /// <summary>收发原始字节（Info/Error 为 null）。</summary>
    public byte[]? Data { get; init; }

    /// <summary>提示文本（仅 Info/Error 有效）。</summary>
    public string Message { get; init; } = "";

    public string TimeText => Time.ToString("HH:mm:ss.fff");
}

/// <summary>串口内容显示/解析工具：转义文本、HEX、发送文本转义还原。</summary>
public static class SerialText
{
    /// <summary>格式化为十六进制（空格分隔大写，如 "41 0D 0A"）。</summary>
    public static string ToHex(byte[] data)
    {
        if (data.Length == 0) return string.Empty;
        var sb = new StringBuilder(data.Length * 3);
        foreach (var b in data)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 显示用转义文本：可打印 ASCII 原样，\r \n \t 转为 \r \n \t 字面，
    /// UTF-8 多字节序列（中文等）原样解码，其余字节转 \xNN。
    /// </summary>
    public static string EscapeText(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 2);
        var i = 0;
        while (i < data.Length)
        {
            var b = data[i];
            switch (b)
            {
                case (byte)'\r': sb.Append("\\r"); i++; continue;
                case (byte)'\n': sb.Append("\\n"); i++; continue;
                case (byte)'\t': sb.Append("\\t"); i++; continue;
            }
            if (b is >= 0x20 and < 0x7F)
            {
                sb.Append((char)b);
                i++;
                continue;
            }
            // 尝试按 UTF-8 多字节序列解码（有效则原样显示，如中文）
            var len = b is >= 0xC2 and <= 0xDF ? 2
                : b is >= 0xE0 and <= 0xEF ? 3
                : b is >= 0xF0 and <= 0xF4 ? 4 : 0;
            if (len > 0 && i + len <= data.Length)
            {
                var s = Encoding.UTF8.GetString(data, i, len);
                if (s.Length == 1 && s[0] != '\uFFFD')
                {
                    sb.Append(s);
                    i += len;
                    continue;
                }
            }
            sb.Append("\\x").Append(b.ToString("X2"));
            i++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// 把输入文本按 \r \n \t \\ \xNN 转义还原为字节（UTF-8 编码）。
    /// AI 直接发送 "AT\r\n" 这类带转义的字面量时可得到真实的回车换行。
    /// </summary>
    public static byte[] UnescapeText(string text)
    {
        if (text.Length == 0) return [];
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\\' && i + 1 < text.Length)
            {
                switch (text[i + 1])
                {
                    case 'r': sb.Append('\r'); i++; continue;
                    case 'n': sb.Append('\n'); i++; continue;
                    case 't': sb.Append('\t'); i++; continue;
                    case '\\': sb.Append('\\'); i++; continue;
                    case 'x' when i + 3 < text.Length && IsHex(text[i + 2]) && IsHex(text[i + 3]):
                        sb.Append((char)Convert.ToInt32(text.Substring(i + 2, 2), 16));
                        i += 3;
                        continue;
                }
            }
            sb.Append(ch);
        }
        return Encoding.UTF8.GetBytes(sb.ToString());

        static bool IsHex(char c) => char.IsAsciiHexDigit(c);
    }

    /// <summary>解析十六进制为字节（容忍空格/逗号/分号/0x 前缀/连续写法，如 "AT" 无效会抛出说明性异常）。</summary>
    public static byte[] HexToBytes(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || ch is ',' or ';' or ':') continue;
            sb.Append(ch);
        }
        var hex = sb.ToString().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (hex.Length == 0) throw new ArgumentException("十六进制内容为空");
        if (hex.Length % 2 != 0) throw new ArgumentException("十六进制长度应为偶数位");
        var result = new byte[hex.Length / 2];
        try
        {
            for (var i = 0; i < result.Length; i++)
                result[i] = byte.Parse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
        }
        catch (FormatException)
        {
            throw new ArgumentException("十六进制内容无法解析");
        }
        return result;
    }
}
