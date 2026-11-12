// Copyright 2026 Takayom1. SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace HoshinoInstaller;
public static class Program
{
    [STAThread] public static void Main()=>new Application{ShutdownMode=ShutdownMode.OnMainWindowClose}.Run(new InstallerWindow());
}
public sealed class InstallerWindow:Window
{
    private static readonly Brush Pink=new SolidColorBrush(Color.FromRgb(234,176,207));
    private static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(183,173,190));
    private readonly TextBox folder=new(){Height=36,VerticalContentAlignment=VerticalAlignment.Center,Padding=new Thickness(10,0,10,0)};
    private readonly CheckBox agree=new(){Content="Я принимаю условия лицензий",Margin=new Thickness(0,14,0,0),VerticalContentAlignment=VerticalAlignment.Center};
    private readonly Button install=new(){Content="Поселить Хошино",Height=42,MinWidth=170,Background=Pink,Foreground=new SolidColorBrush(Color.FromRgb(42,28,44))};
    private readonly TextBlock title=new(){Text="Она уже почти рядом",FontSize=26,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)};
    private readonly TextBlock description=new(){Text="Музыка, дела и немного спокойствия.\nТвой остров всегда будет под рукой.",Foreground=Muted,TextWrapping=TextWrapping.Wrap,LineHeight=21,Margin=new Thickness(0,0,0,18)};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Foreground=Muted,Margin=new Thickness(0,10,0,0),LineHeight=20};
    private readonly StackPanel options=new();
    private readonly ProgressBar progress=new(){Height=7,Foreground=Pink,Margin=new Thickness(0,16,0,0),Visibility=Visibility.Collapsed};
    private readonly TextBlock version=new(){Foreground=Muted,FontSize=11};
    private bool installing,complete;
    public static string Version=>Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
    public InstallerWindow()
    {
        Title="Hoshino Island — установка";Width=740;Height=510;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Foreground=Brushes.White;FontFamily=new FontFamily("Segoe UI");FontSize=13;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Resources=CreateStyles();
        var shell=new Border{Background=new SolidColorBrush(Color.FromRgb(26,21,33)),BorderBrush=new SolidColorBrush(Color.FromRgb(72,54,78)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(24),Padding=new Thickness(24)};Content=shell;
        var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});shell.Child=root;
        var bar=new DockPanel{Margin=new Thickness(0,0,0,22)};root.Children.Add(bar);bar.MouseLeftButtonDown+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed)DragMove();};
        var close=new Button{Content="×",FontSize=22,Width=32,Height=32,Padding=new Thickness(0),Background=Brushes.Transparent};DockPanel.SetDock(close,Dock.Right);bar.Children.Add(close);close.Click+=(_,_)=>Close();
        bar.Children.Add(new TextBlock{Text="HOSHINO ISLAND",FontSize=13,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Foreground=Pink});
        var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(198)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});body.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(body,1);root.Children.Add(body);
        var companion=new Border{Background=new SolidColorBrush(Color.FromRgb(43,32,50)),CornerRadius=new CornerRadius(20),Padding=new Thickness(18)};body.Children.Add(companion);
        var companionContent=new StackPanel{VerticalAlignment=VerticalAlignment.Center};companion.Child=companionContent;companionContent.Children.Add(Face());companionContent.Children.Add(new TextBlock{Text="Я всегда тебе помогу",TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Foreground=Muted,Margin=new Thickness(0,20,0,0),FontSize=12});
        var details=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(details,2);body.Children.Add(details);details.Children.Add(title);details.Children.Add(description);details.Children.Add(options);
        options.Children.Add(new TextBlock{Text="Где будет жить остров",Foreground=Muted,FontSize=11,Margin=new Thickness(0,0,0,7)});
        var folderRow=new DockPanel();options.Children.Add(folderRow);var browse=new Button{Content="…",Width=36,Height=36,Margin=new Thickness(6,0,0,0),Padding=new Thickness(0)};DockPanel.SetDock(browse,Dock.Right);folderRow.Children.Add(browse);folderRow.Children.Add(folder);folder.Text=InstallFolder();browse.Click+=(_,_)=>{var dialog=new OpenFolderDialog{Title="Выбери папку для острова"};if(dialog.ShowDialog(this)==true)folder.Text=Path.Combine(dialog.FolderName,"HoshinoIsland");};
        options.Children.Add(agree);var legal=new Button{Content="Прочитать лицензии и авторство",Background=Brushes.Transparent,Foreground=Pink,HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(0,5,0,0),FontSize=11};options.Children.Add(legal);legal.Click+=(_,_)=>ShowLicenses();
        details.Children.Add(progress);details.Children.Add(status);status.Text="При обновлении остров будет закрыт.\nАккаунт, калибровка и история сохранятся.";
        var bottom=new DockPanel{Margin=new Thickness(0,22,0,0)};Grid.SetRow(bottom,2);root.Children.Add(bottom);DockPanel.SetDock(install,Dock.Right);bottom.Children.Add(install);version.Text=Version;version.VerticalAlignment=VerticalAlignment.Center;bottom.Children.Add(version);
        agree.Checked+=(_,_)=>RefreshButton();agree.Unchecked+=(_,_)=>RefreshButton();RefreshButton();install.Click+=async(_,_)=>{if(complete){OpenIsland();Close();return;}await Install();};
        Closing+=(_,e)=>{if(installing)e.Cancel=true;};PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!installing)Close();};
    }
    private void RefreshButton()=>install.IsEnabled=!installing&&(complete||agree.IsChecked==true)&&HasPayload;
    public static bool HasPayload=>Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("Hoshino.CoreSetup.exe");
    private static string InstallFolder()
    {
        using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{7AD6B4D0-35EC-44DC-A0A9-E65887F8BFB5}_is1");
        return key?.GetValue("InstallLocation") as string??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","HoshinoIsland");
    }
    public static async Task ExtractPayload(string path)
    {
        var assembly=Assembly.GetExecutingAssembly();var expected=assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a=>a.Key=="PayloadSha256").Value;
        await using(var input=assembly.GetManifestResourceStream("Hoshino.CoreSetup.exe")??throw new IOException("Не найден пакет приложения"))
        await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))await input.CopyToAsync(output);
        await using var verify=File.OpenRead(path);var actual=Convert.ToHexString(await SHA256.HashDataAsync(verify));if(!string.Equals(actual,expected,StringComparison.OrdinalIgnoreCase))throw new IOException("Пакет повреждён. Скачай установщик заново.");
    }
    private async Task Install()
    {
        if(agree.IsChecked!=true)return;
        string target;
        try{target=Path.GetFullPath(folder.Text.Trim());var protectedFolders=new[]{Path.GetPathRoot(target),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),Environment.GetFolderPath(Environment.SpecialFolder.Windows)};if(protectedFolders.Any(p=>string.Equals(p?.TrimEnd('\\'),target.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)))throw new IOException("Выбери отдельную папку HoshinoIsland.");}
        catch(Exception e){status.Text=e.Message;return;}
        installing=true;RefreshButton();options.Visibility=Visibility.Collapsed;progress.Visibility=Visibility.Visible;progress.IsIndeterminate=true;title.Text="Хошино обустраивается";description.Text="Осталось совсем немного. Можно выдохнуть.";status.Text="Подготавливаю установку…";
        var cacheRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HoshinoIslandInstaller");var temp=Path.Combine(cacheRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);var core=Path.Combine(temp,"setup.exe");
        try{
            await ExtractPayload(core);var logs=Path.Combine(cacheRoot,"Logs");Directory.CreateDirectory(logs);var log=Path.Combine(logs,"setup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".log");
            var start=new ProcessStartInfo(core){UseShellExecute=false,CreateNoWindow=true};foreach(var arg in new[]{"/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/CLOSEAPPLICATIONS","/LANG=russian","/DIR="+target,"/LOG="+log})start.ArgumentList.Add(arg);
            using var child=Process.Start(start)??throw new IOException("Не удалось запустить установку");status.Text="Устанавливаю остров и создаю ярлыки…";await child.WaitForExitAsync();if(child.ExitCode!=0)throw new IOException("Установка не завершилась (код "+child.ExitCode+"). Журнал: "+log);
            if(!File.Exists(Path.Combine(target,"DIsland.exe")))throw new IOException("Не найден установленный остров");folder.Text=target;complete=true;title.Text="Теперь я рядом";description.Text="Остров установлен.\nНачнём с одного небольшого дела?";status.Text="Готово. Твои данные остались на месте.";progress.IsIndeterminate=false;progress.Value=100;install.Content="Открыть остров";
        }catch(Exception e){title.Text="Пока не получилось";description.Text="Попробуем ещё раз, без спешки.";status.Text=e.Message;options.Visibility=Visibility.Visible;progress.Visibility=Visibility.Collapsed;install.Content="Повторить установку";}
        finally{installing=false;RefreshButton();try{var resolved=Path.GetFullPath(temp);if(resolved.StartsWith(Path.GetFullPath(cacheRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Directory.Delete(resolved,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    private void OpenIsland(){try{Process.Start(new ProcessStartInfo(Path.Combine(folder.Text,"DIsland.exe")){UseShellExecute=true});}catch(Exception e){MessageBox.Show(this,e.Message,"Hoshino Island");}}
    private void ShowLicenses()
    {
        var assembly=Assembly.GetExecutingAssembly();var text=string.Join("\n\n",new[]{"LICENSING.md","LICENSE.txt","MIT.txt","THIRD-PARTY-NOTICES.txt","INNO.txt"}.Select(name=>{using var stream=assembly.GetManifestResourceStream("Hoshino."+name)!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}));
        new Window{Owner=this,Title="Лицензии и авторство",Width=700,Height=560,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(26,21,33)),Content=new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(20),Background=new SolidColorBrush(Color.FromRgb(26,21,33)),Foreground=Brushes.White,BorderThickness=new Thickness(0)}}.ShowDialog();
    }
    private static ResourceDictionary CreateStyles()=> (ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
      <Style TargetType="Button"><Setter Property="Foreground" Value="White"/><Setter Property="Background" Value="#382C40"/><Setter Property="Padding" Value="14,8"/><Setter Property="Cursor" Value="Hand"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="Frame" Background="{TemplateBinding Background}" CornerRadius="12" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.85"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="Frame" Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
      <Style TargetType="TextBox"><Setter Property="Background" Value="#302638"/><Setter Property="Foreground" Value="White"/><Setter Property="BorderBrush" Value="#59425F"/><Setter Property="CaretBrush" Value="#EAB0CF"/></Style>
      <Style TargetType="CheckBox"><Setter Property="Foreground" Value="#D5CBD9"/></Style>
    </ResourceDictionary>
    """);
    private static FrameworkElement Face()
    {
        var canvas=new Canvas{Width=120,Height=130};var bounce=new TranslateTransform();canvas.RenderTransform=bounce;
        void Shape(string path,string fill)=>canvas.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(path),Fill=(Brush)new BrushConverter().ConvertFromString(fill)!});
        Shape("M55 24C49 11 61 1 72 7c6 3 7 8 4 12-1-6-6-8-11-6-5 2-6 7-4 11C82 21 99 36 101 56l10 8-10 1 6 7-9-1c1 15 2 24 8 35l-10-4 2 12-11-8-2 10c-8-8-11-18-12-29H47c-1 11-4 21-12 29l-2-10-11 8 2-12-10 4c6-11 7-20 8-35l-9 1 6-7-10-1 10-8C21 36 34 24 55 24Z","#EAB0CF");
        Shape("M30 57C30 36 90 36 90 57V71C90 87 78 97 60 98 42 97 30 87 30 71Z","#FFF0E9");
        Shape("M23 59C25 34 38 21 60 24 82 21 98 37 98 59L104 65C94 64 89 59 85 54c4 9 3 15-2 19-2-8-3-16-7-23 0 12-6 19-15 24 4-13 1-23-2-32-1 14-5 23-12 28 1-13-1-21-4-27-1 12-5 21-13 26l3-13c-4 4-9 7-15 8Z","#EAB0CF");
        foreach(var (x,color) in new[]{(47d,"#F2D17D"),(73d,"#83CFF3")}){var eye=new System.Windows.Shapes.Ellipse{Width=11.6,Height=11,Fill=(Brush)new BrushConverter().ConvertFromString(color)!,RenderTransformOrigin=new Point(.5,.5)};Canvas.SetLeft(eye,x-5.8);Canvas.SetTop(eye,71.5);canvas.Children.Add(eye);var eyelid=new ScaleTransform(1,1);eye.RenderTransform=eyelid;var blink=new DoubleAnimationUsingKeyFrames{Duration=TimeSpan.FromSeconds(5),RepeatBehavior=RepeatBehavior.Forever};blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.Zero)));blink.KeyFrames.Add(new LinearDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.65))));blink.KeyFrames.Add(new LinearDoubleKeyFrame(.1,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.78))));blink.KeyFrames.Add(new LinearDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.92))));eyelid.BeginAnimation(ScaleTransform.ScaleYProperty,blink);}
        bounce.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(-3,3,TimeSpan.FromSeconds(2.4)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
        return new Viewbox{Child=canvas,Width=154,Height=170};
    }
}
