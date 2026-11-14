using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using RCHub.Core;

namespace RCHub.App;

public sealed partial class MainView
{
    private ContextMenu? _openContext;
    private void ContextPointerReleased(object? sender,PointerReleasedEventArgs args)
    {
        if(args.InitialPressMouseButton!=MouseButton.Right || args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        // DataGrid can capture/handle pointer input before the normal ContextRequested gesture.
        // Resolve the row under the pointer, including all columns and their blank padding.
        if(OpenContextAt(this.InputHitTest(args.GetPosition(this)) as Visual,false))args.Handled=true;
    }
    private void ContextKeyReleased(object? sender,KeyEventArgs args)
    {
        if(args.Key!=Key.Apps && !(args.Key==Key.F10 && args.KeyModifiers.HasFlag(KeyModifiers.Shift)))return;
        if(OpenContextAt(args.Source as Visual,true))args.Handled=true;
    }
    private void ContextRequestedFallback(object? sender,ContextRequestedEventArgs args)
    {
        if(args.Handled)return;
        var pointer=args.TryGetPosition(this,out var position);
        var target=pointer?this.InputHitTest(position) as Visual:args.Source as Visual;
        if(OpenContextAt(target,!pointer))args.Handled=true;
    }
    private bool OpenContextAt(Visual? visual,bool keyboard)
    {
        if(visual==null || _modal!=null)return false;
        var chain=visual.GetSelfAndVisualAncestors().OfType<Control>().ToArray();
        foreach(var control in chain)
        {
            var file=control.Tag as CloudEntry ?? (control is DataGridRow?control.DataContext as CloudEntry:null);
            if(file!=null)
            {
                _selected=file;
                var grid=chain.OfType<DataGrid>().FirstOrDefault();
                if(grid!=null)grid.SelectedItem=file;
                OpenContextMenu(control,FileMenu(file),keyboard);return true;
            }
            if(control.Tag is Account account)
            {
                OpenContextMenu(control,new ContextMenu{ItemsSource=new[]{Menu("Настройки аккаунта",()=>AccountSettings(account)),Menu("Отключить…",()=>Disconnect(account))}},keyboard);return true;
            }
        }
        if(keyboard && chain.OfType<DataGrid>().FirstOrDefault() is {} table && table.SelectedItem is CloudEntry selected)
        {
            _selected=selected;OpenContextMenu(table,FileMenu(selected),true);return true;
        }
        return false;
    }
    private void OpenContextMenu(Control anchor,ContextMenu menu,bool keyboard=false)
    {
        _openContext?.Close();_openContext=menu;
        menu.Placement=keyboard?PlacementMode.Bottom:PlacementMode.Pointer;
        anchor.ContextMenu=menu;
        menu.Closed+=(_,_)=>{if(ReferenceEquals(_openContext,menu))_openContext=null;};
        menu.Open(anchor);
    }
}
