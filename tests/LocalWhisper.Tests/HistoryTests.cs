using System.Globalization;
using LocalWhisper.Services;
using LocalWhisper.UI.Panes;

namespace LocalWhisper.Tests;

/// <summary>
/// Формат истории и статистика, которая из неё считается.
///
/// Файл <c>~/.localwhisper/history.json</c> читает не только это приложение:
/// на него смотрит плагин Popup. Поэтому здесь проверяются не «поля какие-то»,
/// а именно те восемь имён, которые писала питоновская версия.
/// </summary>
public class HistoryTests
{
    private const string LegacyJson = """
    [
      {
        "time": "2026-08-07 14:03:11",
        "mode": "Voice to Text",
        "raw": "привет мир",
        "clean": "Привет, мир!",
        "words": 2,
        "duration": 3,
        "wpm": 40,
        "application": "Блокнот"
      },
      {
        "time": "2026-08-07 14:10:52",
        "mode": "Super Mode",
        "raw": "это тест",
        "clean": "Это тест.",
        "words": 2,
        "duration": 2,
        "wpm": 60,
        "application": "Visual Studio Code"
      }
    ]
    """;

    [Fact]
    public void Reads_history_written_by_the_python_version()
    {
        var items = HistoryStore.Parse(LegacyJson);

        Assert.Equal(2, items.Count);
        Assert.Equal("2026-08-07 14:03:11", items[0].Time);
        Assert.Equal("Voice to Text", items[0].Mode);
        Assert.Equal("привет мир", items[0].Raw);
        Assert.Equal("Привет, мир!", items[0].Clean);
        Assert.Equal(2, items[0].Words);
        Assert.Equal(3, items[0].Duration);
        Assert.Equal(40, items[0].Wpm);
        Assert.Equal("Блокнот", items[0].Application);
    }

    [Fact]
    public void Understands_the_time_stamp()
    {
        var entry = HistoryStore.Parse(LegacyJson)[0];
        Assert.Equal(new DateTime(2026, 8, 7, 14, 3, 11), entry.At);
    }

    [Fact]
    public void A_time_stamp_it_cannot_read_is_not_a_crash()
    {
        var entry = HistoryStore.Parse("""[{"time":"вчера","clean":"текст"}]""")[0];
        Assert.Null(entry.At);
        Assert.Equal("текст", entry.Clean);
    }

    [Fact]
    public void A_broken_file_reads_as_no_history_at_all()
    {
        Assert.Empty(HistoryStore.Parse("{ not a list"));
    }

    [Fact]
    public void Writes_a_record_in_exactly_the_old_shape()
    {
        var entry = HistoryEntry.Create(
            "Super Mode", "привет мир", "Привет, мир!", 6.0, "Notepad",
            new DateTime(2026, 8, 8, 9, 30, 0));

        Assert.Equal("2026-08-08 09:30:00", entry.Time);
        Assert.Equal("Super Mode", entry.Mode);
        Assert.Equal(2, entry.Words);
        Assert.Equal(6, entry.Duration);
        Assert.Equal(20, entry.Wpm);
        Assert.Equal("Notepad", entry.Application);
    }

    [Fact]
    public void Counts_words_the_way_a_person_would()
    {
        Assert.Equal(0, HistoryEntry.CountWords(""));
        Assert.Equal(0, HistoryEntry.CountWords("   "));
        Assert.Equal(1, HistoryEntry.CountWords("слово"));
        Assert.Equal(3, HistoryEntry.CountWords("  три   слова  тут "));
        Assert.Equal(4, HistoryEntry.CountWords("строка\nс\tразными\nпробелами"));
    }

    [Fact]
    public void A_half_second_remark_does_not_claim_a_thousand_words_a_minute()
    {
        // Секунда снизу — иначе одно слово за полсекунды даёт 120 сл/мин и
        // портит среднее по всей истории.
        Assert.Equal(60, HistoryEntry.Speed(1, 0.2));
        Assert.Equal(60, HistoryEntry.Speed(1, 1.0));
        Assert.Equal(120, HistoryEntry.Speed(4, 2.0));
    }

