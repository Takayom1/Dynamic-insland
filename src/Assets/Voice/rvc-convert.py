# Original Hoshino Island adapter. Copyright 2026 Takayom1. MIT.
# Uses an explicitly selected local Applio checkout; no audio is uploaded.
import argparse,os,sys
from pathlib import Path
p=argparse.ArgumentParser()
for flag in ['engine','model','index','input','output']:p.add_argument('--'+flag,required=True)
a=p.parse_args()
root=Path(a.engine).resolve()
if not (root/'rvc/infer/infer.py').is_file():raise RuntimeError('Choose the Applio engine folder')
# Restrict checkpoint loading to PyTorch safe tensor/primitive deserialization.
import torch
checkpoint=torch.load(a.model,map_location='cpu',weights_only=True)
if not isinstance(checkpoint,dict) or 'weight' not in checkpoint:raise RuntimeError('Invalid RVC checkpoint')
del checkpoint
os.chdir(root);sys.path.insert(0,str(root));torch.set_num_threads(min(4,os.cpu_count() or 2))
from rvc.infer.infer import VoiceConverter
converter=VoiceConverter()
converter.convert_audio(audio_input_path=str(Path(a.input).resolve()),audio_output_path=str(Path(a.output).resolve()),model_path=str(Path(a.model).resolve()),index_path=str(Path(a.index).resolve()),pitch=0,f0_method='rmvpe',index_rate=0.5,protect=0.33,export_format='WAV',embedder_model='contentvec')
if not Path(a.output).is_file():raise RuntimeError('RVC output not created')
