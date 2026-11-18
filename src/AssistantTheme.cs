// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows;
using System.Windows.Markup;
namespace DIsland;
public static class AssistantTheme
{
    public static void Apply(FrameworkElement element) => element.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Theme));
    private const string Theme = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
 <Style TargetType="Button">
  <Setter Property="Background" Value="{DynamicResource HoshinoControl}"/><Setter Property="Foreground" Value="{DynamicResource HoshinoText}"/><Setter Property="Padding" Value="13,8"/><Setter Property="Cursor" Value="Hand"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="chrome" Background="{TemplateBinding Background}" CornerRadius="9" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="chrome" Property="Background" Value="{DynamicResource HoshinoBorder}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="TextBox">
  <Setter Property="Background" Value="{DynamicResource HoshinoSurface}"/><Setter Property="Foreground" Value="{DynamicResource HoshinoText}"/><Setter Property="CaretBrush" Value="{DynamicResource HoshinoText}"/><Setter Property="Padding" Value="10,8"/><Setter Property="BorderBrush" Value="{DynamicResource HoshinoBorder}"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border Background="{TemplateBinding Background}" CornerRadius="8" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="{TemplateBinding Padding}"><ScrollViewer x:Name="PART_ContentHost" VerticalScrollBarVisibility="{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}" HorizontalScrollBarVisibility="{TemplateBinding ScrollViewer.HorizontalScrollBarVisibility}"/></Border></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="{DynamicResource HoshinoText}"/><Setter Property="Padding" Value="12,8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="item" Padding="{TemplateBinding Padding}" Background="Transparent"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="item" Property="Background" Value="{DynamicResource HoshinoBorder}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="item" Property="Background" Value="{DynamicResource HoshinoControl}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType="ComboBox">
  <Setter Property="Foreground" Value="{DynamicResource HoshinoText}"/><Setter Property="MinHeight" Value="36"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid>
   <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{DynamicResource HoshinoSurface}" CornerRadius="8" BorderBrush="{DynamicResource HoshinoBorder}" BorderThickness="1"><TextBlock Text="▾" Foreground="{DynamicResource HoshinoAccent}" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,12,0"/></Border></ControlTemplate></ToggleButton.Template></ToggleButton>
   <ContentPresenter x:Name="selected" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" Margin="12,8,34,8" IsHitTestVisible="False" VerticalAlignment="Center"/>
   <TextBox x:Name="PART_EditableTextBox" Visibility="Collapsed" Background="Transparent" BorderThickness="0" Margin="3,0,30,0" Text="{Binding Text,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay,UpdateSourceTrigger=PropertyChanged}"/>
   <Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False"><Border Background="{DynamicResource HoshinoSurface}" BorderBrush="{DynamicResource HoshinoBorder}" BorderThickness="1" CornerRadius="8" MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}"><ScrollViewer MaxHeight="260" VerticalScrollBarVisibility="Auto"><ItemsPresenter/></ScrollViewer></Border></Popup>
  </Grid><ControlTemplate.Triggers><Trigger Property="IsEditable" Value="True"><Setter TargetName="selected" Property="Visibility" Value="Collapsed"/><Setter TargetName="PART_EditableTextBox" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="ScrollBar"><Setter Property="Width" Value="8"/><Setter Property="Background" Value="Transparent"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Track x:Name="PART_Track" IsDirectionReversed="True"><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{DynamicResource HoshinoBorder}" CornerRadius="4" Margin="1"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>
""";
}
