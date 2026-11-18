# Copyright 2026 Takayom1. SPDX-License-Identifier: MIT
# Persistent local worker. Character/model weights retain their separate licenses.
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import time

protocol = sys.stdout
sys.stdout = sys.stderr  # Third-party engine logs must not corrupt the JSONL channel.

THREADS = max(4, os.cpu_count() or 4)


def send(kind, **values):
    protocol.write(json.dumps(dict(type=kind, **values), ensure_ascii=False) + "\n")
    protocol.flush()


def normalize_wav(wav_path: Path, target_rms_db: float = -18.0):
    import numpy as np
    import soundfile as sf

    y, sr = sf.read(str(wav_path), dtype="float32")
    if y.ndim > 1:
        y = y.mean(axis=1)
    non_silent = np.where(np.abs(y) > 0.002)[0]
    if len(non_silent) > 100:
        pad = int(0.06 * sr)
        start = max(0, int(non_silent[0]) - pad)
        end = min(len(y), int(non_silent[-1]) + pad)
        y = y[start:end]
    rms = float(np.sqrt(np.mean(y ** 2)) + 1e-9)
    target_rms = 10.0 ** (target_rms_db / 20.0)
    y = y * (target_rms / rms)
    peak = float(np.max(np.abs(y)) + 1e-9)
    if peak > 0.92:
        y = y * (0.92 / peak)
    sf.write(str(wav_path), y, sr, subtype="PCM_16")


class Engine:
    def __init__(self):
        self.torch = None
        self.tts = None
        self.converter = None
        self.rvc_model_path = None
        self.rvc_index_path = None
        self.recognizer = None
        self.recognition_device = "cpu"
        self.dll_directory = None

    def load_recognizer(self, request):
        if self.recognizer is not None:
            return
        send("stage", stage="load_recognizer")
        model = Path(request["whisper_model"]).resolve()
        if not (model / "model.bin").is_file():
            raise RuntimeError("Choose a local faster-whisper model folder")
        try:
            import torch
            lib = Path(torch.__file__).parent / "lib"
            if os.name == "nt" and lib.is_dir():
                self.dll_directory = os.add_dll_directory(str(lib))
                os.environ["PATH"] = str(lib) + os.pathsep + os.environ.get("PATH", "")
        except ImportError:
            pass
        import ctranslate2
        from faster_whisper import WhisperModel
        self.recognition_device = "cuda" if ctranslate2.get_cuda_device_count() else "cpu"
        compute = "int8_float32" if self.recognition_device == "cuda" else "int8"
        self.recognizer = WhisperModel(str(model), device=self.recognition_device, compute_type=compute, local_files_only=True, cpu_threads=min(8, THREADS))

    def transcribe(self, request):
        self.load_recognizer(request)
        send("stage", stage="transcribe", device=self.recognition_device)
        segments, _ = self.recognizer.transcribe(request["input"], language=request.get("language", "ru"),
            beam_size=5, vad_filter=False, condition_on_previous_text=False, temperature=0)
        return " ".join(segment.text.strip() for segment in segments).strip()

    def load(self, request):
        if self.torch is None:
            send("stage", stage="load_torch")
            import torch
            torch.set_num_threads(THREADS)
            self.torch = torch
        if request["neural"] and self.tts is None:
            send("stage", stage="load_neural")
            path = Path(request["neural_model"]).resolve()
            if hashlib.sha256(path.read_bytes()).hexdigest() != "50081637b602126ee06cb3bc8a744d25651d2da149ee8864b9a379bfdd934437":
                raise RuntimeError("Only the verified official Silero v5_5_ru model is supported")
            self.tts = self.torch.package.PackageImporter(str(path)).load_pickle("tts_models", "model")
            self.tts.to(self.torch.device("cpu"))
        if request["rvc"]:
            self.rvc_model_path = str(Path(request["rvc_model"]).resolve())
            self.rvc_index_path = str(Path(request["index"]).resolve())
            if self.converter is None:
                send("stage", stage="load_rvc")
                root = Path(request["engine"]).resolve()
                if not (root / "rvc/infer/infer.py").is_file():
                    raise RuntimeError("Choose the Applio engine folder")
                checkpoint = self.torch.load(self.rvc_model_path, map_location="cpu", weights_only=True)
                if not isinstance(checkpoint, dict) or "weight" not in checkpoint:
                    raise RuntimeError("Invalid RVC checkpoint")
                del checkpoint
                os.environ["PATH"] = str(root) + os.pathsep + os.environ.get("PATH", "")
                os.chdir(root)
                sys.path.insert(0, str(root))
                from rvc.infer.infer import VoiceConverter
                class WarmConverter(VoiceConverter):
                    def cleanup_model(self):
                        pass
                self.converter = WarmConverter()
                self.torch.set_num_threads(THREADS)

    def render(self, request, text, source, output):
        import soundfile as sf
        source = Path(source).resolve()
        output = Path(output).resolve()
        if request["neural"]:
            send("stage", stage="synthesize")
            with self.torch.inference_mode():
                audio = self.tts.apply_tts(text=text, speaker=request["speaker"], sample_rate=48000, put_accent=True, put_yo=True)
            sf.write(str(source), audio, 48000)
        if request["rvc"]:
            send("stage", stage="convert", device=self.converter.config.device)
            pitch = int(request.get("pitch", 1))
            index_rate = float(request.get("index_rate", 0.30))
            protect = float(request.get("protect", 0.42))
            with self.torch.inference_mode():
                self.converter.convert_audio(
                    audio_input_path=str(source),
                    audio_output_path=str(output),
                    model_path=self.rvc_model_path,
                    index_path=self.rvc_index_path,
                    pitch=pitch,
                    f0_method="rmvpe",
                    index_rate=index_rate,
                    protect=protect,
                    export_format="WAV",
                    embedder_model="contentvec",
                )
            normalize_wav(output)
        else:
            import shutil
            shutil.copyfile(str(source), str(output))
        if not output.is_file() or output.stat().st_size < 44:
            raise RuntimeError("Voice output not created")
        if output.stat().st_size > 50 * 1024 * 1024:
            raise RuntimeError("Voice output exceeds 50 MB")

    def prepare(self, request):
        self.load(request)
        with tempfile.TemporaryDirectory(prefix="hoshino-warm-") as folder:
            source, output = Path(folder) / "source.wav", Path(folder) / "output.wav"
            if not request["neural"]:
                import numpy as np
                import soundfile as sf
                sf.write(str(source), .05 * np.sin(2 * np.pi * 180 * np.arange(16000) / 16000), 16000)
            self.render(request, "Я здесь.", source, output)


engine = Engine()
send("started")
for line in sys.stdin:
    try:
        request = json.loads(line)
        started = time.perf_counter()
        recognized = None
        if request["action"] == "prepare_asr":
            engine.load_recognizer(request)
        elif request["action"] == "transcribe":
            recognized = engine.transcribe(request)
        elif request["action"] == "prepare":
            engine.prepare(request)
        elif request["action"] == "render":
            engine.load(request)
            text = Path(request["text_file"]).read_text(encoding="utf-8-sig") if request["neural"] else ""
            engine.render(request, text, Path(request["input"]), Path(request["output"]))
        else:
            raise ValueError("Unknown voice action")
        device = engine.recognition_device if request["action"] in ("prepare_asr", "transcribe") else engine.converter.config.device if engine.converter else "cpu"
        send("done", seconds=round(time.perf_counter() - started, 3), device=device, text=recognized)
    except Exception as error:
        send("error", message=str(error)[-1200:])
