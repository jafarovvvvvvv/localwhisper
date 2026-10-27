using LocalWhisper.Services;

namespace LocalWhisper.Tests;

/// <summary>
/// Разбор того, что говорит питоновский движок.
///
/// Это единственная граница, где приложение зависит от чужого формата, и
/// единственное место, где ошибка выглядит как «микрофон не работает». Строки
/// здесь взяты дословно из вывода <c>engine/worker.py</c>, запущенного руками
/// в консоли.
/// </summary>
public class WorkerMessageTests
{
    [Fact]
    public void Reads_the_greeting()
    {
        var message = WorkerMessage.Parse("""{"type": "ready"}""");
        Assert.NotNull(message);
        Assert.Equal("ready", message.Type);
    }

    [Fact]
    public void Reads_the_result_of_a_dictation()
    {
        var message = WorkerMessage.Parse(
            """{"type": "result", "raw": "привет мир", "clean": "Привет, мир!", "duration": 3.744}""");

        Assert.NotNull(message);
        Assert.Equal("result", message.Type);
        Assert.Equal("привет мир", message.Raw);
        Assert.Equal("Привет, мир!", message.Clean);
        Assert.Equal(3.744, message.Duration, 3);
    }

    [Fact]
    public void Reads_the_state_of_the_model()
    {
        var loading = WorkerMessage.Parse("""{"type": "model", "state": "loading", "message": "tiny"}""");
        Assert.Equal("loading", loading?.State);
        Assert.Equal("tiny", loading?.Message);

        var progress = WorkerMessage.Parse(
            """{"type": "model", "state": "progress", "message": "large-v3-turbo · model.bin: 42%"}""");
        Assert.Equal("progress", progress?.State);
        Assert.Contains("42%", progress?.Message);
    }

    [Fact]
    public void Reads_the_fallback_to_the_processor()
    {
        var message = WorkerMessage.Parse(
            """{"type": "device", "device": "cpu", "compute_type": "float32"}""");

        Assert.Equal("cpu", message?.Device);
        Assert.Equal("float32", message?.ComputeType);
    }

    [Fact]
    public void Reads_the_list_of_microphones_including_the_odd_names()
    {
        // Имена устройств Bluetooth содержат перевод строки прямо внутри —
        // в JSON он приезжает экранированным и строку протокола не рвёт.
        var message = WorkerMessage.Parse(
            """{"type": "mics", "items": ["Microphone Array", "Headset (%1 Hands-Free%0\r\n;(AirPods))"]}""");

        Assert.NotNull(message);
        Assert.Equal(2, message.Names.Count);
        Assert.Contains("AirPods", message.Names[1]);
    }

    [Fact]
    public void An_absent_field_is_empty_and_not_a_crash()
    {
        var message = WorkerMessage.Parse("""{"type": "result"}""");

        Assert.NotNull(message);
        Assert.Equal("", message.Raw);
        Assert.Equal("", message.Clean);
        Assert.Equal(0, message.Duration);
        Assert.Empty(message.Names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Traceback (most recent call last):")]
    [InlineData("[Downloader] модель уже на месте")]
    [InlineData("{ half a json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{"no_type": true}""")]
    public void A_line_that_is_not_the_protocol_is_simply_not_a_message(string line)
    {
        // Отладочный print во время разработки попадает в тот же поток — и не
        // должен ронять движок.
        Assert.Null(WorkerMessage.Parse(line));
    }

    [Fact]
    public void Reads_the_whole_conversation_of_one_dictation()
    {
        // Ровно эти строки печатает воркер за одну диктовку, по порядку.
        string[] lines =
        [
            """{"type": "ready"}""",
            """{"type": "model", "state": "loaded", "message": "tiny · cuda"}""",
            """{"type": "started"}""",
            """{"type": "silence"}""",
            """{"type": "processing"}""",
            """{"type": "result", "raw": "Смотри.", "clean": "Смотри.", "duration": 3.744}""",
        ];

        var types = lines.Select(WorkerMessage.Parse).Select(m => m?.Type).ToList();
        Assert.Equal(["ready", "model", "started", "silence", "processing", "result"], types);
    }
}
