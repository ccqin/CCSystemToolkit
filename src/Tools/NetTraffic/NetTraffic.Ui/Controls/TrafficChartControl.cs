using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace NetTraffic.Ui.Controls;

/// <summary>折线图上的一个采样点（10/5 秒周期增量）。</summary>
public sealed record TrafficPoint(DateTime Ts, long Down, long Up);

/// <summary>
/// 流量折线图（自绘，无第三方依赖）：绿色=下行、橙色=上行；
/// 左侧/底部坐标轴、横向网格线，鼠标悬停显示最近采样点的时间与数值。
/// </summary>
public sealed class TrafficChartControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<TrafficPoint>?> PointsProperty =
        AvaloniaProperty.Register<TrafficChartControl, IReadOnlyList<TrafficPoint>?>(nameof(Points));

    public IReadOnlyList<TrafficPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    private int _hoverIndex = -1;

    static TrafficChartControl() => AffectsRender<TrafficChartControl>(PointsProperty);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pts = Points;
        if (pts is not { Count: > 1 }) return;
        var area = PlotArea();
        var x = e.GetPosition(this).X;
        if (x < area.left || x > area.left + area.width)
        {
            if (_hoverIndex != -1) { _hoverIndex = -1; InvalidateVisual(); }
            return;
        }
        // 最近点的索引（横轴按索引等距——采样周期固定）
        var idx = (int)Math.Round((x - area.left) / area.width * (pts.Count - 1));
        idx = Math.Clamp(idx, 0, pts.Count - 1);
        if (idx != _hoverIndex) { _hoverIndex = idx; InvalidateVisual(); }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverIndex != -1) { _hoverIndex = -1; InvalidateVisual(); }
    }

    private (double left, double top, double width, double height) PlotArea() =>
        (58, 14, Math.Max(10, Bounds.Width - 58 - 12), Math.Max(10, Bounds.Height - 14 - 26));

    private static string FmtBytes(long b) => b switch
    {
        >= 1 << 20 => (b / 1024.0 / 1024.0).ToString("F1", CultureInfo.CurrentCulture) + "MB",
        >= 1 << 10 => (b / 1024.0).ToString("F0", CultureInfo.CurrentCulture) + "KB",
        _ => b + "B",
    };

    public override void Render(DrawingContext ctx)
    {
        var (l, t, w, h) = PlotArea();
        var bg = this.TryFindResource("CardBackgroundFillColorDefaultBrush", out var bgv) && bgv is IBrush b ? b : null;
        ctx.DrawRectangle(bg ?? Brushes.Transparent, null, new Rect(0, 0, Bounds.Width, Bounds.Height));

        var pts = Points;
        var axisPen = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80)), 1);
        var fg = this.TryFindResource("TextFillColorSecondaryBrush", out var fgv) && fgv is IBrush f ? f : Brushes.Gray;
        var typeface = new Typeface(FontFamily.Parse("Cascadia Mono,Consolas,Courier New"));
        void Text(string s, double x, double y, IBrush brush, double size = 10.5, bool right = false)
        {
            var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);
            ctx.DrawText(ft, new Point(right ? x - ft.Width : x, y));
        }

        if (pts is not { Count: > 1 })
        {
            var tip = new FormattedText("该时段没有采样点", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, 13, fg);
            ctx.DrawText(tip, new Point((Bounds.Width - tip.Width) / 2, Bounds.Height / 2 - 8));
            return;
        }

        var max = Math.Max(1L, pts.Max(p => Math.Max(p.Down, p.Up)));

        // 横向网格 + 纵轴刻度（5 档）
        for (var i = 0; i <= 4; i++)
        {
            var y = t + h - h * i / 4.0;
            ctx.DrawLine(axisPen, new Point(l, y), new Point(l + w, y));
            Text(FmtBytes((long)(max * i / 4.0)), l - 6, y - 7, fg, 10, right: true);
        }
        // 横轴时间（首/中/尾）
        Text(pts[0].Ts.ToString("HH:mm:ss"), l, t + h + 6, fg);
        var mid = pts[pts.Count / 2].Ts.ToString("HH:mm:ss");
        Text(mid, l + w / 2, t + h + 6, fg);
        Text(pts[^1].Ts.ToString("HH:mm:ss"), l + w, t + h + 6, fg, 10.5, right: true);

        // 折线：下行=亮蓝实线 / 上行=亮橙虚线（蓝橙是色盲安全高对比组合，线型再双保险）
        var downPen = new Pen(new SolidColorBrush(Color.Parse("#0078D4")), 1.8);
        var upPen = new Pen(new SolidColorBrush(Color.Parse("#FF8C00")), 1.8)
        {
            DashStyle = DashStyle.Dash,
        };
        DrawLine(ctx, pts, p => p.Down, downPen, l, t, w, h, max);
        DrawLine(ctx, pts, p => p.Up, upPen, l, t, w, h, max);

        // 图例（右上角）：与曲线同色同线型
        var lx = l + w - 230;
        ctx.DrawLine(downPen, new Point(lx, t + 4), new Point(lx + 18, t + 4));
        Text("下行(下载)", lx + 22, t - 2, fg);
        ctx.DrawLine(upPen, new Point(lx + 100, t + 4), new Point(lx + 118, t + 4));
        Text("上行(上传)", lx + 122, t - 2, fg);

        // 悬浮读值：竖线 + 顶部文本
        if (_hoverIndex >= 0 && _hoverIndex < pts.Count)
        {
            var p = pts[_hoverIndex];
            var x = l + w * _hoverIndex / (double)(pts.Count - 1);
            ctx.DrawLine(new Pen(Brushes.Gray, 1) { DashStyle = DashStyle.Dash },
                new Point(x, t), new Point(x, t + h));
            var label = $"{p.Ts:HH:mm:ss}  ⬇{FmtBytes(p.Down)}  ⬆{FmtBytes(p.Up)}";
            var ft = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11.5, fg);
            var tx = Math.Clamp(x - ft.Width / 2, l, l + w - ft.Width);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xE0, 0x33, 0x33, 0x33)),
                null, new Rect(tx - 4, t - 1, ft.Width + 8, ft.Height + 2));
            ctx.DrawText(ft, new Point(tx, t));
        }
    }

    private static void DrawLine(DrawingContext ctx, IReadOnlyList<TrafficPoint> pts,
        Func<TrafficPoint, long> value, Pen pen,
        double l, double t, double w, double h, long max)
    {
        var geo = new StreamGeometry();
        using (var s = geo.Open())
        {
            for (var i = 0; i < pts.Count; i++)
            {
                var x = l + w * i / (double)(pts.Count - 1);
                var y = t + h - h * value(pts[i]) / (double)max;
                if (i == 0) s.BeginFigure(new Point(x, y), false);
                else s.LineTo(new Point(x, y));
            }
        }
        ctx.DrawGeometry(null, pen, geo);
    }
}
