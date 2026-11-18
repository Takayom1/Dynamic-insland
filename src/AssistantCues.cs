// Copyright 2026 Takayom1. SPDX-License-Identifier: MIT
// Audio: Kenney Interface Sounds, CC0; see Assets/Sounds/LICENSE-KENNEY.txt.
using System.IO;
using NAudio.Wave;
namespace DIsland;
public enum AssistantCue {Listen,Working,Reply,Error}
public static class AssistantCues
{
    private static long last;
    public static void Play(AssistantCue cue) => PlayInternal(cue.ToString().ToLowerInvariant()+".wav", 1.0);
    public static void PlayHeadpat() => PlayInternal("reply.wav", 0.7);
    private static void PlayInternal(string fileName, double scale)
    {
        var p=AssistantPreferences.Current;if(!p.AssistantSounds||p.AssistantSoundVolume<=0)return;
        var now=Environment.TickCount64;if(now-last<300)return;last=now;
        AudioFileReader? reader=null;WaveOutEvent? output=null;
        try{
            var file=Path.Combine(AppContext.BaseDirectory,"Assets","Sounds",fileName);if(!File.Exists(file))return;
            reader=new AudioFileReader(file){Volume=(float)Math.Clamp(p.AssistantSoundVolume*scale,0,1)};output=new WaveOutEvent{DesiredLatency=100};
            var currentReader=reader;var currentOutput=output;output.PlaybackStopped+=(_,_)=>{currentOutput.Dispose();currentReader.Dispose();};output.Init(reader);output.Play();
        }catch{output?.Dispose();reader?.Dispose();}
    }
}
