using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace LocalWhisper.Services;

/// <summary>Как выглядит состояние в панели Popup. Названия — из docs/pipe.md.</summary>
public enum ActivityTone
{
    Idle,
    Live,
    Busy,
    Ok,
}

/// <summary>
/// Мост между LocalWhisper и панелью Popup.
///
/// Когда Popup запущен, пилюля не показывает себя сама — её состояние уезжает
/// наверх экрана, в панель, и рисуется там. Когда Popup не запущен (или
/// закрылся посреди работы), пилюля мгновенно возвращается на своё место в
/// правом нижнем углу. Настраивать нечего: наличие панели определяется тем,
/// отвечает ли она.
///
/// Канал открывается перекрывающимся (<see cref="PipeOptions.Asynchronous"/>) —
/// и это здесь главное. У неперекрывающегося дескриптора блокирующее чтение
/// замораживает запись из другого потока: пилюля перестала бы отдавать таймер,
/// пока панель молчит. Питоновский мост обходил это опросом
/// <c>PeekNamedPipe</c>; в .NET достаточно попросить асинхронный дескриптор и
/// читать <c>ReadAsync</c>.
/// </summary>
public sealed class PopupBridge : IDisposable
{
    private const string PipeName = "popup";

    /// <summary>
    /// Как часто перепроверять, не появился ли Popup. Три секунды — достаточно
    /// редко, чтобы не шуметь, и достаточно часто, чтобы человек не успел
    /// заметить, что панель уже запущена, а пилюля всё ещё внизу.
    /// </summary>
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private NamedPipeClientStream? _pipe;
    private string? _lastSent;

    /// <summary>Панель попросила остановить запись — нажали кнопку в её шапке.</summary>
    public event Action? StopRequested;

    /// <summary>Панель попросила начать.</summary>
    public event Action? StartRequested;

    /// <summary>Появилась или пропала. По этому пилюля решает, показывать ли себя.</summary>
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => _pipe is { IsConnected: true };

    public PopupBridge() => _ = Loop();

    private async Task Loop()
    {
        var token = _stopping.Token;
        while (!token.IsCancellationRequested)
        {
            if (!await TryConnect(token).ConfigureAwait(false))
            {
                try
                {
                    await Task.Delay(ReconnectDelay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }

            await Read(token).ConfigureAwait(false);
            Drop();
        }
    }

    private async Task<bool> TryConnect(CancellationToken token)
    {
        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            // Секунда: панели либо нет, либо она отвечает сразу. Долгое ожидание
            // здесь означало бы, что мост залипает на каждой попытке.
            await pipe.ConnectAsync(1000, token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Панель не запущена — это нормальное состояние, а не ошибка.
            await pipe.DisposeAsync().ConfigureAwait(false);
            return false;
        }

        _pipe = pipe;
        _lastSent = null;
        Log.Info("popup", "панель нашлась, пилюля уезжает наверх");
        _ = _dispatcher.BeginInvoke(() => ConnectionChanged?.Invoke(true));
        return true;
    }

    private async Task Read(CancellationToken token)
    {
        if (_pipe is not { } pipe) return;

        var buffer = new byte[4096];
        var pending = new StringBuilder();

        while (!token.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await pipe.ReadAsync(buffer, token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }
            if (read <= 0) return;

            pending.Append(Encoding.UTF8.GetString(buffer, 0, read));
            var text = pending.ToString();
            int newline;
            while ((newline = text.IndexOf('\n')) >= 0)
            {
                Handle(text[..newline]);
                text = text[(newline + 1)..];
            }
            pending.Clear();
            pending.Append(text);
        }
    }

    private void Handle(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return;

        try
        {
            var root = JsonDocument.Parse(line).RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "action") return;
            var id = root.TryGetProperty("id", out var value) ? value.GetString() : null;

            _ = _dispatcher.BeginInvoke(() =>
            {
                if (id == "stop") StopRequested?.Invoke();
                else if (id == "start") StartRequested?.Invoke();
            });
        }
        catch (JsonException)
        {
            // Панель сказала что-то, чего мы не понимаем. Не наше дело.
        }
    }

    private void Drop()
    {
        var pipe = _pipe;
        _pipe = null;
        if (pipe is null) return;
        try
        {
            pipe.Dispose();
        }
        catch (Exception)
        {
            // Уже оборвано.
        }
        Log.Info("popup", "панель пропала, пилюля возвращается");
        _ = _dispatcher.BeginInvoke(() => ConnectionChanged?.Invoke(false));
    }

    // MARK: - Отправка состояния

    /// <summary>
    /// Отдать панели текущее состояние. Одно и то же второй раз не уезжает: в
    /// покое мост не должен делать вообще ничего.
    /// </summary>
    public void Push(ActivityTone tone, string label)
    {
        var message = Compose(tone, label);
        if (message == _lastSent) return;
        _lastSent = message;
        _ = SendAsync(message);
    }

    /// <summary>
    /// Собирает строку протокола. Открыто ради тестов: формат описан в
    /// <c>docs/pipe.md</c> Popup, и разойтись с ним молча — значит потерять
    /// полоску наверху, не увидев ни одной ошибки.
    /// </summary>
    public static string Compose(ActivityTone tone, string label)
    {
        var payload = new Dictionary<string, object>
        {
            ["type"] = "activity",
            ["source"] = "localwhisper",
            ["name"] = "LocalWhisper",
            ["tone"] = tone switch
            {
                ActivityTone.Live => "live",
                ActivityTone.Busy => "busy",
                ActivityTone.Ok => "ok",
                _ => "idle",
            },
        };

        if (tone != ActivityTone.Idle) payload["label"] = label;
        if (tone == ActivityTone.Live)
        {
            payload["actions"] = new[]
            {
                new Dictionary<string, string> { ["id"] = "stop", ["label"] = Strings.L("Stop") },
            };
        }

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private async Task SendAsync(string message)
    {
        if (_pipe is not { IsConnected: true } pipe) return;

        var bytes = Encoding.UTF8.GetBytes(message + "\n");
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await pipe.WriteAsync(bytes).ConfigureAwait(false);
            await pipe.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Панель закрылась посреди записи. Цикл чтения это заметит и вернёт
            // пилюлю на место — здесь достаточно промолчать.
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose()
    {
        // Убрать за собой: панель не должна остаться с полоской «идёт запись»
        // от процесса, который уже закрылся.
        if (IsConnected)
        {
            try
            {
                SendAsync(Compose(ActivityTone.Idle, "")).Wait(300);
            }
            catch (Exception)
            {
                // Не дождались — панель уберёт полоску сама, заметив обрыв.
            }
        }

        _stopping.Cancel();
        _pipe?.Dispose();
        _pipe = null;
        _stopping.Dispose();
        _writeGate.Dispose();
    }
}
