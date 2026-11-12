// Copyright (c) 2026 Takayom1. SPDX-License-Identifier: MIT
using System.IO;
using System.Text.Json;
using NAudio.Dsp;
namespace DIsland;

/// <summary>Local three-example phrase templates, not identity authentication.</summary>
public sealed class WakeProfile
{
    public int Version {get;set;}=1;
    public List<float[][]> Templates {get;set;}=new();
    public double Threshold {get;set;}
    public static string FilePath=>Path.Combine(AssistantPreferences.DataDirectory,"wake-profile.json");
    public static WakeProfile? Load(string? path=null)
    {
        try {path??=FilePath;if(!File.Exists(path)||new FileInfo(path).Length>2_000_000)return null;
            var p=JsonSerializer.Deserialize<WakeProfile>(File.ReadAllText(path));
            if(p==null||p.Version!=1||p.Templates==null||p.Templates.Count!=3||!double.IsFinite(p.Threshold)||p.Threshold<.25||p.Threshold>1)return null;
            if(p.Templates.Any(t=>t==null||t.Length<25||t.Length>450||t.Any(f=>f==null||f.Length!=13||f.Any(v=>!float.IsFinite(v)))))return null;return p;
        }catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){return null;}
    }
    public void Save(string? path=null){path??=FilePath;Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this));File.Move(path+".tmp",path,true);}
    public static WakeProfile Learn(IEnumerable<float[][]> samples)
    {
        var list=samples.ToList();if(list.Count!=3)throw new InvalidDataException("Нужны три повторения");
        var distances=new[]{Distance(list[0],list[1]),Distance(list[0],list[2]),Distance(list[1],list[2])};
        if(distances.Any(d=>!double.IsFinite(d)||d>1.05))throw new InvalidDataException("Повторения слишком разные. Произнесите одну фразу три раза в одинаковом темпе.");
        return new(){Templates=list,Threshold=Math.Clamp(distances.Average()*1.3+.12,.48,.88)};
    }
    public bool Matches(short[] pcm,out double score)
    {
        score=double.PositiveInfinity;if(pcm.Length<6400||pcm.Length>72000)return false;
        var feature=Features(pcm);var distances=Templates.Select(t=>Distance(t,feature)).OrderBy(x=>x).ToArray();
        // Two examples must agree; avoids matching one accidental enrollment noise.
        score=(distances[0]+distances[1])/2;return score<=Threshold;
    }
    public static double Distance(float[][] a,float[][] b)
    {
        if(a.Length==0||b.Length==0||a.Length/(double)b.Length is <.55 or >1.8)return double.PositiveInfinity;
        var previous=Enumerable.Repeat(double.PositiveInfinity,b.Length+1).ToArray();previous[0]=0;
        for(var i=1;i<=a.Length;i++){
            var next=Enumerable.Repeat(double.PositiveInfinity,b.Length+1).ToArray();
            var center=i*b.Length/(double)a.Length;var band=Math.Max(15,b.Length*.25);
            for(var j=Math.Max(1,(int)(center-band));j<=Math.Min(b.Length,(int)(center+band));j++){
                double cost=0;for(var k=0;k<13;k++){var d=a[i-1][k]-b[j-1][k];cost+=d*d;}
                next[j]=cost/13+Math.Min(previous[j-1]*1.0,Math.Min(previous[j],next[j-1]));
            }previous=next;
        }
        return Math.Sqrt(previous[b.Length]/Math.Max(a.Length,b.Length));
    }
    public static float[][] Features(short[] pcm)
    {
        var levels=new List<double>();for(var i=0;i<pcm.Length;i+=320){double sum=0;var n=Math.Min(320,pcm.Length-i);for(var j=0;j<n;j++){var x=pcm[i+j]/32768.0;sum+=x*x;}levels.Add(Math.Sqrt(sum/Math.Max(1,n)));}
        var gate=Math.Max(.003,levels.DefaultIfEmpty(0).Max()*.12);var first=levels.FindIndex(e=>e>gate);var last=levels.FindLastIndex(e=>e>gate);
        if(first>=0){var begin=Math.Max(0,first*320-1280);var end=Math.Min(pcm.Length,(last+1)*320+1280);pcm=pcm[begin..end];}
        if(pcm.Length<400)throw new InvalidDataException("Слишком короткая запись");
        const int frame=400,hop=160,nfft=512,bands=26,dims=13;
        var count=Math.Min(450,1+(pcm.Length-frame)/hop);var result=new float[count][];
        double Mel(double hz)=>2595*Math.Log10(1+hz/700);
        double Hz(double mel)=>700*(Math.Pow(10,mel/2595)-1);
        var points=Enumerable.Range(0,bands+2).Select(i=>(int)Math.Floor((nfft+1)*Hz(Mel(80)+(Mel(7600)-Mel(80))*i/(bands+1))/16000)).ToArray();
        for(var f=0;f<count;f++){
            var fft=new Complex[nfft];var start=f*hop;
            for(var i=0;i<frame;i++){var sample=pcm[start+i]/32768.0;var before=start+i>0?pcm[start+i-1]/32768.0:0;fft[i].X=(float)((sample-.97*before)*(.54-.46*Math.Cos(2*Math.PI*i/(frame-1))));}
            FastFourierTransform.FFT(true,9,fft);var power=fft.Take(nfft/2+1).Select(c=>(double)c.X*c.X+c.Y*c.Y).ToArray();
            var energies=new double[bands];for(var m=0;m<bands;m++){
                double sum=0;for(var k=points[m];k<points[m+1];k++)sum+=power[k]*(k-points[m])/(double)Math.Max(1,points[m+1]-points[m]);
                for(var k=points[m+1];k<points[m+2];k++)sum+=power[k]*(points[m+2]-k)/(double)Math.Max(1,points[m+2]-points[m+1]);energies[m]=Math.Log(Math.Max(1e-12,sum));
            }
            result[f]=new float[dims];for(var k=0;k<dims;k++)for(var m=0;m<bands;m++)result[f][k]+=(float)(energies[m]*Math.Cos(Math.PI*(k+1)*(m+.5)/bands));
        }
        for(var k=0;k<dims;k++){var mean=result.Average(r=>r[k]);var sd=Math.Sqrt(result.Average(r=>(r[k]-mean)*(r[k]-mean)));foreach(var r in result)r[k]=(float)((r[k]-mean)/Math.Max(.1,sd));}
        return result;
    }
}

