# LocalWhisper — локальный клон Superwhisper для Windows

Диктовка по горячей клавише: говоришь → текст вставляется туда, где курсор.
Распознавание локальное (faster-whisper, GPU/CPU), опционально — «Super Mode»
с вычиткой языковой моделью (OpenAI или локальный Ollama).

**Стек:** C# / WPF (окно, клавиши, трей, настройки, пилюля) · Python-воркер
(звук и распознавание) · faster-whisper · OpenAI/Ollama (опц.)

---

## Из чего это собрано

Приложение состоит из двух процессов, и это не усложнение, а единственный
честный способ жить дальше:

| Часть | Чем занимается |
|---|---|
| `src/LocalWhisper` (C#, WPF) | окно, горячие клавиши, трей, конфиг, история, вставка текста, пилюля, связь с Popup |
| `engine/worker.py` (Python) | захват звука, определение тишины, распознавание, супер-режим, скачивание моделей |

**Почему движок остался на Python.** `faster-whisper` — это питоновская обёртка
над CTranslate2, и три гигабайта моделей в `~/.localwhisper/models/` лежат в её
формате. Переезд на Whisper.net потребовал бы других моделей, других весов и
дал бы другое качество — это была бы замена движка, а не перенос приложения.

Между процессами — построчный JSON через stdin/stdout, тот же протокол, что у
плагинов [Popup](../popup/docs/plugins.md). Воркер отлаживается запуском руками:

```bash
python engine/worker.py
{"type":"config","config":{"whisper_model":"tiny","whisper_device":"cuda","whisper_compute_type":"float16"}}
{"type":"start","mode":{"name":"Test","language":"ru","super_mode":false}}
{"type":"stop"}
```

Старый PyQt6-вариант (`app.py`, `pill_widget.py`, `settings_window.py`,
`popup_bridge.py`) удалён после приёмки — он весь целиком переехал сюда. Если
понадобится посмотреть, как было: `git show HEAD:app.py`.

---

## Установка

```bash
pip install -r requirements.txt
dotnet publish src/LocalWhisper -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Получится `publish/LocalWhisper.exe` (~25 МБ) и папка `publish/engine/` рядом с
ним. Нужны **.NET 9 Runtime** и Python с пакетами из `requirements.txt` — exe
содержит интерфейс целиком, но не движок.

Первый запуск скачает модель Whisper в `~/.localwhisper/models` (turbo ≈ 1.6 ГБ);
прогресс виден в левом нижнем углу окна. Появится пилюля в правом нижнем углу;
приложение живёт в трее.

> **GPU (NVIDIA):** для CUDA нужны `nvidia-cuda-runtime-cu12` и
> `nvidia-cublas-cu12` (или `pip install faster-whisper[cuda]`). Без GPU
> приложение само откатится на CPU — тогда возьми модель поменьше
> (`small`/`base`) и тип вычислений `int8`, иначе будет медленно.

Тесты:

```bash
dotnet test tests/LocalWhisper.Tests/LocalWhisper.Tests.csproj
```

---

## Использование

| Действие | Клавиши |
|---|---|
| Диктовка (активный режим) | `Ctrl+Alt+Space` |
| Super Mode (диктовка + вычитка) | `Ctrl+Shift+Space` |
| Остановить досрочно | та же клавиша ещё раз |
| Авто-стоп по тишине | пауза ~2 сек **после** начала речи |
| Сменить активный режим | `Ctrl+Shift+M` |

Своя клавиша у каждого режима, меняется в разделе **Режимы**. Если сочетание
занято другим приложением, LocalWhisper перехватывает его низкоуровневым хуком —
так можно отобрать даже `Ctrl+Space` у переключателя раскладки.

**Состояния пилюли:** серые точки — покой · красная кнопка с таймером и бегущей
волной — запись · серые пульсирующие — обработка · зелёная галочка — вставлено.
Пилюлю можно перетащить мышью, место запоминается.

---

## Вместе с Popup

Когда панель [Popup](../popup) запущена, пилюля отдаёт ей своё состояние и
становится невидимой: запись видна узкой полоской у верхнего края экрана.
Закрыли панель — пилюля возвращается вниз в течение трёх секунд. Настраивать
нечего, перезапускать ничего не нужно.

---

## API-ключ (только если используешь OpenAI)

Ключ **не хранится в открытом виде**. Варианты:

- **Настройки → Ключ OpenAI** — сохранится зашифрованным через Windows DPAPI
  (`openai_api_key_enc` в конфиге, расшифровывается только под твоей учётной
  записью Windows);
- либо переменная окружения `OPENAI_API_KEY` — имеет приоритет и на диск не
  пишется вовсе.

Полностью бесплатно и приватно: поставь
[Ollama](https://ollama.com/download/windows), `ollama pull qwen2.5:7b`, и
выбери провайдера чистки **Ollama**.

---

## Конфиг (`~/.localwhisper/config.json`)

Формат тот же, что и у прежней версии, — файл переезжает без единой правки.

```json
{
  "whisper_provider": "local",
  "whisper_model": "large-v3-turbo",
  "whisper_device": "cuda",
  "whisper_compute_type": "float16",
  "llm_provider": "openai",
  "openai_model": "gpt-5.4-mini",
  "silence_threshold": 0.003,
  "silence_duration": 2.0,
  "paste_method": "clipboard",
  "active_mode_id": "default",
  "modes": [
    { "id": "default", "name": "Voice to Text", "hotkey": "ctrl+alt+space",
      "super_mode": false, "language": "ru", "system_prompt": "" },
    { "id": "super_mode", "name": "Super Mode", "hotkey": "ctrl+shift+space",
      "super_mode": true, "language": "ru",
      "system_prompt": "Fix punctuation, capitalization, grammar. Keep language. Return ONLY cleaned text." }
  ]
}
```

Добавились три необязательных ключа: `pill_x` / `pill_y` — где оставили пилюлю,
`ui_language` (`ru`, `en` или пусто — как в системе) и `python_exe` — чем
запускать воркер, если `python` в PATH не тот.

**Модели Whisper (от быстрой к точной):** `tiny`, `base`, `small`, `medium`,
`large-v3-turbo` (рекомендуется), `large-v3`, `distil-large-v3`.

**`paste_method`:** `clipboard` (Ctrl+V, работает везде) · `keyboard` (печать по
символам, медленнее, зато проходит туда, где вставка запрещена).

История диктовок — `~/.localwhisper/history.json`, последние сто записей.
Журнал — `~/.localwhisper/app.log`.

---

## Требования к железу

| Компонент | Минимум | Рекомендуется |
|---|---|---|
| GPU VRAM | — (можно CPU) | 4+ ГБ NVIDIA |
| RAM | 8 ГБ | 16 ГБ |

`large-v3-turbo` занимает ~2 ГБ VRAM в float16.

---

## Канал для других приложений

LocalWhisper слушает `\.\pipe\localwhisper` (доступ только текущему пользователю Windows). Через него другие
приложения, например Clicky, используют уже загруженную модель вместо собственной копии. Запись, начатая клиентом,
ничего не вставляет в окна и не попадает в историю: текст возвращается только тому, кто просил.

```json
{"type":"hello"}                          -> {"type":"state","ready":true,"status":"..."}
{"type":"start","language":"ru"}          -> {"type":"started"} ... {"type":"processing"} ... {"type":"result","text":"...","raw":"...","duration":2.4}
{"type":"stop"}   {"type":"cancel"}       -> {"type":"empty"} или {"type":"error","message":"busy"}
```
