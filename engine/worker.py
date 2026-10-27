"""Движок LocalWhisper: звук, распознавание, супер-режим — без единого окна.

Здесь живёт всё, ради чего в этом хозяйстве вообще остался Python:
`faster-whisper` — питоновская обёртка над CTranslate2, и модели на диске лежат
в её формате. Переписать это на C# значило бы поменять движок, а не перенести
приложение.

Всё остальное — окна, клавиши, трей, настройки, история — уехало в C#. Разговор
между ними построчный JSON через stdin/stdout, тот же протокол, что у плагинов
Popup (`docs/plugins.md`).

    C# -> воркер   {"type":"config","config":{...}}
                   {"type":"start","mode":{...}}
                   {"type":"stop"}
                   {"type":"cancel"}
                   {"type":"mics"}
                   {"type":"ping"}
                   {"type":"shutdown"}

    воркер -> C#   {"type":"ready"}
                   {"type":"model","state":"loading|progress|loaded|error","message":"…"}
                   {"type":"device","device":"cpu","compute_type":"float32"}
                   {"type":"started"}
                   {"type":"silence"}
                   {"type":"processing"}
                   {"type":"result","raw":"…","clean":"…","duration":2.4}
                   {"type":"empty"}
                   {"type":"warning","message":"…"}
                   {"type":"error","message":"…"}
                   {"type":"mics","items":["…"]}
                   {"type":"log","message":"…"}

Что здесь неочевидно и важно:

* **stdout — это протокол, и больше ничего.** Любая библиотека может напечатать
  предупреждение в самый неподходящий момент и сломать разбор строки. Поэтому
  настоящий stdout забирается себе один раз при старте, а `sys.stdout`
  подменяется на stderr: чужой `print` уходит в журнал, а не в канал.

* **Тишина заканчивает диктовку, а не только запись.** В старом Qt-приложении
  детектор тишины останавливал только захват звука, а пилюля продолжала
  показывать «идёт запись» с бегущим таймером, пока человек не нажмёт клавишу
  ещё раз. Настройка `silence_duration` при этом почти ничего не значила.
  Здесь тишина доводит дело до конца — расшифровка начинается сама.
"""

import json
import os
import sys
import threading
import time
import wave
import tempfile
from pathlib import Path

# Настоящий stdout забираем себе, пока до него никто не добрался.
_CHANNEL = sys.stdout
try:
    _CHANNEL.reconfigure(encoding="utf-8")
except Exception:
    pass
try:
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass
# Всё, что напечатают библиотеки, уходит в stderr — C# кладёт это в журнал.
sys.stdout = sys.stderr

import numpy as np  # noqa: E402
import requests  # noqa: E402
import sounddevice as sd  # noqa: E402

_WRITE_LOCK = threading.Lock()


def send(message):
    """Одно сообщение — одна строка. Без flush C# не увидит её до выхода."""
    line = json.dumps(message, ensure_ascii=False) + "\n"
    with _WRITE_LOCK:
        try:
            _CHANNEL.write(line)
            _CHANNEL.flush()
        except Exception:
            pass


def log(message):
    send({"type": "log", "message": str(message)})


# ─────────────────────────────────────────────
# КОНФИГ ПО УМОЛЧАНИЮ
# Дублирует DEFAULT_CONFIG старого app.py, чтобы воркер можно было запустить
# руками из консоли и он работал бы без единого сообщения от C#.
# ─────────────────────────────────────────────
DEFAULT_CONFIG = {
    "whisper_provider": "local",
    "whisper_model": "large-v3-turbo",
    "whisper_device": "cuda",
    "whisper_compute_type": "float16",
    "llm_provider": "ollama",
    "ollama_url": "http://localhost:11434",
    "ollama_model": "llama3.2",
    "openai_api_key": "",
    "openai_url": "https://api.openai.com/v1",
    "openai_model": "gpt-5.4-mini",
    "sample_rate": 16000,
    "silence_threshold": 0.003,
    "silence_duration": 2.0,
    "vocabulary": [],
}

