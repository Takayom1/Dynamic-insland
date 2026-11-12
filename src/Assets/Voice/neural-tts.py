# Original Hoshino Island adapter, Copyright 2026 Takayom1, MIT.
# Silero weights are separate and retain their own non-commercial license.
import argparse,hashlib
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--model',required=True);p.add_argument('--text',required=True);p.add_argument('--output',required=True);p.add_argument('--voice',default='baya');a=p.parse_args()
model_file=Path(a.model)
if hashlib.sha256(model_file.read_bytes()).hexdigest()!='50081637b602126ee06cb3bc8a744d25651d2da149ee8864b9a379bfdd934437':raise RuntimeError('Only the verified official Silero v5_5_ru model is supported')
import torch,soundfile as sf
# This specific official model is executable torch.package code, verified above.
torch.set_num_threads(4);model=torch.package.PackageImporter(str(model_file)).load_pickle('tts_models','model');model.to(torch.device('cpu'))
text=Path(a.text).read_text(encoding='utf-8-sig')
audio=model.apply_tts(text=text,speaker=a.voice,sample_rate=48000,put_accent=True,put_yo=True);sf.write(a.output,audio,48000)
