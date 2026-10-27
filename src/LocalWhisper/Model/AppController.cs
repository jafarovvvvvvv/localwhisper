using System.Windows.Threading;
using LocalWhisper.Interop;
using LocalWhisper.Services;
using LocalWhisper.UI;

namespace LocalWhisper.Model;

/// <summary>
/// Всё приложение целиком: кто кого зовёт и в каком порядке.
///
/// Здесь живёт то, что было классом <c>LocalWhisperApplication</c> в
/// питоновской версии, — без единого куска движка. Ход диктовки один и тот же
/// сверху донизу:
///
///     клавиша или кнопка на пилюле
///        → запоминаем окно, куда поедет текст
///        → пилюля в «запись», воркер пишет звук
///        → «стоп» (клавишей, кнопкой, панелью Popup или тишиной)
///        → пилюля в «обработку», воркер расшифровывает
///        → текст уезжает в буфер и вставляется, запись ложится в историю
///        → пилюля мигает зелёным и возвращается в покой
///
/// Окно, куда поедет текст, запоминается **до** начала записи — потом
/// переднее окно уже может быть другим.
/// </summary>
public sealed class AppController : IDisposable
{
    public Config Config { get; private set; } = new();
    public HistoryStore History { get; private set; } = new();

    /// <summary>Микрофоны, которые нашёл движок. Пусто, пока он не ответил.</summary>
    public IReadOnlyList<string> Microphones { get; private set; } = [];

    /// <summary>Что показывать про модель: «Готово», «Загружаю…», текст ошибки.</summary>
    public string ModelStatus { get; private set; } = "";

    /// <summary>Настройки или история изменились — окну пора перерисоваться.</summary>
    public event Action? Changed;

    private readonly WorkerClient _worker = new();
    private readonly PopupBridge _bridge = new();
    private RemoteDictationServer? _remote;
    private bool _remoteSession;
    private readonly DispatcherTimer _bridgeTicker;

    private PillWindow _pill = null!;
    private SettingsWindow _settings = null!;
    private TrayIcon _tray = null!;
    private HotkeyManager? _hotkeys;

    private Mode? _activeRecordingMode;
    private string _targetApplication = "Desktop";

    public AppController()
    {
        _bridgeTicker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _bridgeTicker.Tick += (_, _) => PushToPanel();
    }

    // MARK: - Запуск

    public void Start(bool showSettings = true)
    {
        AppPaths.EnsureCreated();
        Config = Config.Load();
        Strings.Configure(Config.UiLanguage);
        History = HistoryStore.Load();

        _pill = new PillWindow();
        _pill.StartRequested += StartFromButton;
        _pill.StopRequested += StopRecording;
        _pill.PillStateChanged += _ => PushToPanel();
        _pill.Moved += OnPillMoved;
        _pill.PlaceAt(Config.PillX, Config.PillY);
        // Показываем сразу: клавиши регистрируются на её HWND, а до показа
        // никакого HWND нет.
        _pill.Show();

        _hotkeys = new HotkeyManager(Win32.HandleOf(_pill));
        RegisterHotkeys();

        _bridge.StartRequested += StartFromButton;
        _bridge.StopRequested += StopRecording;
        _bridge.ConnectionChanged += connected =>
        {
            _pill.SetRemote(connected);
            if (connected) PushToPanel();
        };

        WireWorker();
        WireRemote();
        _worker.Start(Config);
        _worker.SendConfig(Config);

        _settings = new SettingsWindow(this);
        _tray = new TrayIcon(this);

        // Запуск из Clicky (--background) идёт тихо: окно настроек не выскакивает, приложение остаётся в трее.
        if (showSettings) _settings.Show();
    }

