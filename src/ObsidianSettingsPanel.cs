// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows;
using System.Windows.Controls;
namespace DIsland;

public sealed class ObsidianSettingsPanel : StackPanel
{
    private readonly TextBox root=new(),tasks=new(),memory=new(),daily=new();
    private readonly TextBox memoryText=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=140,MaxLength=20000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly CheckBox enabled=new(){Content="Синхронизировать с этим vault"};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Opacity=.7,Margin=new Thickness(0,10,0,0)};
    private string memoryHash="";
    public ObsidianSettingsPanel()
    {
        var p=ObsidianPreferences.Current;
        AddText("Задачи, ежедневник и память — обычные Markdown-файлы. Можно редактировать их здесь, в Obsidian или через Codex. Синхронизация задач проверяет изменения каждые 2 секунды, пока приложение открыто.");
        Field("Vault",root,p.Root.Length>0?p.Root:AssistantPreferences.Current.Vault);
        var choose=new Button{Content="Выбрать vault…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,6,0,8)};
        choose.Click+=(_,_)=>{var dialog=new Microsoft.Win32.OpenFolderDialog();if(dialog.ShowDialog()==true)root.Text=dialog.FolderName;};Children.Add(choose);
        Field("Заметка задач (относительный путь)",tasks,p.TasksNote);Field("Заметка памяти",memory,p.MemoryNote);Field("Папка ежедневника · YYYY-MM-DD.md",daily,p.DailyFolder);
        enabled.IsChecked=p.Enabled;Children.Add(enabled);
        AddText("При подключении локальные задачи объединятся с разделом Hoshino в заметке. Остальной текст заметки сохраняется. Ссылки/junction и .obsidian не затрагиваются. Резервные копии правок остаются локально вне vault.");
        ActionButton("Сохранить подключение",()=>{
            if(AssistantRuntime.Busy)throw new InvalidOperationException("Сначала остановите активный ответ помощника");
            new ObsidianPreferences{Root=root.Text.Trim(),TasksNote=tasks.Text.Trim(),MemoryNote=memory.Text.Trim(),DailyFolder=daily.Text.Trim(),Enabled=enabled.IsChecked==true}.Save();
            memoryHash="";
            AssistantRuntime.Router.RestartAll();status.Text=ObsidianSync.Active?.Status??"Сохранено";
        });
        ActionButton("Синхронизировать сейчас",()=>{ObsidianSync.Active?.Poll();status.Text=ObsidianSync.Active?.Status??"Остров не запущен";});
        ActionButton("Объединить оба варианта после конфликта",()=>{ObsidianSync.Active?.MergeAfterConflict();status.Text=ObsidianSync.Active?.Status??"Остров не запущен";});
        AddText("Память помощника · предпочтения и сведения, которые вы хотите сохранить. Она используется как контекст новых и возобновлённых диалогов Codex; содержимое может передаваться OpenAI при работе с чатом. Это не история приложения ChatGPT.");
        Children.Add(memoryText);
        ActionButton("Прочитать память из vault",()=>{RequireEnabled();var text=new VaultFiles(ObsidianPreferences.Current).Read(ObsidianPreferences.Current.MemoryNote);memoryText.Text=text;memoryHash=VaultFiles.Hash(text);status.Text="Память загружена";});
        ActionButton("Сохранить память в vault",()=>{RequireEnabled();if(memoryHash.Length==0)throw new InvalidOperationException("Сначала прочитайте текущую память");new VaultFiles(ObsidianPreferences.Current).Write(ObsidianPreferences.Current.MemoryNote,memoryText.Text,memoryHash);memoryHash=VaultFiles.Hash(memoryText.Text);status.Text="Память сохранена. Начните новый чат для обновлённого контекста.";});
        AddText("Codex использует корень подключённого vault как рабочую папку. Выбранная политика доступа и подтверждения опасных действий сохраняются. Изменения модели в файлах появятся в острове при следующей проверке. Синхронизация всего vault с облаком и настройка плагинов Obsidian не выполняются.");
        Children.Add(status);
    }
    private static void RequireEnabled(){if(!ObsidianPreferences.Current.Enabled)throw new InvalidOperationException("Сначала подключите vault");}
    private void Field(string label,TextBox box,string value){AddText(label);box.Text=value;Children.Add(box);}
    private void AddText(string text)=>Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Opacity=.65,Margin=new Thickness(0,8,0,8),FontSize=12});
    private void ActionButton(string label,Action action){var b=new Button{Content=label,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,6,0,6)};b.Click+=(_,_)=>{try{action();}catch(Exception e){status.Text=e.Message;}};Children.Add(b);}
}
