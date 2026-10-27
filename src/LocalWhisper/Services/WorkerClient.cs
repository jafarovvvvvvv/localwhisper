using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace LocalWhisper.Services;

/// <summary>Что сейчас с моделью — это единственное, что окно показывает про движок.</summary>
public enum ModelState
{
    Idle,
    Loading,
    Loaded,
    Failed,
}

/// <summary>
/// Питоновский движок как дочерний процесс.
///
/// Распознавание речи осталось на <c>faster-whisper</c>: это питоновская
/// обёртка над CTranslate2, и три гигабайта моделей на диске лежат в её
/// формате. Переезд на другой движок дал бы другое качество — это была бы
/// замена, а не перенос.
///
/// Разговор — построчный JSON через stdin/stdout, тот же протокол, что у
/// плагинов Popup. Он отлаживается запуском воркера руками в консоли, и именно
/// так он и был проверен до появления первого окна.
///
/// Две вещи, на которых здесь легко обжечься:
/// * <c>PYTHONUNBUFFERED=1</c> обязателен — иначе воркер молчит до самого
///   выхода, и приложение выглядит зависшим;
/// * кодировку потоков надо назвать явно: без этого .NET расшифровывает вывод
///   локалью, и первая же русская расшифровка приезжает кракозябрами.
/// </summary>
public sealed class WorkerClient : IDisposable
{
    /// <summary>
    /// Как команды ложатся в провод. Своя настройка, а не общая с конфигом:
    /// там переименование в <c>snake_case</c> и отступы для человека, а здесь
    /// имена уже написаны как надо и строка обязана быть одной строкой.
    /// </summary>
    private static readonly JsonSerializerOptions Wire = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// UTF-8 без метки порядка байтов.
    ///
    /// Именно без неё: <c>Encoding.UTF8</c> дописывает в начало потока три
    /// байта BOM, и первая же команда приезжает воркеру как «﻿{"type"…» —
    /// строка перестаёт быть JSON-ом, настройки не доходят, модель не грузится,
    /// а приложение выглядит просто зависшим. Стоило одного запуска.
    /// </summary>
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Lock _writeGate = new();

    private Process? _process;
    private string? _workerPath;
    private Config? _config;
    private bool _stopping;
    private int _restarts;

    // MARK: - События. Все приходят в поток интерфейса.

    public event Action? Ready;
    public event Action<ModelState, string>? Model;
    public event Action<string, string>? DeviceFellBack;
    public event Action? RecordingStarted;
    public event Action? SilenceDetected;
    public event Action? Processing;
    public event Action<string, string, double>? Result;
    public event Action? Empty;
    public event Action<string>? Warning;
    public event Action<string>? Failed;
    public event Action<IReadOnlyList<string>>? Microphones;

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Готова ли модель принимать звук. До этого запись начинать нечем.</summary>
    public bool ModelLoaded { get; private set; }

    public void Start(Config config)
    {
        _config = config;
        _stopping = false;

        _workerPath = AppPaths.FindWorker();
        if (_workerPath is null)
        {
            Log.Error("worker", "engine/worker.py не найден рядом с приложением");
            Failed?.Invoke(Strings.L("Python worker not found"));
            return;
        }

        Launch();
    }

