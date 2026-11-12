// Original Hoshino Island code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DIsland.Interop;
using DIsland.Services;
namespace DIsland;

// Vector geometry from the user's approved Hoshino design. Eyes move independently of the hair.
public sealed class HoshinoCompanion : Grid
{
    private readonly AppSettings settings;
    private readonly Canvas face = new() { Width=120, Height=120 };
    private readonly TranslateTransform gaze = new();
    private readonly ScaleTransform eyelids = new(1,1,60,77);
    private readonly TranslateTransform bounce=new();
    private readonly RotateTransform sway = new(0,60,72);
    private readonly DispatcherTimer timer = new() { Interval=TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch time=Stopwatch.StartNew();
    private double last;
    public bool Interactive { get; set; }
    public string Reaction { get; private set; } = "normal";
    private int reactionStep;
    private readonly Canvas cheeks=new() { Opacity=0 };
    private readonly Path leftLid=new() { Fill=(Brush)new BrushConverter().ConvertFromString("#FFF0E9")! };
    private readonly Path rightLid=new() { Fill=(Brush)new BrushConverter().ConvertFromString("#FFF0E9")! };
    public void React()
    {
        Reaction=new[]{"blush","angry","normal"}[reactionStep++%3];
        UpdateState();
    }

    public Func<bool>? Playing { get; set; }
    public Func<bool>? Listening { get; set; }
    public Func<bool>? Working { get; set; }
    public string State { get; private set; }="neutral";
    public Point GazeOffset => new(gaze.X,gaze.Y);
    public HoshinoCompanion(AppSettings settings)
    {
        this.settings=settings;
        ToolTip="Таканаши Хошино";
        AddPath("M55 24C49 11 61 1 72 7c6 3 7 8 4 12-1-6-6-8-11-6-5 2-6 7-4 11C82 21 99 36 101 56l10 8-10 1 6 7-9-1c1 15 2 24 8 35l-10-4 2 12-11-8-2 10c-8-8-11-18-12-29H47c-1 11-4 21-12 29l-2-10-11 8 2-12-10 4c6-11 7-20 8-35l-9 1 6-7-10-1 10-8C21 36 34 24 55 24Z", "#EAB0CF", 1);
        AddPath("M30 57C30 36 90 36 90 57V71C90 87 78 97 60 98 42 97 30 87 30 71Z", "#FFF0E9", 1);
        AddPath("M23 59C25 34 38 21 60 24 82 21 98 37 98 59L104 65C94 64 89 59 85 54c4 9 3 15-2 19-2-8-3-16-7-23 0 12-6 19-15 24 4-13 1-23-2-32-1 14-5 23-12 28 1-13-1-21-4-27-1 12-5 21-13 26l3-13c-4 4-9 7-15 8Z", "#EAB0CF", 1);
        AddPath("M29 72c-1 14-2 26-5 31 7-6 10-17 11-30ZM91 72c1 14 2 26 5 31-7-6-10-17-11-30Z", "#D795BC", .8);
        var eyes=new Canvas();
        foreach(var (x,color) in new[]{(47d,"#F2D17D"),(73d,"#83CFF3")})
        {
            var eye=new Ellipse { Width=11.6,Height=11,Fill=(Brush)new BrushConverter().ConvertFromString(color)! };
            Canvas.SetLeft(eye,x-5.8);Canvas.SetTop(eye,71.5);eyes.Children.Add(eye);
        }
        var transforms=new TransformGroup();transforms.Children.Add(eyelids);transforms.Children.Add(gaze);eyes.RenderTransform=transforms;
        face.Children.Add(eyes);var bodyTransforms=new TransformGroup();bodyTransforms.Children.Add(sway);bodyTransforms.Children.Add(bounce);face.RenderTransform=bodyTransforms;
        foreach(var cx in new[]{35d,85d}) {
            var blush=new Ellipse {Width=14,Height=7,Fill=(Brush)new BrushConverter().ConvertFromString("#EC81A3")!};Canvas.SetLeft(blush,cx-7);Canvas.SetTop(blush,82);cheeks.Children.Add(blush);
        }
        face.Children.Add(cheeks);face.Children.Add(leftLid);face.Children.Add(rightLid);
        Background=Brushes.Transparent;
        System.Windows.Automation.AutomationProperties.SetName(this,"Хошино — нажми, чтобы сменить настроение");
        Focusable=true;
        MouseLeftButtonDown+=(_,e)=>{if(Interactive){Focus();e.Handled=true;}};
        MouseLeftButtonUp+=(_,e)=>{if(!Interactive)return;React();e.Handled=true;};
        PreviewKeyDown+=(_,e)=>{if(Interactive&&(e.Key==System.Windows.Input.Key.Enter||e.Key==System.Windows.Input.Key.Space)){React();e.Handled=true;}};
        MouseEnter+=(_,_)=>{if(Interactive)Cursor=System.Windows.Input.Cursors.Hand;};
        Children.Add(new Viewbox { Child=face, Stretch=Stretch.Uniform });
        timer.Tick+=(_,_)=>UpdateState();
        IsVisibleChanged+=(_,_)=> { if(IsVisible) {last=time.Elapsed.TotalSeconds;timer.Start();} else timer.Stop(); };
        Loaded+=(_,_)=> {last=time.Elapsed.TotalSeconds;if(IsVisible)timer.Start();};Unloaded+=(_,_)=>timer.Stop();
    }
    private void AddPath(string data,string fill,double opacity)
    {
        var geometry=Geometry.Parse(data); geometry.Freeze();
        face.Children.Add(new Path { Data=geometry,Fill=(Brush)new BrushConverter().ConvertFromString(fill)!,Opacity=opacity });
    }
    public void UpdateState()
    {
        var now=time.Elapsed.TotalSeconds;var dt=Math.Clamp(now-last,0,.1);last=now;
        var still=settings.ReduceMotion || !settings.AnimateHoshino;
        double x=0,y=0,lid=1,angle=0;
        State="neutral";
        if(Listening?.Invoke()==true) { State="listening";lid=1.25; }
        else if(!still)
        {
            if(Playing?.Invoke()==true) { angle=Math.Sin(now*3)*4; State=angle<0?"dance-left":"dance-right"; }
            else if(Working?.Invoke()==true) { x=Math.Sin(now*1.4)*2;State=x<0?"look-left":"look-right"; }
            if(settings.HoshinoFollowsCursor && IsLoaded && IsVisible && ActualWidth>0 && NativeMethods.GetCursorPos(out var cursor))
            {
                var center=PointToScreen(new Point(ActualWidth/2,ActualHeight*.64));
                x=Math.Clamp((cursor.X-center.X)/100,-1,1)*3;
                y=Math.Clamp((cursor.Y-center.Y)/180,-1,1)*1.8;
            }
            // Short soft blink, independent of the cursor and frame rate.
            var phase=now%5.4;
            if(phase>5.08) { lid=.12+.88*Math.Abs((phase-5.24)/.16); if(State=="neutral")State="sleepy"; }
        }
        if(Interactive) {
            if(Reaction=="blush") {State="blush";lid=.6;}
            else if(Reaction=="angry") {State="angry";lid=1; x=0;y=0;}
            else if(!still) { lid=.55; }
            if(!still && Playing?.Invoke()==true)angle*=1.8;
            cheeks.Opacity+=((Reaction=="blush"?.85:0)-cheeks.Opacity)*(still?1:1-Math.Exp(-dt/.12));
            leftLid.Data=Reaction=="angry"?Geometry.Parse("M40 70 L54 78 L54 70 Z"):Geometry.Empty;
            rightLid.Data=Reaction=="angry"?Geometry.Parse("M66 70 L66 78 L80 70 Z"):Geometry.Empty;
        }
        var blend=still?1:1-Math.Exp(-dt/0.11);
        gaze.X+=(x-gaze.X)*blend;gaze.Y+=(y-gaze.Y)*blend;
        eyelids.ScaleY+=(lid-eyelids.ScaleY)*blend;sway.Angle+=(angle-sway.Angle)*blend;
        var hop=Interactive&&!still&&Playing?.Invoke()==true ? -Math.Abs(Math.Sin(now*3))*3:0;bounce.Y+=(hop-bounce.Y)*blend;
    }
}
