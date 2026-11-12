# Copyright 2026 Takayom1. MIT. Build-time preparation, not microphone capture.
import hashlib,urllib.request,zipfile,tempfile
from pathlib import Path
URL='https://alphacephei.com/vosk/models/vosk-model-small-ru-0.22.zip'
SHA256='961d5ff98a17f4aa6de69864d0aa71fa5bac682301d2b5d17a3f24c5c99a46d4'
root=Path(__file__).resolve().parents[1]/'src/Assets/Speech'
root.mkdir(parents=True,exist_ok=True)
with tempfile.TemporaryDirectory() as temp:
 archive=Path(temp)/'model.zip';urllib.request.urlretrieve(URL,archive)
 if hashlib.sha256(archive.read_bytes()).hexdigest()!=SHA256:raise RuntimeError('Model checksum mismatch')
 with zipfile.ZipFile(archive) as z:
  for member in z.infolist():
   if not (root/member.filename).resolve().is_relative_to(root.resolve()):raise RuntimeError('Unsafe model path')
  z.extractall(root)
print('Russian Vosk model prepared. Model: Apache-2.0; source: '+URL)
