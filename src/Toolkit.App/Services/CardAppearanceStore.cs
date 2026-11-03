using System.IO;
using System.Text.Json;

namespace Toolkit.App.Services;

/// <summary>主页卡片背景类型。</summary>
public enum CardAppearanceKind
{
    Default,
    Color,
    Image,
}

/// <summary>单张主页卡片的外观配置（JSON 持久化）。</summary>
public sealed class CardAppearance
{
    public CardAppearanceKind Kind { get; set; } = CardAppearanceKind.Default;

    /// <summary>颜色背景（#AARRGGBB；Kind=Color 时有效）。</summary>
    public string? Color { get; set; }

    /// <summary>背景图片绝对路径（Kind=Image 时有效；文件已复制到用户目录，原图片移动不受影响）。</summary>
    public string? ImagePath { get; set; }
}

/// <summary>
/// 卡片外观持久化：%LOCALAPPDATA%\SystemToolkit\card-appearance.json；
/// 背景图片复制到同目录 cardbg\ 下，避免用户选择的原图被移动/删除后卡片背景失效。
/// </summary>
public static class CardAppearanceStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string BaseDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemToolkit");

    private static string StorePath => Path.Combine(BaseDir, "card-appearance.json");

    private static string ImageDir => Path.Combine(BaseDir, "cardbg");

    /// <summary>读取全部卡片外观（文件不存在或损坏时返回空表，卡片回退主题默认外观）。</summary>
    public static Dictionary<string, CardAppearance> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            return JsonSerializer.Deserialize<Dictionary<string, CardAppearance>>(File.ReadAllText(StorePath)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(Dictionary<string, CardAppearance> appearances)
    {
        try
        {
            Directory.CreateDirectory(BaseDir);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(appearances, JsonOpts));
        }
        catch
        {
            // 保存失败不影响本次会话的外观效果，仅下次启动回退旧配置
        }
    }

    /// <summary>把用户选择的图片复制到 cardbg\{key}{ext}（覆盖同键旧图），返回新路径。</summary>
    public static string SaveImage(string key, Stream source, string ext)
    {
        Directory.CreateDirectory(ImageDir);
        ext = ext.ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp")) ext = ".png";

        // 清掉同键旧图（可能扩展名不同）
        foreach (var old in Directory.EnumerateFiles(ImageDir, key + ".*"))
            File.Delete(old);

        var dest = Path.Combine(ImageDir, key + ext);
        using (var fs = File.Create(dest))
            source.CopyTo(fs);
        return dest;
    }
}
