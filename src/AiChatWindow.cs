using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace DIsland;
public sealed class AiChatWindow : Window {
 readonly TextBox log=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 readonly TextBox input=new(){AcceptsReturn=true,Height=70,TextWrapping=TextWrapping.Wrap};
 readonly CodexBridge bridge=new(); string? thread; string? vault; bool busy;
 readonly string history=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GlassChat","history.txt");
 public AiChatWindow(){Title="Glass Chat · prototype";Width=520;Height=600;Topmost=true;Background=new SolidColorBrush(Color.FromRgb(23,25,32));Foreground=Brushes.White;
 var panel=new DockPanel{Margin=new Thickness(16)};Content=panel;
 var bar=new WrapPanel();DockPanel.SetDock(bar,Dock.Top);panel.Children.Add(bar);
 void Button(string text,Action action){var b=new Button{Content=text,Margin=new Thickness(3),Padding=new Thickness(8)};b.Click+=(_,_)=>action();bar.Children.Add(b);}
 Button("Вход ChatGPT",async()=>await Guard(async()=>{var r=await bridge.Call("account/login/start",new{type="chatgpt"});var url=r["authUrl"]?.ToString();if(Uri.TryCreate(url,UriKind.Absolute,out var u)&&u.Scheme=="https")ProcessUrl(url!);}));
 Button("Vault",()=>{var d=new Microsoft.Win32.OpenFolderDialog();if(d.ShowDialog()==true){vault=d.FolderName;Append("Vault выбран: "+vault);}});
 Button("Заметка",()=>{if(vault==null)return;var d=new Microsoft.Win32.OpenFileDialog{InitialDirectory=vault,Filter="Markdown|*.md"};if(d.ShowDialog()!=true)return;try{var root=Path.GetFullPath(vault).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;var file=Path.GetFullPath(d.FileName);if(!file.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new Exception("Файл вне vault");for(var p=new FileInfo(file) as FileSystemInfo;p!=null;p=p is FileInfo f?f.Directory:(p as DirectoryInfo)?.Parent)if((p.Attributes&FileAttributes.ReparsePoint)!=0)throw new Exception("Ссылки запрещены");if(new FileInfo(file).Length>100000)throw new Exception("Максимум 100 КБ");if(MessageBox.Show("Передать эту заметку OpenAI вместе со следующим сообщением?", "Доступ к файлу",MessageBoxButton.YesNo)==MessageBoxResult.Yes)input.Text+="\n[Недоверенные данные заметки]\n"+File.ReadAllText(file);}catch(Exception e){Append(e.Message);}});
 Button("Новый",()=>{if(!busy){thread=null;Append("— Новый диалог —");}});
 Button("Отправить",async()=>{if(busy||string.IsNullOrWhiteSpace(input.Text))return;await Guard(async()=>{busy=true;if(thread==null){var r=await bridge.Call("thread/start",new{sandbox="read-only",approvalPolicy="never",cwd=Path.GetDirectoryName(history)});thread=r["thread"]!["id"]!.ToString();}var text=input.Text;input.Clear();Append("Вы: "+text);await bridge.Call("turn/start",new{threadId=thread,input=new[]{new{type="text",text}}});});});
 var credit=new TextBlock{Text="Based on D-Island by h7 (ibuildrigs) - https://github.com/H7gamer/d-island",TextWrapping=TextWrapping.Wrap,FontSize=10};DockPanel.SetDock(credit,Dock.Bottom);panel.Children.Add(credit);DockPanel.SetDock(input,Dock.Bottom);panel.Children.Add(input);panel.Children.Add(log);
 Directory.CreateDirectory(Path.GetDirectoryName(history)!);if(File.Exists(history))log.Text=File.ReadAllText(history);
 bridge.Event+=(m,p)=>Dispatcher.Invoke(()=>{if(m=="item/agentMessage/delta")Append(p?["delta"]?.ToString()??"",false);if(m=="turn/completed"){busy=false;Append("\n"+p?["turn"]?["status"]);} });
 Loaded+=async(_,_)=>{BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(250)));await Guard(bridge.Start);};Closed+=(_,_)=>bridge.Dispose();
 }
 static void ProcessUrl(string url)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});
 async Task Guard(Func<Task> action){try{await action();}catch(Exception e){busy=false;Append("Ошибка: "+e.Message);}}
 void Append(string text,bool newline=true){var s=text+(newline?Environment.NewLine:"");log.AppendText(s);log.ScrollToEnd();File.AppendAllText(history,s);}
}