MODEL_REPOS = {
    "tiny": {
        "repo": "Systran/faster-whisper-tiny",
        "files": ["config.json", "model.bin", "tokenizer.json", "vocabulary.txt"],
    },
    "base": {
        "repo": "Systran/faster-whisper-base",
        "files": ["config.json", "model.bin", "tokenizer.json", "vocabulary.txt"],
    },
    "small": {
        "repo": "Systran/faster-whisper-small",
        "files": ["config.json", "model.bin", "tokenizer.json", "vocabulary.txt"],
    },
    "medium": {
        "repo": "Systran/faster-whisper-medium",
        "files": ["config.json", "model.bin", "tokenizer.json", "vocabulary.txt"],
    },
    "large-v3": {
        "repo": "Systran/faster-whisper-large-v3",
        "files": ["config.json", "model.bin", "preprocessor_config.json",
                  "tokenizer.json", "vocabulary.json"],
    },
    "large-v3-turbo": {
        "repo": "deepdml/faster-whisper-large-v3-turbo-ct2",
        "files": ["config.json", "model.bin", "preprocessor_config.json",
                  "tokenizer.json", "vocabulary.json"],
    },
    "distil-large-v3": {
        "repo": "Systran/faster-distil-whisper-large-v3",
        "files": ["config.json", "model.bin", "preprocessor_config.json",
                  "tokenizer.json", "vocabulary.json"],
    },
}

MODELS_DIR = Path.home() / ".localwhisper" / "models"


# ─────────────────────────────────────────────
# СУПЕР-РЕЖИМ
# ─────────────────────────────────────────────
def start_ollama_if_needed(config):
    if str(config.get("llm_provider", "ollama")).lower() != "ollama":
        return
    url = config.get("ollama_url", "http://localhost:11434")
    try:
        if requests.get(url, timeout=1.5).status_code == 200:
            log("[Ollama] сервер уже на связи")
            return
    except Exception:
        pass

    if not any(m.get("super_mode") for m in config.get("modes", [])):
        return

    log("[Ollama] сервер молчит, поднимаю в фоне")
    try:
        import subprocess

        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        # CREATE_NO_WINDOW = 0x08000000
        subprocess.Popen(["ollama", "serve"], startupinfo=startupinfo, creationflags=0x08000000)
    except Exception as e:
        log(f"[Ollama] не поднялся: {e}")


def super_mode_process(text: str, config: dict, system_prompt: str) -> str:
    if not text:
        return text

    provider = str(config.get("llm_provider", "ollama")).lower()
    prompt = system_prompt or "Fix punctuation, spelling, grammar. Keep language. Return ONLY cleaned text."

    if provider == "openai":
        url = str(config.get("openai_url", "https://api.openai.com/v1")).rstrip("/")
        model = config.get("openai_model", "gpt-5.4-mini")
        api_key = config.get("openai_api_key", "") or os.environ.get("OPENAI_API_KEY", "")
        if not api_key:
            raise RuntimeError("OpenAI API key is missing (add it in Settings or set OPENAI_API_KEY)")

        payload = {
            "model": model,
            "messages": [
                {"role": "system", "content": prompt},
                {"role": "user", "content": text},
            ],
            "temperature": 0.1,
        }
        try:
            log(f"[SuperMode] OpenAI: {model} на {url}")
            resp = requests.post(
                f"{url}/chat/completions",
                json=payload,
                headers={"Authorization": f"Bearer {api_key}", "Content-Type": "application/json"},
                timeout=15,
            )
            resp.raise_for_status()
            result = resp.json()["choices"][0]["message"]["content"].strip()
            return result or text
        except Exception as e:
            raise RuntimeError(f"OpenAI ({model}): {e}")

    url = str(config.get("ollama_url", "http://localhost:11434")).rstrip("/")
    model = config.get("ollama_model", "llama3.2")
    try:
        log(f"[SuperMode] Ollama: {model} на {url}")
        resp = requests.post(
            f"{url}/api/generate",
            json={
                "model": model,
                "prompt": f"{prompt}\n\nRaw text:\n{text}",
                "stream": False,
                "options": {"temperature": 0.1},
            },
            timeout=15,
        )
        resp.raise_for_status()
        result = resp.json().get("response", "").strip()
        return result or text
    except Exception as e:
        raise RuntimeError(f"Ollama ({model}): {e}")


