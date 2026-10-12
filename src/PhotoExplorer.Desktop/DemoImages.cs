using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoExplorer.Desktop;

/// <summary>Original, deterministic landscape illustrations for an offline demonstration.</summary>
public static class DemoImages
{
    private static readonly string[] Names = ["01_Тихая_бухта", "02_Пески_вечера", "03_Северный_хребет", "04_Туманное_озеро", "05_Линия_прибоя", "06_Золотая_долина", "07_За_перевалом", "08_Сосновый_берег", "09_Розовый_рассвет", "10_Лунные_дюны", "11_Зимний_воздух", "12_Последний_свет"];
    private static readonly string[][] Palettes =
    [
        ["#BCD1D5", "#E9DAC5", "#65858C", "#344F58", "#203B42"],
        ["#D6BCA6", "#F4DBA6", "#C69A6F", "#AE7352", "#714E42"],
        ["#A3B6C9", "#D8E0E3", "#798F9E", "#445B6D", "#293C4D"],
        ["#C4CFC0", "#E3DEBE", "#8DAD9A", "#4B786E", "#2B504D"],
        ["#AFCDD5", "#F0E8D7", "#8BA9A6", "#527D81", "#2B5B63"],
        ["#CFCCAC", "#F3E5B5", "#B4A070", "#857B54", "#4F5A40"],
        ["#B5B8CA", "#E8D9D3", "#8B90A8", "#595F7C", "#333F59"],
        ["#C0CDC5", "#E1D7B8", "#829E8E", "#3F7063", "#204F47"],
        ["#CDB8B7", "#F0D5BD", "#B8958D", "#866F7D", "#554E65"],
        ["#8399AD", "#C6CED0", "#8F9B9C", "#667981", "#394F61"],
        ["#BED1DA", "#F2EDE2", "#A6B7BC", "#6A8893", "#375663"],
        ["#B6AEA9", "#F1CFAB", "#B19478", "#786959", "#474A45"]
    ];
    public static string Ensure(string folder)
    {
        Directory.CreateDirectory(folder);
        for (var i = 0; i < Names.Length; i++)
        {
            var path = Path.Combine(folder, Names[i] + ".png");
            if (!File.Exists(path)) Render(path, i);
        }
        return folder;
    }
    private static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private static void Render(string path, int index)
    {
        const int width = 1200, height = 800;
        var palette = Palettes[index];
        var random = new Random(index * 193 + 42);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush((Color)ColorConverter.ConvertFromString(palette[0]), (Color)ColorConverter.ConvertFromString(palette[1]), 90), null, new Rect(0, 0, width, height));
            dc.DrawEllipse(Brush(palette[1]), null, new Point(780 - index % 4 * 155, 190 + index % 3 * 30), 46, 46);
            for (var layer = 0; layer < 3; layer++)
            {
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(new Point(-30, height), true, true);
                    var baseline = 360 + layer * 115;
                    context.LineTo(new Point(-30, baseline), true, false);
                    if (index % 3 == 1)
                    {
                        for (var x = 0; x < width + 300; x += 300)
                            context.BezierTo(new Point(x + 30, baseline - 180 + random.Next(80)), new Point(x + 130, baseline - 40), new Point(x + 300, baseline + random.Next(-80, 70)), true, false);
                    }
                    else
                    {
                        for (var x = 0; x <= width + 100; x += 100)
                            context.LineTo(new Point(x, baseline + random.Next(-145, 85)), true, false);
                    }
                    context.LineTo(new Point(width + 300, height), true, false);
                }
                dc.DrawGeometry(Brush(palette[layer + 2]), null, geometry);
            }
            if (index % 3 == 0 || index % 3 == 2)
            {
                var waterY = index % 3 == 0 ? 535 : 670;
                dc.DrawRectangle(new LinearGradientBrush((Color)ColorConverter.ConvertFromString(palette[2]), (Color)ColorConverter.ConvertFromString(palette[4]), 90), null, new Rect(0, waterY, width, height - waterY));
                for (var j = 0; j < 85; j++)
                {
                    var x = random.Next(width); var y = random.Next(waterY + 8, height);
                    dc.PushOpacity(.15 + random.NextDouble() * .2);
                    dc.DrawLine(new Pen(Brush(palette[1]), 1), new Point(x, y), new Point(x + random.Next(15, 110), y));
                    dc.Pop();
                }
            }
            if (index is 3 or 7)
            {
                for (var tree = 0; tree < 16; tree++)
                {
                    var x = tree * 39; var y = 680 + random.Next(65); var treeHeight = 80 + random.Next(130);
                    var geometry = Geometry.Parse($"M {x},{y - treeHeight} L {x - 34},{y} L {x + 34},{y} Z");
                    dc.DrawGeometry(Brush(palette[4]), null, geometry);
                }
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
