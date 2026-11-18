// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.IO;
namespace DIsland;
public static class RvcVoice
{
    public static async Task Synthesize(string textFile,string output,CancellationToken cancel)
    {
        var p=AssistantPreferences.Current;
        if(!File.Exists(p.RvcPython)||!File.Exists(p.NeuralModel))throw new IOException("Укажите Python и проверенную модель Silero в настройках");
        var start=new ProcessStartInfo(p.RvcPython){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{Path.Combine(AppContext.BaseDirectory,"Assets","Voice","neural-tts.py"),"--model",Path.GetFullPath(p.NeuralModel),"--text",textFile,"--output",output,"--voice",p.NeuralSpeaker})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("Не удалось запустить нейроголос");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync(cancel);}catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);throw;}
        await stdout;var error=await stderr;if(process.ExitCode!=0||!File.Exists(output))throw new IOException("Нейроголос: "+(error.Length>800?error[^800..]:error));
    }
    public static async Task Convert(string input,string output,CancellationToken cancel)
    {
        var p=AssistantPreferences.Current;
        if(!File.Exists(p.RvcPython)||!Directory.Exists(p.RvcEngine)||!File.Exists(p.RvcModel)||!File.Exists(p.RvcIndex))throw new IOException("Настройте Python, Applio и файлы RVC в настройках голоса");
        var start=new ProcessStartInfo(p.RvcPython){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{Path.Combine(AppContext.BaseDirectory,"Assets","Voice","rvc-convert.py"),"--engine",Path.GetFullPath(p.RvcEngine),"--model",Path.GetFullPath(p.RvcModel),"--index",Path.GetFullPath(p.RvcIndex),"--pitch",p.RvcPitch.ToString(System.Globalization.CultureInfo.InvariantCulture),"--index-rate",p.RvcIndexRate.ToString(System.Globalization.CultureInfo.InvariantCulture),"--protect",p.RvcProtect.ToString(System.Globalization.CultureInfo.InvariantCulture),"--input",input,"--output",output})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("Не удалось запустить RVC");
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        try {await process.WaitForExitAsync(cancel);}catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);throw;}
        await stdout;var error=await stderr;
        if(process.ExitCode!=0||!File.Exists(output))throw new IOException("RVC: "+(error.Length>800?error[^800..]:error));
        if(new FileInfo(output).Length>50*1024*1024)throw new IOException("RVC вернул слишком большой WAV");
    }
}
