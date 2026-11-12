// Original Hoshino Island code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DIsland.Services;

namespace DIsland;

/// <summary>A small view of the same persistent list as the To-Do page.</summary>
public sealed class HomeTasksPanel : Border
{
    private readonly NotesService notes;
    private readonly StackPanel tasks = new();
    private readonly TextBlock count = new() { Opacity=.55, FontSize=11, VerticalAlignment=VerticalAlignment.Center };
    private readonly TextBox input = new() { MaxLength=240, FontSize=13, MinHeight=32 };
    private readonly TextBlock hint = new() { Text="Одно небольшое дело — уже начало", Opacity=.5, FontSize=11, Margin=new Thickness(0,4,0,0) };
    public HomeTasksPanel(NotesService notes, Action<TextBox> beginTyping)
    {
        this.notes=notes;
        Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
<Style TargetType="CheckBox"><Setter Property="Cursor" Value="Hand"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox"><Grid Background="Transparent"><Grid.ColumnDefinitions><ColumnDefinition Width="26"/><ColumnDefinition/></Grid.ColumnDefinitions><Ellipse x:Name="ring" Width="15" Height="15" Stroke="{DynamicResource HoshinoMuted}" StrokeThickness="1.2" VerticalAlignment="Center" HorizontalAlignment="Left"/><ContentPresenter Grid.Column="1" VerticalAlignment="Center"/></Grid><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="ring" Property="Stroke" Value="{DynamicResource HoshinoAccent}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="ring" Property="Stroke" Value="White"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>
"""));
        CornerRadius=new CornerRadius(18); Padding=new Thickness(16); Margin=new Thickness(0,8,0,8);
        SetResourceReference(BackgroundProperty,"HoshinoSurface");
        var body=new StackPanel(); Child=body;
        var heading=new DockPanel { Margin=new Thickness(0,0,0,8) };
        DockPanel.SetDock(count,Dock.Right); heading.Children.Add(count);
        heading.Children.Add(new TextBlock { Text="На сейчас", FontSize=14, FontWeight=FontWeights.SemiBold }); body.Children.Add(heading);
        body.Children.Add(new ScrollViewer { Content=tasks, MaxHeight=134, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
        var composer=new Grid { Margin=new Thickness(0,8,0,0) };
        composer.ColumnDefinitions.Add(new ColumnDefinition());composer.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        input.Style=null; input.Background=System.Windows.Media.Brushes.Transparent; input.BorderThickness=new Thickness(0);
        input.Foreground=System.Windows.Media.Brushes.White; input.CaretBrush=System.Windows.Media.Brushes.White;
        input.VerticalContentAlignment=VerticalAlignment.Center; input.Padding=new Thickness(0); input.ToolTip="Добавить дело · Enter";
        System.Windows.Automation.AutomationProperties.SetName(input,"Новое дело");
        input.PreviewMouseLeftButtonDown+=(_,_)=>beginTyping(input);
        input.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Enter) { e.Handled=true; Add(); } };
        var field=new Border { CornerRadius=new CornerRadius(10), Padding=new Thickness(8,2,8,2) };
        field.SetResourceReference(BackgroundProperty,"HoshinoControl");
        var fieldGrid=new Grid();field.Child=fieldGrid;fieldGrid.Children.Add(input);
        var placeholder=new TextBlock { Text="Одно небольшое дело…", Opacity=.45, VerticalAlignment=VerticalAlignment.Center, IsHitTestVisible=false };
        fieldGrid.Children.Add(placeholder);input.TextChanged+=(_,_)=>placeholder.Visibility=input.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        composer.Children.Add(field);
        var add=new Button { Content="+ Дело", Margin=new Thickness(8,0,0,0), Style=(Style)Application.Current.FindResource("ChipButton") };
        Grid.SetColumn(add,1); composer.Children.Add(add);add.Click+=(_,_)=>Add();
        body.Children.Add(composer);body.Children.Add(hint);
        MouseLeftButtonUp+=(_,e)=>e.Handled=true;
        notes.Changed+=Refresh; Refresh();
    }
    private void Add()
    {
        if(notes.Add(input.Text)) { input.Clear(); hint.Text="Одно небольшое дело — уже начало"; }
        else hint.Text=string.IsNullOrWhiteSpace(input.Text) ? "Напиши одно дело" : "Список заполнен — удали ненужные дела во вкладке задач";
    }
    private void Refresh()
    {
        tasks.Children.Clear(); count.Text=notes.OpenCount==0 ? "Всё сделано" : $"Осталось {notes.OpenCount}";
        foreach(var item in notes.Todos.Where(t=>!t.Done))
        {
            var check=new CheckBox { Content=new TextBlock { Text=item.Text, TextWrapping=TextWrapping.Wrap, FontSize=13 }, Margin=new Thickness(0,5,0,5), Foreground=System.Windows.Media.Brushes.White };
            var id=item.Id;check.Click+=(_,_)=>notes.SetDone(id,true); tasks.Children.Add(check);
        }
        if(notes.OpenCount==0) tasks.Children.Add(new TextBlock { Text="Можно выдохнуть. Или выбрать одно новое дело.", TextWrapping=TextWrapping.Wrap, Opacity=.65, FontSize=12, Margin=new Thickness(0,4,0,4) });
    }
}