/// <summary>PCM16 speech segmentation. Buffers stay in memory and are discarded.</summary>
public sealed class PhraseSegments
{
    private readonly List<short> current=new();
    private readonly Queue<short> before=new();
    private int warmup,quiet,cooldown;
    private double noise;
    private bool active;
    public event Action<short[]>? Segment;
    public event Action? Ready;
    public event Action<double>? Level;
    public void Feed(byte[] data,int length)
    {
        var samples=new short[length/2];double square=0;
        for(var i=0;i<samples.Length;i++){samples[i]=(short)(data[i*2]|data[i*2+1]<<8);var x=samples[i]/32768.0;square+=x*x;}
        var rms=Math.Sqrt(square/Math.Max(1,samples.Length));Level?.Invoke(Math.Min(100,rms*700));
        if(warmup<8000){noise+=(rms-noise)*.35;warmup+=samples.Length;if(warmup>=8000)Ready?.Invoke();return;}
        if(cooldown>0){cooldown-=samples.Length;return;}
        var voiced=rms>Math.Clamp(noise*2.8,.006,.06);
        if(!active){if(!voiced){foreach(var s in samples){before.Enqueue(s);if(before.Count>3200)before.Dequeue();}return;}active=true;current.AddRange(before);before.Clear();}
        current.AddRange(samples);quiet=voiced?0:quiet+samples.Length;
        if(quiet<8800&&current.Count<72000)return;
        var speech=current.Take(Math.Max(0,current.Count-Math.Max(0,quiet-1600))).ToArray();current.Clear();active=false;quiet=0;cooldown=4800;
        if(speech.Length>=6400&&speech.Length<=72000)Segment?.Invoke(speech);
    }
}