# ─────────────────────────────────────────────
# ЗАПИСЬ ЗВУКА
# ─────────────────────────────────────────────
class Recorder:
    """Захват с микрофона с остановкой по тишине.

    Тишина считается только после того, как человек заговорил: пауза перед
    началом или раздумье над первой фразой не должны обрывать запись.
    """

    def __init__(self, config, on_silence=None):
        self.config = config
        self.on_silence = on_silence
        self.recording = False
        self.audio_data = []
        self.error_occurred = None
        self._lock = threading.Lock()

    def start(self):
        with self._lock:
            if self.recording:
                return
            self.recording = True
            self.audio_data = []
            self.error_occurred = None
        threading.Thread(target=self._record_loop, daemon=True).start()

    def stop(self):
        with self._lock:
            self.recording = False
        time.sleep(0.05)
        if not self.audio_data:
            return None
        try:
            return np.concatenate(self.audio_data)
        except Exception:
            return None

    def _device_index(self):
        target_name = self.config.get("active_mic_name", "")
        if not target_name:
            return None
        try:
            for idx, dev in enumerate(sd.query_devices()):
                if dev.get("name") == target_name and dev.get("max_input_channels", 0) > 0:
                    return idx
        except Exception as e:
            log(f"[Recorder] не найти микрофон по имени: {e}")
        return None

    def _record_loop(self):
        sr = int(self.config.get("sample_rate", 16000))
        silence_frames = int(float(self.config.get("silence_duration", 2.0)) * sr)
        threshold = float(self.config.get("silence_threshold", 0.003))
        silent_count = 0
        speech_started = False
        hit_silence = False

        device_idx = self._device_index()

        def callback(indata, frames, time_info, status):
            if not self.recording:
                raise sd.CallbackStop()
            chunk = indata[:, 0].copy()
            self.audio_data.append(chunk)

            rms = np.sqrt(np.mean(chunk ** 2))
            nonlocal silent_count, speech_started, hit_silence
            if rms >= threshold:
                speech_started = True
                silent_count = 0
            elif speech_started:
                silent_count += frames
            if speech_started and silent_count >= silence_frames:
                hit_silence = True
                self.recording = False
                raise sd.CallbackStop()

        try:
            with sd.InputStream(device=device_idx, samplerate=sr, channels=1,
                                dtype="float32", callback=callback):
                while self.recording:
                    time.sleep(0.05)
        except sd.CallbackStop:
            pass
        except Exception as e:
            self.error_occurred = str(e)
        finally:
            self.recording = False

        if hit_silence and self.on_silence:
            self.on_silence()