    private void Launch()
    {
        if (_workerPath is null || _config is null) return;

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(_config.PythonExe) ? "python" : _config.PythonExe,
                WorkingDirectory = Path.GetDirectoryName(_workerPath)!,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
                StandardInputEncoding = Utf8NoBom,
            };
            info.ArgumentList.Add(_workerPath);
            info.Environment["PYTHONUNBUFFERED"] = "1";
            info.Environment["PYTHONIOENCODING"] = "utf-8";

            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line) _dispatcher.BeginInvoke(() => Receive(line));
            };
            process.ErrorDataReceived += (_, e) =>
            {
                // stderr — это не протокол, а то, что воркер сказал в сторону:
                // предупреждение библиотеки, трассировка. Место ему в журнале.
                if (e.Data is { Length: > 0 } line) Log.Info("engine", line);
            };
            process.Exited += (_, _) => _dispatcher.BeginInvoke(OnExited);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            Log.Info("worker", $"движок запущен: {info.FileName} {_workerPath}");
        }
        catch (Exception e)
        {
            Log.Error("worker", "движок не запустился", e);
            Failed?.Invoke($"{Strings.L("The engine is not running")}: {e.Message}");
        }
    }

    private void OnExited()
    {
        ModelLoaded = false;
        if (_stopping) return;

        Log.Warn("worker", "движок завершился сам");
        // Один раз поднимаем молча: движок могло уронить нехваткой видеопамяти,
        // и второй запуск обычно проходит. Дальше — уже разговор с человеком, а
        // не бесконечная карусель перезапусков.
        if (_restarts++ < 3)
        {
            Model?.Invoke(ModelState.Loading, Strings.L("Loading the model"));
            _ = RestartAfterDelay();
        }
        else
        {
            Failed?.Invoke(Strings.L("The engine is not running"));
        }
    }

    private async Task RestartAfterDelay()
    {
        await Task.Delay(1500).ConfigureAwait(true);
        if (_stopping) return;
        Launch();
        if (_config is not null) SendConfig(_config);
    }

    // MARK: - Команды

    public void SendConfig(Config config)
    {
        _config = config;
        ModelLoaded = false;
        SendRaw($"{{\"type\":\"config\",\"config\":{config.ToWorkerJson()}}}");
    }

    public void StartRecording(Mode mode) =>
        Send(new
        {
            type = "start",
            mode = new
            {
                id = mode.Id,
                name = mode.Name,
                language = mode.Language,
                super_mode = mode.SuperMode,
                system_prompt = mode.SystemPrompt,
            },
        });

    public void StopRecording() => Send(new { type = "stop" });
    public void CancelRecording() => Send(new { type = "cancel" });
    public void RequestMicrophones() => Send(new { type = "mics" });

    private void Send(object message) => SendRaw(JsonSerializer.Serialize(message, Wire));

    private void SendRaw(string line)
    {
        if (_process is not { HasExited: false } process) return;
        try
        {
            lock (_writeGate)
            {
                // Одно сообщение — одна строка, всегда без переносов внутри.
                process.StandardInput.Write(line.ReplaceLineEndings(" "));
                process.StandardInput.Write('\n');
                process.StandardInput.Flush();
            }
        }
        catch (Exception e)
        {
            Log.Warn("worker", $"не отправить команду: {e.Message}");
        }
    }

    // MARK: - Ответы

    private void Receive(string line)
    {
        if (WorkerMessage.Parse(line) is not { } message)
        {
            // Воркер напечатал не JSON. Во время разработки это нормально —
            // отладочный print попадает в тот же поток.
            if (line.Trim().Length > 0) Log.Info("engine", line);
            return;
        }

        switch (message.Type)
        {
            case "ready":
                _restarts = 0;
                Ready?.Invoke();
                break;

            case "model":
                OnModel(message.State, message.Message);
                break;

            case "device":
                DeviceFellBack?.Invoke(message.Device, message.ComputeType);
                break;

            case "started":
                RecordingStarted?.Invoke();
                break;

            case "silence":
                SilenceDetected?.Invoke();
                break;

            case "processing":
                Processing?.Invoke();
                break;

            case "result":
                Result?.Invoke(message.Raw, message.Clean, message.Duration);
                break;

            case "empty":
                Empty?.Invoke();
                break;

            case "warning":
                Warning?.Invoke(message.Message);
                break;

            case "error":
                Failed?.Invoke(message.Message);
                break;

            case "mics":
                Microphones?.Invoke(message.Names);
                break;

            case "log":
                Log.Info("engine", message.Message);
                break;

            case "pong":
                break;

            default:
                Log.Info("engine", $"неизвестный ответ: {line}");
                break;
        }
    }

    /// <summary>
    /// Состояние модели в терминах приложения. «Скачиваю» и «загружаю» для окна
    /// одно и то же — ждать; разница только в подписи.
    /// </summary>
    private void OnModel(string state, string message)
    {
        switch (state)
        {
            case "loading" or "progress":
                ModelLoaded = false;
                Model?.Invoke(ModelState.Loading, message);
                break;
            case "loaded":
                ModelLoaded = true;
                Model?.Invoke(ModelState.Loaded, message);
                break;
            default:
                ModelLoaded = false;
                Model?.Invoke(ModelState.Failed, message);
                break;
        }
    }

    // MARK: - Выход

    public void Dispose()
    {
        _stopping = true;
        if (_process is not { } process) return;
        try
        {
            if (!process.HasExited)
            {
                Send(new { type = "shutdown" });
                // Даём попрощаться, но недолго: приложение закрывается, а не ждёт.
                if (!process.WaitForExit(800)) process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e)
        {
            Log.Warn("worker", $"движок не остановился: {e.Message}");
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }
}
