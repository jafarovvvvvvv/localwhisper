using System.Text.Json;
using LocalWhisper.Services;

namespace LocalWhisper.Tests;

/// <summary>
/// Ровно те ключи, что были, и ни одного лишнего.
///
/// Эти два теста написаны по следам настоящих ошибок, найденных первым же
/// запуском: вычисляемые свойства <c>ActiveMode</c> и <c>At</c> молча уехали
/// в файлы как настоящие поля. Ничего не сломалось — и в этом всё дело: файл
/// начал противоречить сам себе, а заметить это можно было, только открыв его
/// глазами.
/// </summary>
public class FileFormatTests
{
    [Fact]
    public void A_history_record_has_exactly_the_eight_fields_it_always_had()
    {
        var entry = HistoryEntry.Create(
            "Voice to Text", "привет", "Привет!", 3.0, "Notepad", DateTime.Now);

        var json = JsonSerializer.Serialize(new[] { entry }, Config.JsonOptions);
        using var document = JsonDocument.Parse(json);

        var names = document.RootElement[0].EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(
            ["application", "clean", "duration", "mode", "raw", "time", "words", "wpm"],
            names);
    }

    [Fact]
    public void The_settings_file_has_no_invented_keys()
    {
        using var document = JsonDocument.Parse(Config.FromJson("{}").ToJson());
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

        // Ключ, которого никогда не было в питоновской версии и который
        // повторяет уже записанное в modes другими словами.
        Assert.DoesNotContain("active_mode", names);

        // А обещанные — на месте.
        foreach (var key in new[] { "whisper_model", "modes", "active_mode_id", "vocabulary" })
            Assert.Contains(key, names);
    }

    [Fact]
    public void A_mode_has_exactly_the_six_fields_it_always_had()
    {
        using var document = JsonDocument.Parse(Config.FromJson("{}").ToJson());
        var mode = document.RootElement.GetProperty("modes")[0];

        var names = mode.EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(["hotkey", "id", "language", "name", "super_mode", "system_prompt"], names);
    }

    [Fact]
    public void An_untouched_settings_file_survives_a_round_trip_unchanged()
    {
        // Приложение, которое переписывает файл при каждом запуске, рано или
        // поздно перепишет его не так.
        var first = Config.FromJson("{}").ToJson();
        var second = Config.FromJson(first).ToJson();
        Assert.Equal(first, second);
    }
}
