# Copyright 2026 Takayom1. SPDX-License-Identifier: MIT
# Persistent local worker. Character/model weights retain their separate licenses.
import contextlib
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import time

protocol = sys.stdout
sys.stdout = sys.stderr  # Third-party engine logs must not corrupt the JSONL channel.


def send(kind, **values):
    protocol.write(json.dumps(dict(type=kind, **values), ensure_ascii=False) + "\n")
    protocol.flush()


class Engine:
    def __init__(self):
        self.torch = None
        self.tts = None
        self.converter = None

    def load(self, request):
        if self.torch is None:
            send("stage", stage="load_torch")
            import torch
            torch.set_num_threads(4)
            self.torch = torch
        if request["neural"] and self.tts is None:
            send("stage", stage="load_neural")
            path = Path(request["neural_model"])
            if hashlib.sha256(path.read_bytes()).hexdigest() != "50081637b602126ee06cb3bc8a744d25651d2da149ee8864b9a379bfdd934437":
                raise RuntimeError("Only the verified official Silero v5_5_ru model is supported")
            self.tts = self.torch.package.PackageImporter(str(path)).load_pickle("tts_models", "model")
            self.tts.to(self.torch.device("cpu"))
        if request["rvc"] and self.converter is None:
            send("stage", stage="load_rvc")
            root = Path(request["engine"]).resolve()
            if not (root / "rvc/infer/infer.py").is_file():
                raise RuntimeError("Choose the Applio engine folder")
            checkpoint = self.torch.load(request["rvc_model"], map_location="cpu", weights_only=True)
            if not isinstance(checkpoint, dict) or "weight" not in checkpoint:
                raise RuntimeError("Invalid RVC checkpoint")
            del checkpoint
            os.chdir(root)
            sys.path.insert(0, str(root))
            from rvc.infer.infer import VoiceConverter
            class WarmConverter(VoiceConverter):
                # Applio's file-conversion path otherwise unloads every model
                # after each WAV. This dedicated process owns one voice config.
                def cleanup_model(self):
                    pass
            self.converter = WarmConverter()

    def render(self, request, text, source, output):
        import soundfile as sf
        if request["neural"]:
            send("stage", stage="synthesize")
            with self.torch.inference_mode():
                audio = self.tts.apply_tts(text=text, speaker=request["speaker"], sample_rate=48000, put_accent=True, put_yo=True)
            sf.write(source, audio, 48000)
        if request["rvc"]:
            send("stage", stage="convert", device=self.converter.config.device)
            with self.torch.inference_mode():
                self.converter.convert_audio(audio_input_path=str(source), audio_output_path=str(output),
                    model_path=request["rvc_model"], index_path=request["index"], pitch=0,
                    f0_method="rmvpe", index_rate=0.5, protect=0.33, export_format="WAV", embedder_model="contentvec")
        else:
            import shutil
            shutil.copyfile(source, output)
        if not Path(output).is_file() or Path(output).stat().st_size < 44:
            raise RuntimeError("Voice output not created")
        if Path(output).stat().st_size > 50 * 1024 * 1024:
            raise RuntimeError("Voice output exceeds 50 MB")

    def prepare(self, request):
        self.load(request)
        # Warm lazy pitch/embedder kernels too; this generated phrase is never played.
        with tempfile.TemporaryDirectory(prefix="hoshino-warm-") as folder:
            source, output = Path(folder) / "source.wav", Path(folder) / "output.wav"
            if not request["neural"]:
                import numpy as np
                import soundfile as sf
                sf.write(source, .05 * np.sin(2 * np.pi * 180 * np.arange(16000) / 16000), 16000)
            self.render(request, "Я здесь.", source, output)


engine = Engine()
send("started")
for line in sys.stdin:
    try:
        request = json.loads(line)
        started = time.perf_counter()
        if request["action"] == "prepare":
            engine.prepare(request)
        elif request["action"] == "render":
            engine.load(request)
            text = Path(request["text_file"]).read_text(encoding="utf-8-sig") if request["neural"] else ""
            engine.render(request, text, Path(request["input"]), Path(request["output"]))
        else:
            raise ValueError("Unknown voice action")
        send("done", seconds=round(time.perf_counter() - started, 3), device=engine.converter.config.device if engine.converter else "cpu")
    except Exception as error:
        send("error", message=str(error)[-1200:])
