using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Shape= Avalonia.Controls.Shapes.Path;
namespace RCHub.App;
internal static class ServiceMark
{
    public static Control Create(string service,double size)
    {
        var c=new Canvas{Width=24,Height=24};
        void Part(string path,string color)=>c.Children.Add(new Shape{Data=Geometry.Parse(path),Fill=Brush.Parse(color)});
        if(service.Contains("Google")){
            Part("M8 2H15L6 18H0Z","#0F9D58");Part("M8 2H16L24 16H16Z","#F9BC15");Part("M0 17H24L20 23H4Z","#4285F4");
        }else if(service.Contains("Dropbox")){
            Part("M0 6L6 2L12 6L6 10ZM12 6L18 2L24 6L18 10ZM0 14L6 10L12 14L6 18ZM12 14L18 10L24 14L18 18ZM6 20L12 16L18 20L12 24Z","#2166EA");
        }else if(service.Contains("MEGA")){
            Part("M12 0A12 12 0 1 0 12.01 0Z","#CD2940");Part("M5 17V7H8L12 12L16 7H19V17H16V11L12 16L8 11V17Z","White");
        }else if(service.Contains("Яндекс") || service.Contains("OneDrive") || service.Contains("iCloud") || service.Contains("Mail")){
            Part("M5 20C-1 20 -1 12 4 11C4 3 13 2 16 8C24 6 28 20 19 20Z",service.Contains("iCloud")?"#59BDF8":"#247FDE");
            Part("M2 16L13 10L21 14L9 20H5Q2 20 2 16Z",service.Contains("Яндекс")?"#79C8FF":"#388FDD");
            if(service.Contains("Mail"))c.Children.Add(new TextBlock{Text="@",FontSize=17,Foreground=Brushes.Orange,FontWeight=FontWeight.Bold});
        }else{
            Part("M2 3H22V10H2ZM2 13H22V20H2Z","#416981");Part("M5 5H8V8H5ZM5 15H8V18H5Z","#95E4C8");
        }
        return new Viewbox{Width=size,Height=size,Child=c};
    }
}
