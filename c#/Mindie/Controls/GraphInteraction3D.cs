using Mindie.Services;
using System.Windows.Media.Media3D;

namespace Mindie.Controls;

public sealed partial class KnowledgeGraphCanvas
{
    private bool _pointerDown,_dragging,_cameraMoved,_panGesture;
    private Point _pointerStart;
    private Point3D _nodeStart,_targetStart;
    private Vector3D _dragNormal,_grabOffset;
    private double _yawStart,_pitchStart;
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.ChangedButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle)) return;
        Focus(); var point=e.GetPosition(this); var node=Hit(point);
        if(e.ChangedButton==MouseButton.Left && e.ClickCount>1 && node?.IsMemory==true) { Select(node.Id); OpenRequested?.Invoke(this,node); e.Handled=true; return; }
        BeginPointer(point,e.ChangedButton!=MouseButton.Left || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); CaptureMouse(); e.Handled=true;
    }
    internal void BeginPointer(Point point,bool forcePan=false)
    {
        _pointerDown=true; _pointerStart=point; _targetStart=_target; _yawStart=_yaw; _pitchStart=_pitch;
        _dragging=false; _cameraMoved=false; _panGesture=forcePan || PanMode; _pending=forcePan?null:Hit(point);
        if(_pending is not null) {
            _nodeStart=_pending.SpatialPosition; _dragNormal=Basis().Forward; _grabOffset=_nodeStart-OnPlane(point,_nodeStart,_dragNormal); Select(_pending.Id);
        }
        RefreshHighlights();
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); MovePointer(e.GetPosition(this)); }
    internal void MovePointer(Point point)
    {
        if(_pointerDown)
        {
            var delta=point-_pointerStart; if(delta.Length<4 && !_dragging && !_cameraMoved) return;
            if(_pending?.IsMemory==true) {
                _dragging=true; _pending.SpatialPosition=Clamp(OnPlane(point,_nodeStart,_dragNormal)+_grabOffset); RefreshHighlights();
            }
            else {
                _cameraMoved=true;
                if(_panGesture) {
                    var basis=Basis(); var unit=2*(_fitDistance/_zoom)*Math.Tan(_camera.FieldOfView*Math.PI/360)/Math.Max(1,ActualWidth);
                    _target=_targetStart-basis.Right*(delta.X*unit)+basis.Up*(delta.Y*unit);
                }
                else { _yaw=(_yawStart-delta.X*.45)%360; _pitch=Math.Clamp(_pitchStart+delta.Y*.45,-85,85); }
                UpdateCamera();
            }
            return;
        }
        var hover=Hit(point); if(hover==_hovered) return; _hovered=hover; ToolTip=hover?.Label; Cursor=hover is null?PanMode?Cursors.SizeAll:Cursors.Hand:Cursors.Hand; RefreshHighlights();
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e); if(!_pointerDown) return; EndPointer(); if(IsMouseCaptured) ReleaseMouseCapture(); e.Handled=true;
    }
    internal void EndPointer()
    {
        if(!_pointerDown) return; var moved=_dragging?_pending:null; var clear=_pending is null && !_cameraMoved;
        _pointerDown=false; _dragging=false; _pending=null; _cameraMoved=false;
        if(clear) Select(null); if(moved is not null) PositionCommitted?.Invoke(this,moved); RefreshHighlights();
    }
    private void CancelPointer()
    {
        if(_dragging && _pending is not null) _pending.SpatialPosition=_nodeStart;
        if(_pointerDown && _cameraMoved) { _target=_targetStart; _yaw=_yawStart; _pitch=_pitchStart; UpdateCamera(); }
        _pointerDown=false; _dragging=false; _pending=null; _cameraMoved=false; if(IsMouseCaptured) ReleaseMouseCapture();
        if(_labels is not null) RefreshHighlights();
    }
    private static Point3D Clamp(Point3D p)=>new(Math.Clamp(p.X,-5.5,5.5),Math.Clamp(p.Y,-4,4),Math.Clamp(p.Z,-3.8,3.8));
    public void NudgeSelected(Vector delta)
    {
        var basis=Basis(); NudgeSpatialSelected(basis.Right*(delta.X*.006)-basis.Up*(delta.Y*.006));
    }
    public void NudgeSpatialSelected(Vector3D delta)
    {
        if(_selected?.IsMemory!=true) return;
        _selected.SpatialPosition=Clamp(_selected.SpatialPosition+delta); PositionCommitted?.Invoke(this,_selected); RefreshHighlights();
    }
    public void NudgeDepth(double amount)=>NudgeSpatialSelected(-Basis().Forward*amount);
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); if(!_pointerDown) { _hovered=null; RefreshHighlights(); } }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && _selected?.IsMemory==true) NudgeDepth(e.Delta>0 ? .18 : -.18);
        else ChangeZoom(e.Delta>0?1.12:1/1.12); e.Handled=true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.Key==Key.Escape) { CancelPointer(); Select(null); e.Handled=true; return; }
        if(e.Key==Key.Enter && _selected?.IsMemory==true) { OpenRequested?.Invoke(this,_selected); e.Handled=true; return; }
        if(e.Key is Key.Add or Key.OemPlus) { ChangeZoom(1.15); e.Handled=true; return; }
        if(e.Key is Key.Subtract or Key.OemMinus) { ChangeZoom(1/1.15); e.Handled=true; return; }
        if(e.Key==Key.Home) { Fit(); e.Handled=true; return; }
        if((e.Key is Key.PageUp or Key.PageDown) && _selected?.IsMemory==true) { NudgeDepth(e.Key==Key.PageUp ? .18 : -.18); e.Handled=true; return; }
        if(e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) NudgeSelected(e.Key switch { Key.Left=>new(-25,0),Key.Right=>new(25,0),Key.Up=>new(0,-25),_=>new(0,25) });
        else {
            var nodes=Graph.Nodes.Where(n=>n.IsMemory && Matches(n)).ToArray();
            if(nodes.Length>0) { var backwards=e.Key is Key.Left or Key.Up; var index=Array.IndexOf(nodes,_selected); if(index<0) index=backwards?0:-1; Select(nodes[(index+(backwards?-1:1)+nodes.Length)%nodes.Length].Id); }
        }
        e.Handled=true;
    }
}