    private void WireWorker()
    {
        _worker.Ready += () => _worker.RequestMicrophones();

        _worker.Model += (state, message) =>
        {
            ModelStatus = state switch
            {
                ModelState.Loading => message.Length > 0
                    ? $"{Strings.L("Loading the model")}: {message}"
                    : Strings.L("Loading the model"),
                ModelState.Loaded => $"{Strings.L("Ready")} · {message}",
                _ => message,
            };
            if (state == ModelState.Failed && message.Length > 0) Notify(message);
            _remote?.Broadcast(StateEvent());
            Changed?.Invoke();
        };

        _worker.DeviceFellBack += (device, compute) =>
        {
            // Движок сам слез с видеокарты на процессор. Настройки должны это
            // отражать, иначе окно показывает cuda там, где давно cpu.
            Config.WhisperDevice = device;
            Config.WhisperComputeType = compute;
            Config.Save();
            Notify(Strings.L("Fell back to CPU, it will be slower"));
            Changed?.Invoke();
        };

        _worker.RecordingStarted += () =>
        {
            Log.Info("app", "запись пошла");
            if (_remoteSession) _remote?.Broadcast(RemoteProtocol.Event("started"));
        };
        _worker.SilenceDetected += () =>
        {
            Log.Info("app", "тишина, заканчиваю сама");
            if (_remoteSession) _remote?.Broadcast(RemoteProtocol.Event("silence"));
        };

        _worker.Processing += () =>
        {
            if (_remoteSession) { _remote?.Broadcast(RemoteProtocol.Event("processing")); return; }
            if (_pill.State != PillState.Recording) return;
            Sound.Play(Chime.Stop, Config.SoundEnabled);
            _pill.SetState(PillState.Processing);
        };

        _worker.Result += OnResult;
        _worker.Empty += () =>
        {
            if (_remoteSession) { _remoteSession = false; _remote?.Broadcast(RemoteProtocol.Event("empty")); return; }
            _pill.SetState(PillState.Idle);
        };
        _worker.Warning += Notify;

        _worker.Failed += message =>
        {
            if (_remoteSession)
            {
                _remoteSession = false;
                _remote?.Broadcast(RemoteProtocol.Event("error", new { message }));
                return;
            }
            _pill.SetState(PillState.Idle);
            Notify($"{Strings.L("Transcription failed")}: {message}");
        };

        _worker.Microphones += list =>
        {
            Microphones = list;
            Changed?.Invoke();
        };
    }

    // MARK: - Клавиши

    private void RegisterHotkeys()
    {
        if (_hotkeys is null) return;

        var bindings = new List<(string, Action)>();
        foreach (var mode in Config.Modes)
        {
            if (string.IsNullOrWhiteSpace(mode.Hotkey)) continue;
            var captured = mode;
            bindings.Add((mode.Hotkey, () => OnHotkey(captured)));
        }
        bindings.Add((CycleHotkey, CycleActiveMode));

        var failed = _hotkeys.Apply(bindings);
        if (failed.Count > 0)
        {
            Notify($"{Strings.L("Could not register hotkeys")}: {string.Join(", ", failed)}. " +
                   Strings.L("Pick a different combination in Modes"));
        }
    }

    /// <summary>Клавиша, переключающая активный режим по кругу.</summary>
    public const string CycleHotkey = "ctrl+shift+m";

    /// <summary>
    /// Одно и то же сочетание и начинает, и останавливает запись. Каждая
    /// клавиша при этом всегда запускает свой режим — активный режим важен
    /// только для кнопки на пилюле.
    /// </summary>
    private void OnHotkey(Mode mode)
    {
        if (_remoteSession) return;
        switch (_pill.State)
        {
            case PillState.Idle:
                StartRecording(mode);
                break;
            case PillState.Recording:
                StopRecording();
                break;
        }
    }

    private void StartFromButton()
    {
        if (_remoteSession || _pill.State != PillState.Idle) return;
        if (Config.ActiveMode is { } mode) StartRecording(mode);
    }

    /// <summary>Кнопка «начать запись» на главной — тот же путь, что у кнопки на пилюле.</summary>
    public void RequestRecording() => StartFromButton();

    // MARK: - Ход диктовки

    private void StartRecording(Mode mode)
    {
        if (!_worker.ModelLoaded)
        {
            Notify(Strings.L("Model is still loading, please wait"));
            return;
        }

        // Пока пилюля не показалась и фокус не сместился.
        _targetApplication = Win32.ForegroundWindowTitle();
        _activeRecordingMode = mode;

        Sound.Play(Chime.Start, Config.SoundEnabled);
        _pill.SetState(PillState.Recording);
        _bridgeTicker.Start();
        _worker.StartRecording(mode);

        Log.Info("app", $"запись в режиме «{mode.Name}», текст поедет в «{_targetApplication}»");
    }

    private void StopRecording()
    {
        if (_pill.State != PillState.Recording) return;
        _worker.StopRecording();
    }

    private async void OnResult(string raw, string clean, double seconds)
    {
        _bridgeTicker.Stop();

        // Запись начал внешний клиент (Clicky): текст возвращается только ему, без вставки и истории.
        if (_remoteSession)
        {
            _remoteSession = false;
            Log.Info("app", $"распознано для внешнего клиента: {clean.Length} симв.");
            _remote?.Broadcast(RemoteProtocol.Event("result", new { text = clean, raw, duration = seconds }));
            return;
        }

        if (string.IsNullOrWhiteSpace(clean))
        {
            _pill.SetState(PillState.Idle);
            return;
        }

        await TextOutput.PasteAsync(clean, Config.PasteMethod).ConfigureAwait(true);

        var entry = HistoryEntry.Create(
            _activeRecordingMode?.Name ?? "", raw, clean, seconds, _targetApplication, DateTime.Now);
        History.Add(entry);

        Sound.Play(Chime.Success, Config.SoundEnabled);
        _pill.SetState(PillState.Success);
        Changed?.Invoke();

        Log.Info("app", $"вставлено {entry.Words} сл., {entry.Wpm} сл/мин, в «{entry.Application}»");
    }

