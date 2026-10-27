using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalWhisper.Services;

/// <summary>
/// Одна расшифровка в истории. Формат записи — тот же, что писала питоновская
/// версия, слово в слово: этот файл читает ещё и плагин Popup
/// (<c>plugins/localwhisper/main.py</c>), и он ждёт именно эти имена.
/// </summary>
public sealed class HistoryEntry
{
    /// <summary>Время в формате <c>yyyy-MM-dd HH:mm:ss</c> — как у <c>time.strftime</c>.</summary>
    public string Time { get; set; } = "";

    public string Mode { get; set; } = "";
    public string Raw { get; set; } = "";
    public string Clean { get; set; } = "";
    public int Words { get; set; }
    public int Duration { get; set; }
    public int Wpm { get; set; }
    public string Application { get; set; } = "";

    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// Разобранное время, или <c>null</c> у строки, которую не прочитать.
    ///
    /// В файл не пишется. Без этой пометки System.Text.Json послушно добавил бы
    /// в каждую запись девятое поле <c>at</c> — а формат истории читает ещё и
    /// плагин Popup, и обещание «тот же файл» перестало бы быть правдой.
    /// </summary>
    [JsonIgnore]
    public DateTime? At =>
        DateTime.TryParseExact(Time, TimeFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var value)
            ? value
            : null;

    /// <summary>
    /// Собирает запись из готовой расшифровки. Слова и темп считаются здесь, а
    /// не в трёх местах: на них держится вся статистика на главной.
    /// </summary>
    public static HistoryEntry Create(
        string modeName, string raw, string clean, double seconds, string application, DateTime now)
    {
        var words = CountWords(clean);
        return new HistoryEntry
        {
            Time = now.ToString(TimeFormat, CultureInfo.InvariantCulture),
            Mode = modeName,
            Raw = raw,
            Clean = clean,
            Words = words,
            Duration = (int)seconds,
            Wpm = Speed(words, seconds),
            Application = application,
        };
    }

    public static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Слов в минуту. Секунда снизу отсекается так же, как раньше: короткая
    /// реплика на полсекунды иначе даёт тысячи слов в минуту и портит среднее.
    /// </summary>
    public static int Speed(int words, double seconds) =>
        (int)(words / (Math.Max(seconds, 1.0) / 60.0));
}

/// <summary>
/// История расшифровок в <c>~/.localwhisper/history.json</c>.
///
/// На диске лежат последние сто записей — столько же, сколько хранила
/// питоновская версия. Это не экономия места, а осознанный предел: история
/// диктовок — то, что читают за сегодня, а не за год, и файл, который растёт
/// без конца, рано или поздно придётся чистить руками.
/// </summary>
public sealed class HistoryStore
{
    public const int Keep = 100;

    private readonly List<HistoryEntry> _items = [];

    public IReadOnlyList<HistoryEntry> Items => _items;

    public static HistoryStore Load()
    {
        var store = new HistoryStore();
        if (!File.Exists(AppPaths.History)) return store;
        try
        {
            store._items.AddRange(Parse(File.ReadAllText(AppPaths.History)));
        }
        catch (Exception e)
        {
            Log.Error("history", "history.json не читается", e);
        }
        return store;
    }

    public static List<HistoryEntry> Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<HistoryEntry>>(json, Config.JsonOptions) ?? [];
        }
        catch (JsonException e)
        {
            Log.Warn("history", $"history.json не разобрать: {e.Message}");
            return [];
        }
    }

    public void Add(HistoryEntry entry)
    {
        _items.Add(entry);
        Save();
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            var tail = _items.Count > Keep ? _items.Skip(_items.Count - Keep) : _items;
            AppPaths.WriteAtomic(AppPaths.History,
                JsonSerializer.Serialize(tail.ToList(), Config.JsonOptions));
        }
        catch (Exception e)
        {
            Log.Error("history", "не сохранить историю", e);
        }
    }
}

/// <summary>Четыре числа на главной. Считаются из истории, больше ниоткуда.</summary>
public readonly record struct Stats(int AverageWpm, int WordsThisWeek, int AppsUsed, int HoursSaved)
{
    /// <summary>
    /// Диктовка примерно вдвое быстрее набора — отсюда «сэкономлено». Множитель
    /// тот же, что стоял в питоновской версии; менять его значило бы обнулить
    /// накопленную цифру без всякой причины.
    /// </summary>
    private const int TypingFactor = 2;

    public static Stats From(IEnumerable<HistoryEntry> history, DateTime now)
    {
        var items = history as IReadOnlyList<HistoryEntry> ?? history.ToList();
        if (items.Count == 0) return new Stats(0, 0, 0, 0);

        var speeds = items.Where(i => i.Wpm > 0).Select(i => i.Wpm).ToList();
        var averageWpm = speeds.Count > 0 ? (int)speeds.Average() : 0;

        var weekAgo = now.AddDays(-7);
        var words = 0;
        var savedSeconds = 0;
        var applications = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item.At is not { } at || at < weekAgo) continue;
            words += item.Words;
            savedSeconds += item.Duration * TypingFactor;
            if (!string.IsNullOrWhiteSpace(item.Application)) applications.Add(item.Application);
        }

        // Час снизу — но только если на неделе вообще что-то было. Пустая неделя,
        // подписанная «сэкономлен час», читалась бы как поломка.
        var hours = savedSeconds > 0 ? Math.Max(1, savedSeconds / 3600) : 0;
        return new Stats(averageWpm, words, applications.Count, hours);
    }
}
