// Copyright (c) 2026 Takayom1. SPDX-License-Identifier: MIT
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace DIsland;
public sealed class WakeCalibrationWindow : Window
{
    private readonly AssistantVoice voice;
    private readonly TextBlock progress=new(){Text="Нажмите «Начать», затем три раза повторите фразу после сигналов.",TextWrapping=TextWrapping.Wrap,FontSize=15,Margin=new Thickness(0,14,0,12)};
    private readonly ProgressBar level=new(){Minimum=0,Maximum=100,Height=9,Margin=new Thickness(0,4,0,12)};
    private readonly Button start=new(){Content="Начать калибровку",Margin=new Thickness(0,10,0,10)};
    private readonly CheckBox enable=new(){Content="После калибровки ждать фразу и отвечать голосом",IsChecked=true,Foreground=Brushes.White};
    private readonly Action finished;
    private readonly ComboBox devices;
    public WakeCalibrationWindow(AssistantVoice voice,Action finished)
    {
        this.voice=voice;this.finished=finished;Title=WakeProfile.SelectedPhrase+" · три повторения";Width=560;Height=480;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=(Brush)Application.Current.FindResource("HoshinoBackground");Foreground=Brushes.White;AssistantTheme.Apply(this);
        var panel=new StackPanel{Margin=new Thickness(26)};Content=panel;
        panel.Children.Add(new TextBlock{Text="Запомним твоё произношение",FontSize=23,FontWeight=FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text=$"Три коротких повторения «{WakeProfile.SelectedPhrase}», отдельно от команды. Шаблоны звуковых признаков останутся только на этом ПК; исходные записи не сохраняются и не отправляются. Это не проверка личности.",TextWrapping=TextWrapping.Wrap,Opacity=.65,Margin=new Thickness(0,12,0,14)});
        devices=new ComboBox{ItemsSource=AssistantVoice.Microphones(),DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=AssistantPreferences.Current.MicrophoneDevice};if(devices.SelectedIndex<0)devices.SelectedIndex=0;panel.Children.Add(devices);
        panel.Children.Add(progress);panel.Children.Add(level);panel.Children.Add(enable);panel.Children.Add(start);
        var close=new Button{Content="Закрыть",HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(close);close.Click+=(_,_)=>Close();
        start.Click+=(_,_)=>{try{var p=AssistantPreferences.Current;p.MicrophoneDevice=(int)(devices.SelectedValue??-1);p.Save(false);devices.IsEnabled=false;voice.BeginCalibration();}catch(Exception e){progress.Text=e.Message;}};
        voice.CalibrationProgress+=Progress;voice.CalibrationCompleted+=Completed;voice.AudioLevel+=Level;voice.Status+=Status;voice.StateChanged+=State;
        Closed+=(_,_)=>{voice.CalibrationProgress-=Progress;voice.CalibrationCompleted-=Completed;voice.AudioLevel-=Level;voice.Status-=Status;voice.StateChanged-=State;voice.CancelCalibration();};
    }
    private void Progress(int n,string text){progress.Text=$"{n}/3 · {text}";}
    private void Level(double value)=>level.Value=value;
    private void Status(string text){if(!voice.Calibrating)progress.Text=text;}
    private void State(){start.IsEnabled=!voice.Calibrating;devices.IsEnabled=!voice.Calibrating;}
    private void Completed()
    {
        try{var p=AssistantPreferences.Current;p.WakeEnabled=enable.IsChecked==true;if(p.WakeEnabled)p.SpeakReplies=true;p.Save(false);voice.Configure();progress.Text="Готово: три шаблона сохранены. "+(p.WakeEnabled?$"Теперь можно сказать «{WakeProfile.SelectedPhrase}».":"Ожидание можно включить позже.");finished();}
        catch(Exception e){progress.Text=e.Message;}
    }
}
