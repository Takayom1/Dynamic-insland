# Original HoshiBar adapter. Copyright 2026 Takayom1. MIT.
# Uses an explicitly selected local Applio checkout; no audio is uploaded.
import argparse,os,sys
from pathlib import Path
p=argparse.ArgumentParser()
for flag in ['engine','model','index','input','output']:p.add_argument('--'+flag,required=True)
p.add_argument('--pitch',type=int,default=1)
p.add_argument('--index-rate',type=float,default=0.30)
p.add_argument('--protect',type=float,default=0.42)
a=p.parse_args()
root=Path(a.engine).resolve()
model_path=str(Path(a.model).resolve())
index_path=str(Path(a.index).resolve())
in_path=str(Path(a.input).resolve())
out_path=str(Path(a.output).resolve())
if not (root/'rvc/infer/infer.py').is_file():raise RuntimeError('Choose the Applio engine folder')
import torch
checkpoint=torch.load(model_path,map_location='cpu',weights_only=True)
if not isinstance(checkpoint,dict) or 'weight' not in checkpoint:raise RuntimeError('Invalid RVC checkpoint')
del checkpoint
os.environ['PATH']=str(root)+os.pathsep+os.environ.get('PATH','')
os.chdir(root);sys.path.insert(0,str(root));torch.set_num_threads(max(4,os.cpu_count() or 4))
from rvc.infer.infer import VoiceConverter
converter=VoiceConverter()
converter.convert_audio(audio_input_path=in_path,audio_output_path=out_path,model_path=model_path,index_path=index_path,pitch=a.pitch,f0_method='rmvpe',index_rate=a.index_rate,protect=a.protect,export_format='WAV',embedder_model='contentvec')
if not Path(out_path).is_file():raise RuntimeError('RVC output not created')
