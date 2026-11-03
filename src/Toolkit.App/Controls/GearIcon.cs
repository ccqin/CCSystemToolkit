using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;

namespace Toolkit.App.Controls;

/// <summary>
/// 自绘齿轮图标：8 齿 + 外环 + 中心孔，几何以画布中心完全对称。
/// 替代字体字形（Segoe 图标字形受基线排版影响会偏左上，无法稳定居中）。
/// </summary>
public class GearIcon : Control
{
    public static readonly StyledProperty<IBrush?> IconBrushProperty =
        AvaloniaProperty.Register<GearIcon, IBrush?>(nameof(IconBrush), Brushes.White);

    /// <summary>图标填充画刷（默认白色）。</summary>
    public IBrush? IconBrush
    {
        get => GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    /// <summary>自定义 AutomationPeer：向 UIA 暴露语义名称，便于自动化工具定位。</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new GearIconPeer(this);

    private sealed class GearIconPeer(Control owner) : ControlAutomationPeer(owner)
    {
        protected override string GetNameCore() => "齿轮";
    }

    public override void Render(DrawingContext context)
    {
        if (IconBrush is not { } brush) return;
        var s = Bounds.Size.Width;                 // 假定正方形画布
        var c = new Point(s / 2, s / 2);
        double outer = s * 0.50;                   // 齿顶半径
        double pitch = s * 0.395;                  // 齿根半径（环外径）
        double hole = s * 0.165;                   // 中心孔半径
        double tooth = s * 0.135;                  // 齿宽

        for (int i = 0; i < 8; i++)
        {
            double rad = i * Math.PI / 4;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            // 齿在正上方时的四角（相对中心），旋转到目标角度
            var pts = new[]
            {
                new Point(-tooth / 2, -outer),
                new Point(tooth / 2, -outer),
                new Point(tooth / 2, -pitch + 0.6),   // 与环少量重叠，消除抗锯齿缝隙
                new Point(-tooth / 2, -pitch + 0.6),
            };

            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(Rotate(pts[0], cos, sin, c), true);
                g.LineTo(Rotate(pts[1], cos, sin, c));
                g.LineTo(Rotate(pts[2], cos, sin, c));
                g.LineTo(Rotate(pts[3], cos, sin, c));
                g.EndFigure(true);
            }
            context.DrawGeometry(brush, null, geo);
        }

        var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(new Rect(c.X - pitch, c.Y - pitch, pitch * 2, pitch * 2)),
            new EllipseGeometry(new Rect(c.X - hole, c.Y - hole, hole * 2, hole * 2)));
        context.DrawGeometry(brush, null, ring);
    }

    private static Point Rotate(Point p, double cos, double sin, Point center)
    {
        double x = center.X + p.X * cos - p.Y * sin;
        double y = center.Y + p.X * sin + p.Y * cos;
        return new Point(x, y);
    }
}