    // MARK: - Внешние клиенты (канал \.\pipe\localwhisper)

    private void WireRemote()
    {
        _remote = new RemoteDictationServer { HelloProvider = StateEvent };
        _remote.CommandReceived += OnRemoteCommand;
        _remote.AllClientsGone += () =>
        {
            if (!_remoteSession) return;
            _remoteSession = false;
            _worker.CancelRecording();
        };
    }

    private string StateEvent() =>
        RemoteProtocol.Event("state", new { ready = _worker.ModelLoaded, status = ModelStatus });

    private void OnRemoteCommand(RemoteCommand command)
    {
        switch (command.Type)
        {
            case "start":
                if (_remoteSession || _pill.State != PillState.Idle)
                {
                    _remote?.Broadcast(RemoteProtocol.Event("error", new { message = "busy" }));
                    return;
                }
                if (!_worker.ModelLoaded)
                {
                    _remote?.Broadcast(RemoteProtocol.Event("error", new { message = "Model is still loading" }));
                    return;
                }
                _remoteSession = true;
                _worker.StartRecording(new Mode
                {
                    Id = "remote",
                    Name = "Remote",
                    Language = string.IsNullOrWhiteSpace(command.Language) ? "auto" : command.Language,
                });
                Log.Info("app", $"внешний клиент начал запись, язык {command.Language}");
                break;
            case "stop":
                if (_remoteSession) _worker.StopRecording();
                break;
            case "cancel":
                if (_remoteSession) _worker.CancelRecording();
                break;
        }
    }

    // MARK: - Панель Popup

    /// <summary>
    /// Отдать панели то же, что показывает пилюля. Полоска наверху повторяет её
    /// визуальный язык нарочно: глаз уже научился читать эту картинку.
    /// </summary>
    private void PushToPanel()
    {
        var tone = _pill.State switch
        {
            PillState.Recording => ActivityTone.Live,
            PillState.Processing => ActivityTone.Busy,
            PillState.Success => ActivityTone.Ok,
            _ => ActivityTone.Idle,
        };

        if (tone != ActivityTone.Live) _bridgeTicker.Stop();
        else if (!_bridgeTicker.IsEnabled) _bridgeTicker.Start();

        _bridge.Push(tone, tone == ActivityTone.Ok ? "" : _pill.TimeString);
    }

    // MARK: - Настройки

    /// <summary>
    /// Сохранить настройки и разложить последствия: перевесить клавиши, если
    /// сочетания менялись, и перезагрузить модель, если менялся движок. Решает
    /// об этом воркер — он один знает, что у него уже загружено.
    /// </summary>
    public void SaveConfig()
    {
        Config.Save();
        Strings.Configure(Config.UiLanguage);
        RegisterHotkeys();
        _worker.SendConfig(Config);
        Changed?.Invoke();
    }

    public void SetActiveMode(string modeId)
    {
        Config.ActiveModeId = modeId;
        Config.Save();

        if (Config.Modes.FirstOrDefault(m => m.Id == modeId) is { } mode)
            Notify($"{Strings.L("Active mode")}: {mode.Name}");

        Sound.Play(Chime.ModeSwitch, Config.SoundEnabled);
        Changed?.Invoke();
    }

    private void CycleActiveMode()
    {
        if (Config.Modes.Count == 0) return;
        var index = Config.Modes.FindIndex(m => m.Id == Config.ActiveModeId);
        var next = Config.Modes[(index + 1 + Config.Modes.Count) % Config.Modes.Count];
        SetActiveMode(next.Id);
    }

    private void OnPillMoved(double left, double top)
    {
        Config.PillX = left;
        Config.PillY = top;
        Config.Save();
    }

    public void RefreshMicrophones() => _worker.RequestMicrophones();

    /// <summary>Проверить сигнал, не начиная записи, — раздел «Звук».</summary>
    public void PlayChime(Chime chime) => Sound.Play(chime, true);

    // MARK: - Окно и трей

    public void ShowSettings()
    {
        _settings.Show();
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    public void ToggleSettings()
    {
        if (_settings.IsVisible && _settings.WindowState != WindowState.Minimized) _settings.Hide();
        else ShowSettings();
    }

    public void Notify(string message)
    {
        // Уведомления Windows режут длинный текст сами и не всегда изящно —
        // лучше обрезать здесь, чем показать половину слова.
        if (message.Length > 200) message = message[..197] + "…";
        Log.Info("notify", message);
        _tray?.Notify(message);
    }

    public void Quit() => Application.Current.Shutdown();

    public void Dispose()
    {
        _bridgeTicker.Stop();
        // Окно спрятано, а не закрыто, — само оно не уйдёт.
        _settings?.CloseForGood();
        _hotkeys?.Dispose();
        _remote?.Dispose();
        _bridge.Dispose();
        _worker.Dispose();
        _tray?.Dispose();
    }
}
