// Original Hoshino Island code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace DIsland;

/// <summary>Explicit-save daily Markdown editor, in the selected vault or local per-user storage.</summary>
public sealed class JournalPanel : Border, IDisposable
{
    private DateTime date=DateTime.Today;
    private readonly TextBlock title=new(){FontWeight=FontWeights.SemiBold,FontSize=13,TextWrapping=TextWrapping.Wrap};
    private readonly TextBox editor=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=132,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxLength=20000};
    private readonly TextBlock status=new(){Opacity=.6,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,0)};
    private string relative="",root="",loadedHash="";
    private bool loading,dirty;
    private bool vault;
    private readonly System.Windows.Threading.DispatcherTimer refresh=new(){Interval=TimeSpan.FromSeconds(2)};
    public event Action? LayoutChanged;
    public JournalPanel(Action<TextBox> beginTyping)
    {
        AssistantTheme.Apply(this);
        CornerRadius=new CornerRadius(16);Padding=new Thickness(14);SetResourceReference(BackgroundProperty,"HoshinoSurface");
        var panel=new StackPanel();Child=panel;
        var row=new DockPanel{Margin=new Thickness(0,0,0,8)};
        var save=new Button{Content="Сохранить",Padding=new Thickness(8,4,8,4)};save.Click+=(_,_)=>Save();DockPanel.SetDock(save,Dock.Right);row.Children.Add(save);row.Children.Add(title);panel.Children.Add(row);
        editor.Background=new SolidColorBrush(Color.FromArgb(14,255,255,255));editor.Foreground=Brushes.White;editor.CaretBrush=Brushes.White;editor.Padding=new Thickness(8);editor.BorderThickness=new Thickness(0);
        editor.PreviewMouseLeftButtonDown+=(_,_)=>beginTyping(editor);
        editor.TextChanged+=(_,_)=>{if(!loading){dirty=true;status.Text="Есть несохранённые изменения";}};
        panel.Children.Add(editor);panel.Children.Add(status);
        var actions=new WrapPanel{Margin=new Thickness(0,6,0,0)};
        void Button(string text,Action click){var button=new Button{Content=text,Margin=new Thickness(0,0,8,0),Padding=new Thickness(8,4,8,4)};button.Click+=(_,_)=>click();actions.Children.Add(button);}
        Button("Обновить",()=>{if(dirty){status.Text="Сначала сохраните черновик или смените дату — он останется в локальном резерве.";return;}Load();});
        Button("В Obsidian",()=>{try {if(dirty){status.Text="Сначала сохраните запись";return;}if(!vault){status.Text="Подключите vault в настройках Obsidian";return;}var file=Path.GetFullPath(Path.Combine(root,relative));if(!File.Exists(file)){status.Text="Сначала сохраните запись";return;}System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("obsidian://open?path="+Uri.EscapeDataString(file)){UseShellExecute=true});}catch(Exception e){status.Text=e.Message;}});
        panel.Children.Add(actions);MouseLeftButtonUp+=(_,e)=>e.Handled=true;
        refresh.Tick+=(_,_)=> {if(dirty||loadedHash.Length==0)return;try{var current=new VaultFiles(new(){Root=root}).Read(relative);if(VaultFiles.Hash(current)!=loadedHash)Load();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text=e.Message;}};
        IsVisibleChanged+=(_,_)=> {if(IsVisible)refresh.Start();else refresh.Stop();};
        Loaded+=(_,_)=>{if(IsVisible)refresh.Start();};
        ObsidianPreferences.Changed+=PreferencesChanged;Unloaded+=(_,_)=>{refresh.Stop();SaveDraft();};Load();
    }
    public void Dispose(){refresh.Stop();SaveDraft();ObsidianPreferences.Changed-=PreferencesChanged;}
    private void PreferencesChanged(){if(SaveDraft())Load();}
    public void SelectDate(DateTime value){if(value.Date==date)return;if(!SaveDraft())return;date=value.Date;Load();}
    private bool SaveDraft()
    {
        if(!dirty)return true;
        try {var dir=Path.Combine(AssistantPreferences.DataDirectory,"journal-drafts");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,$"{date:yyyy-MM-dd}-{DateTime.UtcNow:HHmmssfff}.md"),editor.Text);status.Text="Черновик сохранён локально";return true;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text=e.Message;return false;}
    }
    private void Load()
    {
        loading=true;
        try {
            var p=ObsidianPreferences.Current;vault=p.Enabled;root=vault?p.Root:Path.Combine(AssistantPreferences.DataDirectory,"journal");relative=vault?p.DailyFolder.TrimEnd('/','\\')+$"/{date:yyyy-MM-dd}.md":$"{date:yyyy-MM-dd}.md";
            var files=new VaultFiles(new(){Root=root});var text=files.Read(relative);loadedHash=VaultFiles.Hash(text);editor.Text=text;dirty=false;
            title.Text=date.ToString("dd MMM · Дневник");status.Text=vault?"Markdown · выбранный vault":"Локальный Markdown · Obsidian не подключён";
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){status.Text=e.Message;editor.Text="";loadedHash="";}
        finally{loading=false;} LayoutChanged?.Invoke();
    }
    private void Save()
    {
        try {if(loadedHash.Length==0)throw new IOException("Сначала обновите запись после устранения ошибки");var files=new VaultFiles(new(){Root=root});files.Write(relative,editor.Text,loadedHash);loadedHash=VaultFiles.Hash(editor.Text);dirty=false;status.Text="Сохранено · "+DateTime.Now.ToString("HH:mm");}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){var saved=SaveDraft();status.Text=e.Message+(saved?" Черновик сохранён локально.":" Не удалось записать черновик — скопируйте текст перед закрытием.");}
    }
}
