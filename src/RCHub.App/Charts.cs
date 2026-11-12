using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RCHub.Core;
namespace RCHub.App;
public sealed class StorageRing : Control
{
    public IReadOnlyList<StorageCategory> Categories { get; set; }=[];
    public event Action<int>? CategoryClicked;
    public override void Render(DrawingContext context)
    {
        base.Render(context);var center=new Point(Bounds.Width/2,Bounds.Height/2);var radius=Math.Min(Bounds.Width,Bounds.Height)*.45;
        var total=Categories.Sum(x=>x.Bytes);double start=-Math.PI/2;
        if(total==0){context.DrawEllipse(null,new Pen(Brush.Parse("#E2E8F0"),30),center,radius-15,radius-15);return;}
        foreach(var category in Categories.Where(x=>x.Bytes>0))
        {
            var angle=category.Bytes/(double)total*Math.PI*2;var end=start+angle;
            var geometry=new StreamGeometry();using(var g=geometry.Open())
            {
                var p1=new Point(center.X+radius*Math.Cos(start),center.Y+radius*Math.Sin(start));
                var p2=new Point(center.X+radius*Math.Cos(end-0.00001),center.Y+radius*Math.Sin(end-0.00001));
                g.BeginFigure(center,true);g.LineTo(p1);g.ArcTo(p2,new Size(radius,radius),0,angle>Math.PI,SweepDirection.Clockwise);g.LineTo(center);g.EndFigure(true);
            }
            context.DrawGeometry(Brush.Parse(category.Color),null,geometry);start=end;
        }
        context.DrawEllipse(Brush.Parse("#FFFFFF"),null,center,radius*.63,radius*.63);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);var p=e.GetPosition(this)-new Point(Bounds.Width/2,Bounds.Height/2);var angle=(Math.Atan2(p.Y,p.X)+Math.PI/2+Math.PI*2)%(Math.PI*2);var total=Categories.Sum(x=>x.Bytes);double acc=0;
        for(int i=0;i<Categories.Count;i++){acc+=Categories[i].Bytes/(double)Math.Max(1,total)*Math.PI*2;if(angle<=acc){CategoryClicked?.Invoke(i);break;}}
    }
}
