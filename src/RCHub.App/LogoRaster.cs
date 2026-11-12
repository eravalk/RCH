using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
namespace RCHub.App;
public static class LogoRaster
{
    // Vector app mark. The approved calligraphic final asset is retained separately in Assets.
    public static Control Create(double size=40)
    {
        var panel=new Canvas {Width=100,Height=90};
        panel.Children.Add(new Path {Fill=Brushes.White,Data=Geometry.Parse("M20 72 C-3 72 -4 43 15 39 C14 22 30 14 42 21 C51 0 80 10 82 31 C104 34 104 72 83 72 Z")});
        panel.Children.Add(new Path {Fill=Brushes.Black,Data=Geometry.Parse("M21 45 C46 21 86 25 74 44 C70 50 59 53 54 54 L83 68 L68 66 L44 53 L32 68 L40 38 L49 35 L43 50 C75 46 76 30 50 35 Z")});
        return new Viewbox {Width=size,Height=size,Child=panel,Stretch=Stretch.Uniform};
    }
    public static WindowIcon WindowMark()
    {
        using var stream=Avalonia.Platform.AssetLoader.Open(new Uri("avares://RCHub.App/Assets/RCH.ico"));
        return new WindowIcon(stream);
    }
    public static Stream CreateIcon()
    {
        var control=Create(64);control.Measure(new Size(64,64));control.Arrange(new Rect(0,0,64,64));
        using var bitmap=new RenderTargetBitmap(new PixelSize(64,64),new Vector(96,96));bitmap.Render(control);var stream=new MemoryStream();bitmap.Save(stream);stream.Position=0;return stream;
    }
}
