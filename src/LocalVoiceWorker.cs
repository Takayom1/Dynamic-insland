// Copyright 2026 Takayom1. SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
namespace DIsland;

/// <summary>One serialized, cancellable Python process keeps selected models warm.</summary>
public sealed class LocalVoiceWorker : IDisposable
{
    private readonly SemaphoreSlim serial=new(1);
    private readonly object lifecycle=new();
    private readonly StringBuilder errors=new();
    private Process? process;
    private string? configuration,prepared;
    private bool disposed;
    public string Device {get;private set;}="";
    public Task Prepare(Action<string> status,CancellationToken cancel)=>Request("prepare","","","",status,cancel);
    public Task Render(string textFile,string input,string output,Action<string> status,CancellationToken cancel)=>Request("render",textFile,input,output,status,cancel);

    private async Task Request(string action,string textFile,string input,string output,Action<string> status,CancellationToken cancel)
    {
        await serial.WaitAsync(cancel);Process? current=null;
        try {
            var p=AssistantPreferences.Current;
            var options=new {neural=p.NeuralEnabled,neural_model=p.NeuralModel,speaker=p.NeuralSpeaker,rvc=p.RvcEnabled,engine=p.RvcEngine,rvc_model=p.RvcModel,index=p.RvcIndex,python=p.RvcPython};
            var key=JsonSerializer.Serialize(options);
            lock(lifecycle) {
                ObjectDisposedException.ThrowIf(disposed,this);
                if(configuration!=key||process==null||process.HasExited){StopCore();Start(p.RvcPython);configuration=key;}
                current=process!;
                if(action=="prepare"&&prepared==key)return;
            }
            var request=new {action,options.neural,options.neural_model,options.speaker,options.rvc,options.engine,options.rvc_model,options.index,text_file=textFile,input,output};
            await current.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(),cancel);
            while(await current.StandardOutput.ReadLineAsync(cancel) is { } line){
                JsonDocument json;try{json=JsonDocument.Parse(line);}catch(JsonException){continue;}
                using(json){var root=json.RootElement;if(!root.TryGetProperty("type",out var kind))continue;
                    switch(kind.GetString()){
                        case "stage":
                            if(root.TryGetProperty("device",out var device))Device=device.GetString()??"";
                            status(root.GetProperty("stage").GetString() switch {
                                "load_torch"=>"Загружаю движок голоса",
                                "load_neural"=>"Загружаю нейроголос",
                                "load_rvc"=>"Загружаю тембр Хошино",
                                "synthesize"=>"Синтезирую речь",
                                "convert"=>"Применяю тембр Хошино · "+(Device.StartsWith("cuda")?"GPU":"CPU"),
                                _=>"Готовлю голос"});break;
                        case "error":throw new IOException(root.GetProperty("message").GetString());
                        case "done":Device=root.GetProperty("device").GetString()??"cpu";prepared=key;return;
                    }
                }
            }
            string detail;lock(errors)detail=errors.ToString();throw new IOException("Движок голоса закрылся. "+detail);
        }catch(OperationCanceledException){lock(lifecycle){if(ReferenceEquals(process,current))StopCore();}throw;}
        catch{lock(lifecycle){if(ReferenceEquals(process,current))StopCore();}throw;}
        finally{serial.Release();}
    }
    private void Start(string python)
    {
        if(!File.Exists(python))throw new IOException("Укажите Python нейроголоса в настройках");
        var utf8=new UTF8Encoding(false);
        var info=new ProcessStartInfo(python){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=utf8,StandardOutputEncoding=utf8,StandardErrorEncoding=utf8};
        info.ArgumentList.Add("-u");info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"Assets","Voice","voice-worker.py"));info.Environment["PYTHONUTF8"]="1";
        process=Process.Start(info)??throw new IOException("Не удалось запустить движок голоса");
        var current=process;lock(errors)errors.Clear();
        _=Task.Run(async()=>{try{while(await current.StandardError.ReadLineAsync() is { } line)lock(errors){errors.AppendLine(line);if(errors.Length>2000)errors.Remove(0,errors.Length-2000);}}catch(ObjectDisposedException){}catch(IOException){}});
    }
    public void Reset(){lock(lifecycle)StopCore();}
    private void StopCore(){var old=process;process=null;configuration=prepared=null;Device="";if(old==null)return;try{if(!old.HasExited)old.Kill(true);}catch(InvalidOperationException){}finally{old.Dispose();}}
    public void Dispose(){lock(lifecycle){disposed=true;StopCore();}}
}