    [Fact]
    public void Statistics_of_an_empty_history_are_zero_and_not_invented()
    {
        var stats = Stats.From([], DateTime.Now);
        Assert.Equal(new Stats(0, 0, 0, 0), stats);
    }

    [Fact]
    public void Statistics_count_the_last_seven_days()
    {
        var now = new DateTime(2026, 8, 8, 12, 0, 0);
        var history = new[]
        {
            Entry(now.AddDays(-1), words: 100, seconds: 60, application: "Notepad", wpm: 100),
            Entry(now.AddDays(-3), words: 50, seconds: 30, application: "Chrome", wpm: 100),
            // Старше недели: в цифры недели не идёт, но в среднем темпе учитывается.
            Entry(now.AddDays(-30), words: 900, seconds: 600, application: "Word", wpm: 40),
        };

        var stats = Stats.From(history, now);

        Assert.Equal(150, stats.WordsThisWeek);
        Assert.Equal(2, stats.AppsUsed);
        Assert.Equal(80, stats.AverageWpm);
    }

    [Fact]
    public void The_same_application_is_counted_once()
    {
        var now = new DateTime(2026, 8, 8, 12, 0, 0);
        var history = new[]
        {
            Entry(now.AddHours(-1), 10, 10, "Notepad", 60),
            Entry(now.AddHours(-2), 10, 10, "notepad", 60),
            Entry(now.AddHours(-3), 10, 10, "", 60),
        };

        Assert.Equal(1, Stats.From(history, now).AppsUsed);
    }

    [Fact]
    public void Saved_time_assumes_dictation_is_twice_as_fast_as_typing()
    {
        var now = new DateTime(2026, 8, 8, 12, 0, 0);
        // Час диктовки — два часа набора, из них сэкономлен один… но считается
        // так же, как считала питоновская версия: длительность на два.
        var history = new[] { Entry(now.AddHours(-1), 1000, 3600, "Word", 100) };
        Assert.Equal(2, Stats.From(history, now).HoursSaved);
    }

    [Fact]
    public void A_week_with_a_single_short_dictation_shows_an_hour_not_zero()
    {
        var now = new DateTime(2026, 8, 8, 12, 0, 0);
        var history = new[] { Entry(now.AddHours(-1), 5, 4, "Word", 75) };
        Assert.Equal(1, Stats.From(history, now).HoursSaved);
    }

    [Fact]
    public void Search_shows_the_newest_first()
    {
        var items = HistoryStore.Parse(LegacyJson);
        var found = HistoryPane.Filter(items, "");

        Assert.Equal(2, found.Count);
        Assert.Equal("Это тест.", found[0].Clean);
    }

    [Fact]
    public void Search_looks_at_the_text_the_raw_text_the_mode_and_the_application()
    {
        var items = HistoryStore.Parse(LegacyJson);

        Assert.Single(HistoryPane.Filter(items, "Visual Studio"));
        Assert.Single(HistoryPane.Filter(items, "Super"));
        Assert.Single(HistoryPane.Filter(items, "привет мир"));
        Assert.Equal(2, HistoryPane.Filter(items, "т").Count);
        Assert.Empty(HistoryPane.Filter(items, "такого точно нет"));
    }

    [Fact]
    public void Search_does_not_care_about_capitals()
    {
        var items = HistoryStore.Parse(LegacyJson);
        Assert.Single(HistoryPane.Filter(items, "ПРИВЕТ"));
    }

    private static HistoryEntry Entry(
        DateTime at, int words, int seconds, string application, int wpm) => new()
        {
            Time = at.ToString(HistoryEntry.TimeFormat, CultureInfo.InvariantCulture),
            Mode = "Voice to Text",
            Raw = "",
            Clean = new string('x', words),
            Words = words,
            Duration = seconds,
            Wpm = wpm,
            Application = application,
        };
}
