using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RCHub.Core;
using Shape=Avalonia.Controls.Shapes.Path;
namespace RCHub.App;
public static class FileSymbol
{
    public static Control Create(CloudEntry file,double size)
    {
        var spec=FileIcons.For(file.Name,file.IsFolder);var canvas=new Canvas{Width=48,Height=48};
        void Fill(string path,string color,double opacity=1)=>canvas.Children.Add(new Shape{Data=Geometry.Parse(path),Fill=Brush.Parse(color),Opacity=opacity});
        if(file.IsFolder) {
            Fill("M4 12Q4 8 8 8H20L25 13H40Q44 13 44 17V36Q44 40 40 40H8Q4 40 4 36Z","#C18C32");
            Fill("M4 18H44L41 37Q40 40 37 40H8Q5 40 5 37Z",spec.Color);
        }else {
            Fill("M11 3H29L39 13V41Q39 45 35 45H11Q7 45 7 41V7Q7 3 11 3Z",spec.Color);
            Fill("M29 3V10Q29 13 32 13H39Z","White",.3);
            canvas.Children.Add(new Shape{Data=Geometry.Parse(spec.Symbol),Stroke=Brushes.White,StrokeThickness=2,StrokeLineCap=PenLineCap.Round,StrokeJoin=PenLineJoin.Round});
        }
        var view=new Viewbox{Width=size,Height=size,Child=canvas};ToolTip.SetTip(view,spec.Title);return view;
    }
}