# ─────────────────────────────────────────────
# ВОРКЕР
# ─────────────────────────────────────────────
class Worker:
    def __init__(self):
        self.config = dict(DEFAULT_CONFIG)
        self.configured = False
        self.model = None
        self.model_loaded = False
        self.cancel_download = False
        self.recorder = None
        self.mode = None
        self.state = "idle"          # idle | recording | processing
        self._state_lock = threading.Lock()
        self._load_generation = 0

    # MARK: - Настройки

    def apply_config(self, config):
        previous = self.config
        first_time = not self.configured
        self.configured = True
        self.config = {**DEFAULT_CONFIG, **(config or {})}

        start_ollama_if_needed(self.config)

        keys = ("whisper_provider", "whisper_model", "whisper_device", "whisper_compute_type")
        if first_time or any(previous.get(k) != self.config.get(k) for k in keys):
            self.load_model_async()
            return

        # Движок не меняли — перезагружать модель не за чем. Но сказать, что она
        # на месте, обязаны: сторона C# гасит готовность на каждой отправке
        # настроек, и без этого ответа добавленное в словарь слово навсегда
        # запрещало бы запись.
        send({
            "type": "model",
            "state": "loaded" if self.model_loaded else "loading",
            "message": self.config.get("whisper_model", ""),
        })

    # MARK: - Модель

    def load_model_async(self):
        self._load_generation += 1
        generation = self._load_generation
        self.cancel_download = True
        self.model_loaded = False
        threading.Thread(target=self._load_model, args=(generation,), daemon=True).start()

    def _load_model(self, generation):
        # Дать предыдущей загрузке заметить отмену и выйти.
        time.sleep(0.2)
        if generation != self._load_generation:
            return
        self.cancel_download = False

        if self.config.get("whisper_provider", "local") == "openai":
            self.model = None
            self.model_loaded = True
            send({"type": "model", "state": "loaded", "message": "OpenAI API"})
            return

        model_name = self.config.get("whisper_model", "large-v3-turbo")
        device = self.config.get("whisper_device", "cuda")
        compute_type = self.config.get("whisper_compute_type", "float16")

        send({"type": "model", "state": "loading", "message": model_name})

        try:
            model_path = self.ensure_model_downloaded(model_name)
            if model_path is None:
                return
        except Exception as e:
            send({"type": "model", "state": "error", "message": f"Download failed: {e}"})
            return

        if generation != self._load_generation:
            return

        from faster_whisper import WhisperModel

        try:
            log(f"[Model] загружаю {model_name} на {device} ({compute_type})")
            self.model = WhisperModel(model_path, device=device, compute_type=compute_type)
            self.model_loaded = True
            send({"type": "model", "state": "loaded", "message": f"{model_name} · {device}"})
            return
        except Exception as e:
            if self.cancel_download or generation != self._load_generation:
                return
            log(f"[Model] {device} не поднялся: {e}")
            if device != "cuda":
                send({"type": "model", "state": "error", "message": str(e)})
                return

        # Откат на процессор: медленнее, но работает всегда.
        try:
            self.config["whisper_device"] = "cpu"
            self.config["whisper_compute_type"] = "float32"
            self.model = WhisperModel(model_path, device="cpu", compute_type="float32")
            self.model_loaded = True
            send({"type": "device", "device": "cpu", "compute_type": "float32"})
            send({"type": "model", "state": "loaded", "message": f"{model_name} · cpu"})
        except Exception as e:
            if self.cancel_download or generation != self._load_generation:
                return
            send({"type": "model", "state": "error", "message": f"CPU fallback failed: {e}"})

    def ensure_model_downloaded(self, model_name):
        """Скачивает модель, если её нет. Возвращает путь или None при отмене."""
        if os.path.isdir(model_name):
            return model_name
        if model_name not in MODEL_REPOS:
            return model_name

        info = MODEL_REPOS[model_name]
        repo, files = info["repo"], info["files"]

        local_model_dir = MODELS_DIR / model_name
        local_model_dir.mkdir(parents=True, exist_ok=True)

        if all((local_model_dir / f).exists() and (local_model_dir / f).stat().st_size > 0 for f in files):
            log(f"[Downloader] модель '{model_name}' уже на месте")
            return str(local_model_dir)

        # Зеркало на случай, если до huggingface.co не достучаться.
        domain = "huggingface.co"
        try:
            requests.head("https://huggingface.co", timeout=3)
        except Exception as e:
            log(f"[Downloader] huggingface.co недоступен ({e}), беру hf-mirror.com")
            domain = "hf-mirror.com"

        for f in files:
            file_path = local_model_dir / f
            temp_file_path = file_path.with_suffix(".tmp")

            success = False
            max_retries = 5
            attempt = 0

            while not success and attempt < max_retries:
                if self.cancel_download:
                    return None
                attempt += 1
                if attempt > 1 and domain == "huggingface.co":
                    log("[Downloader] повтор через hf-mirror.com")
                    domain = "hf-mirror.com"

                url = f"https://{domain}/{repo}/resolve/main/{f}"
                log(f"[Downloader] ({attempt}/{max_retries}) {f} ← {url}")

                # Докачка больших весов с того места, где оборвалось.
                resume_headers = {}
                current_size = 0
                if f == "model.bin" and temp_file_path.exists():
                    current_size = temp_file_path.stat().st_size
                    if current_size > 0:
                        resume_headers["Range"] = f"bytes={current_size}-"
                        log(f"[Downloader] продолжаю {f} с байта {current_size}")

                try:
                    r = requests.get(url, headers=resume_headers, stream=True, timeout=(15, 30))

                    if resume_headers and r.status_code == 206:
                        content_range = r.headers.get("Content-Range", "")
                        if "/" in content_range:
                            total_size = int(content_range.split("/")[-1])
                        else:
                            total_size = current_size + int(r.headers.get("content-length", 0))
                        mode = "ab"
                    else:
                        if r.status_code == 416:
                            # Диапазон не удовлетворить — значит файл уже целиком наш.
                            log(f"[Downloader] 416 для {f}: файл уже скачан")
                            if temp_file_path.exists() and self._promote(temp_file_path, file_path):
                                success = True
                                break
                        total_size = int(r.headers.get("content-length", 0))
                        current_size = 0
                        mode = "wb"
                        r.raise_for_status()

                    downloaded = current_size
                    last_progress_time = time.time()
                    last_sent_pct = -1
                    stall_timeout = 30

                    with open(temp_file_path, mode) as out_f:
                        for chunk in r.iter_content(chunk_size=512 * 1024):
                            if self.cancel_download:
                                log("[Downloader] отменено")
                                out_f.close()
                                return None
                            if chunk:
                                out_f.write(chunk)
                                downloaded += len(chunk)
                                last_progress_time = time.time()
                                if total_size > 0:
                                    percent = int(downloaded / total_size * 100)
                                    if percent >= last_sent_pct + 2:
                                        last_sent_pct = percent
                                        send({"type": "model", "state": "progress",
                                              "message": f"{model_name} · {f}: {percent}%"})
                                else:
                                    send({"type": "model", "state": "progress",
                                          "message": f"{model_name} · {f}: {downloaded // 1024} KB"})
                            elif time.time() - last_progress_time > stall_timeout:
                                raise ConnectionError(f"загрузка встала: {stall_timeout}с без данных")

                    if temp_file_path.exists() and not self._promote(temp_file_path, file_path):
                        raise OSError(f"не переименовать {temp_file_path} в {file_path}")

                    success = True
                    log(f"[Downloader] {f} готов")
                except Exception as e:
                    log(f"[Downloader] попытка {attempt} для {f} не удалась: {e}")
                    if attempt < max_retries:
                        time.sleep(2)
                    else:
                        raise

        log(f"[Downloader] модель '{model_name}' скачана целиком")
        return str(local_model_dir)

    @staticmethod
    def _promote(temp_file_path, file_path):
        """Переименовать временный файл в настоящий, переживая файловые замки.

        Антивирус или индексатор успевают взять только что записанный файл, и
        первое переименование падает. Пять попыток по секунде хватает.
        """
        if file_path.exists():
            try:
                file_path.unlink()
            except Exception:
                pass
        for attempt in range(5):
            try:
                temp_file_path.rename(file_path)
                return True
            except OSError as e:
                log(f"[Downloader] переименование {attempt + 1} не вышло ({e}), жду секунду")
                time.sleep(1.0)
        return False

    # MARK: - Запись

    def start_recording(self, mode):
        with self._state_lock:
            if self.state != "idle":
                return
            self.state = "recording"

        if not self.model_loaded:
            with self._state_lock:
                self.state = "idle"
            send({"type": "error", "message": "Model is still loading"})
            return

        self.mode = mode or {}
        self.recorder = Recorder(self.config, on_silence=self._on_silence)
        self.recorder.start()
        send({"type": "started"})

    def _on_silence(self):
        send({"type": "silence"})
        self.stop_recording()

    def stop_recording(self):
        with self._state_lock:
            if self.state != "recording":
                return
            self.state = "processing"

        send({"type": "processing"})
        recorder, self.recorder = self.recorder, None
        audio = recorder.stop() if recorder else None

        if recorder and recorder.error_occurred:
            self._finish()
            send({"type": "error", "message": recorder.error_occurred})
            return

        sr = int(self.config.get("sample_rate", 16000))
        # Короче 200 мс — это промах по клавише, а не диктовка. Молча в покой.
        if audio is None or len(audio) < sr * 0.2:
            self._finish()
            send({"type": "empty"})
            return

        threading.Thread(target=self._transcribe, args=(audio, self.mode), daemon=True).start()

    def cancel_recording(self):
        with self._state_lock:
            if self.state != "recording":
                return
            self.state = "idle"
        recorder, self.recorder = self.recorder, None
        if recorder:
            recorder.stop()
        send({"type": "empty"})

    def _finish(self):
        with self._state_lock:
            self.state = "idle"

    # MARK: - Распознавание

    def _transcribe(self, audio, mode):
        sr = int(self.config.get("sample_rate", 16000))
        duration = len(audio) / float(sr)

        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
            tmp_path = f.name

        try:
            with wave.open(tmp_path, "wb") as wf:
                wf.setnchannels(1)
                wf.setsampwidth(2)
                wf.setframerate(sr)
                wf.writeframes((audio * 32767).astype(np.int16).tobytes())

            lang = None if mode.get("language") == "auto" else mode.get("language")
            vocab = self.config.get("vocabulary", []) or []
            initial_prompt = ", ".join(vocab) if vocab else None

            if self.config.get("whisper_provider", "local") == "openai":
                raw_text = self._transcribe_openai(tmp_path, lang, initial_prompt)
            else:
                raw_text = self._transcribe_local(tmp_path, lang, initial_prompt, mode)

            # Чистка через LLM не должна стоить диктовки: сорвалась — отдаём
            # сырой текст и говорим об этом отдельно.
            clean_text = raw_text
            if raw_text and mode.get("super_mode"):
                try:
                    clean_text = super_mode_process(raw_text, self.config, mode.get("system_prompt", ""))
                except Exception as e:
                    log(f"[SuperMode] чистка не удалась, беру сырой текст: {e}")
                    send({"type": "warning", "message": f"LLM cleanup failed: {e}"})
                    clean_text = raw_text

            send({"type": "result", "raw": raw_text, "clean": clean_text, "duration": duration})
        except Exception as e:
            send({"type": "error", "message": str(e)})
        finally:
            self._finish()
            try:
                os.unlink(tmp_path)
            except Exception:
                pass

    def _transcribe_local(self, tmp_path, lang, initial_prompt, mode):
        log(f"[Transcribe] локально, режим '{mode.get('name')}', язык '{lang}'")
        segments, _ = self.model.transcribe(
            tmp_path,
            language=lang,
            beam_size=5,
            vad_filter=True,
            vad_parameters=dict(threshold=0.35),
            initial_prompt=initial_prompt,
        )
        return " ".join(s.text.strip() for s in segments).strip()

    def _transcribe_openai(self, tmp_path, lang, initial_prompt):
        url = str(self.config.get("openai_url", "https://api.openai.com/v1")).rstrip("/")
        api_key = self.config.get("openai_api_key", "") or os.environ.get("OPENAI_API_KEY", "")
        if not api_key:
            raise ValueError("OpenAI API key is missing. Please configure it in Settings.")

        log(f"[Transcribe] OpenAI API, язык '{lang}'")
        with open(tmp_path, "rb") as audio_file:
            files = {"file": (os.path.basename(tmp_path), audio_file, "audio/wav")}
            data = {"model": "whisper-1"}
            if lang:
                data["language"] = lang
            if initial_prompt:
                data["prompt"] = initial_prompt
            resp = requests.post(
                f"{url}/audio/transcriptions",
                headers={"Authorization": f"Bearer {api_key}"},
                files=files,
                data=data,
                timeout=30,
            )
            resp.raise_for_status()
            return resp.json().get("text", "").strip()

    # MARK: - Микрофоны

    def send_mics(self):
        items = []
        try:
            for dev in sd.query_devices():
                if dev.get("max_input_channels", 0) > 0:
                    name = dev.get("name", "")
                    if name and name not in items:
                        items.append(name)
        except Exception as e:
            log(f"[Audio] не перечислить микрофоны: {e}")
        send({"type": "mics", "items": items})

    # MARK: - Цикл команд

    def run(self):
        send({"type": "ready"})
        for line in sys.stdin:
            # Метку порядка байтов в начале потока снимаем сами: тот, кто пишет
            # нам в stdin, может её поставить, и первая же команда перестанет
            # быть JSON-ом.
            line = line.lstrip("﻿").strip()
            if not line:
                continue
            try:
                message = json.loads(line)
            except json.JSONDecodeError:
                log(f"не разобрать строку: {line[:120]}")
                continue

            kind = message.get("type")
            try:
                if kind == "config":
                    self.apply_config(message.get("config"))
                elif kind == "start":
                    self.start_recording(message.get("mode"))
                elif kind == "stop":
                    self.stop_recording()
                elif kind == "cancel":
                    self.cancel_recording()
                elif kind == "mics":
                    self.send_mics()
                elif kind == "ping":
                    send({"type": "pong"})
                elif kind == "shutdown":
                    break
                else:
                    log(f"неизвестная команда: {kind}")
            except Exception as e:
                send({"type": "error", "message": f"{kind}: {e}"})

        self.cancel_download = True


if __name__ == "__main__":
    Worker().run()
