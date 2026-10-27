using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace LocalWhisper.Services;

/// <summary>Команда от внешнего клиента (например, Clicky).</summary>
public sealed record RemoteCommand(string Type, string Language = "");

/// <summary>Строки протокола канала. Вынесено отдельно, чтобы проверять тестами, а не микрофоном.</summary>
public static class RemoteProtocol
{
    public const string PipeName = "localwhisper";

    private static readonly JsonSerializerOptions Wire = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Разбор команды. <c>null</c> - не наш формат или неизвестный тип.
    /// Команды: hello, start (с language), stop, cancel.
    /// </summary>
    public static RemoteCommand? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            var root = JsonDocument.Parse(line).RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (type is not ("hello" or "start" or "stop" or "cancel")) return null;
            var language = root.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : "";
            return new RemoteCommand(type, language);
        }
        catch (JsonException) { return null; }
    }

    public static string Event(string type, object? extra = null)
    {
        var payload = new Dictionary<string, object?> { ["type"] = type };
        if (extra != null)
            foreach (var p in extra.GetType().GetProperties()) payload[p.Name] = p.GetValue(extra);
        return JsonSerializer.Serialize(payload, Wire);
    }
}

/// <summary>
/// Канал <c>\\.\pipe\localwhisper</c>: другие приложения (Clicky) используют уже загруженный движок
/// распознавания LocalWhisper вместо собственной копии модели. Запись, начатая клиентом, ничего не
/// вставляет в окна и не попадает в историю: текст возвращается только тому, кто просил.
/// Доступ только текущему пользователю Windows.
/// </summary>
public sealed class RemoteDictationServer : IDisposable
{
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<StreamWriter> _clients = new();
    private readonly object _gate = new();

    public event Action<RemoteCommand>? CommandReceived;

    /// <summary>Отключился последний клиент: незаконченную запись для него продолжать незачем.</summary>
    public event Action? AllClientsGone;

    /// <summary>Что рассказать новому клиенту при подключении (готовность модели).</summary>
    public Func<string>? HelloProvider { get; set; }

    public RemoteDictationServer() => _ = AcceptLoop();

    private NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(RemoteProtocol.PipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    private async Task AcceptLoop()
    {
        while (!_stopping.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(_stopping.Token).ConfigureAwait(false);
                var client = pipe;
                _ = Task.Run(() => Serve(client));
            }
            catch (OperationCanceledException) { pipe?.Dispose(); return; }
            catch (Exception ex)
            {
                pipe?.Dispose();
                Log.Warn("remote", $"ожидание клиента: {ex.Message}");
                try { await Task.Delay(1000, _stopping.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task Serve(NamedPipeServerStream pipe)
    {
        var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        lock (_gate) _clients.Add(writer);
        Log.Info("remote", "клиент подключился к каналу");
        try
        {
            using var reader = new StreamReader(pipe, new UTF8Encoding(false));
            while (!_stopping.IsCancellationRequested && pipe.IsConnected)
            {
                var line = await reader.ReadLineAsync(_stopping.Token).ConfigureAwait(false);
                if (line == null) break;
                if (RemoteProtocol.Parse(line) is not { } command) continue;

                if (command.Type == "hello")
                {
                    var hello = _ui.Invoke(() => HelloProvider?.Invoke());
                    if (hello != null) Write(writer, hello);
                    continue;
                }
                _ = _ui.BeginInvoke(() => CommandReceived?.Invoke(command));
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (Exception ex) { Log.Warn("remote", $"клиент: {ex.Message}"); }
        finally
        {
            bool none;
            lock (_gate) { _clients.Remove(writer); none = _clients.Count == 0; }
            try { pipe.Dispose(); } catch { }
            if (none) _ = _ui.BeginInvoke(() => AllClientsGone?.Invoke());
            Log.Info("remote", "клиент отключился");
        }
    }

    private static void Write(StreamWriter w, string line)
    {
        try { lock (w) w.WriteLine(line); } catch { }
    }

    /// <summary>Разослать событие всем подключённым клиентам.</summary>
    public void Broadcast(string line)
    {
        StreamWriter[] snapshot;
        lock (_gate) snapshot = _clients.ToArray();
        foreach (var w in snapshot) Write(w, line);
    }

    public bool HasClients { get { lock (_gate) return _clients.Count > 0; } }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }
}
