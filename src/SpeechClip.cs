// Copyright 2026 Takayom1. SPDX-License-Identifier: MIT
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace DIsland;

/// <summary>Keeps the whole command, including brief pauses and quiet consonants.</summary>
public sealed class SpeechClip
{
    private readonly MemoryStream data=new();
    private int quiet,samples,voicedSamples;
    private bool heard,finished;
    private readonly int pauseSamples;
    public SpeechClip(double pauseSeconds)=>pauseSamples=(int)(16000*Math.Clamp(pauseSeconds,.8,3));
    public event Action<byte[]>? Completed;
    public void Feed(byte[] bytes,int count)
    {
        if(finished)return;data.Write(bytes,0,count);samples+=count/2;
        double sum=0;for(var i=0;i+1<count;i+=2){var value=(short)(bytes[i]|bytes[i+1]<<8)/32768.0;sum+=value*value;}
        var voiced=Math.Sqrt(sum/Math.Max(1,count/2))>.002;
        if(voiced){voicedSamples+=count/2;heard=voicedSamples>=4000;quiet=0;}else if(heard)quiet+=count/2;
        if(heard&&quiet>=pauseSamples||samples>=16000*30)Finish();
    }
    public void Finish(){if(finished)return;finished=true;Completed?.Invoke(heard?data.ToArray():Array.Empty<byte>());data.Dispose();}
}
public static class VoiceGain
{
    public static void Scale(string input,string output,double gain)
    {
        using var reader=new WaveFileReader(input);
        var volume=new VolumeSampleProvider(reader.ToSampleProvider()){Volume=(float)Math.Clamp(gain,0,1)};
        WaveFileWriter.CreateWaveFile16(output,volume);
    }
}
