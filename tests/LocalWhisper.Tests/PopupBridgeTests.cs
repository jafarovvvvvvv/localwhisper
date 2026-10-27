using System.Text.Json;
using LocalWhisper.Services;

namespace LocalWhisper.Tests;

/// <summary>
/// Что уезжает в панель Popup.
///
/// Формат описан в <c>docs/pipe.md</c> самой панели, и разойтись с ним молча —
/// значит потерять полоску наверху экрана, не увидев ни одной ошибки: канал
/// принимает любую строку и про непонятную просто молчит.
/// </summary>
public class PopupBridgeTests
{
    private static JsonElement Parse(ActivityTone tone, string label) =>
        JsonDocument.Parse(PopupBridge.Compose(tone, label)).RootElement;

    [Fact]
    public void Always_says_who_is_speaking()
    {
        var message = Parse(ActivityTone.Live, "0:07");

        Assert.Equal("activity", message.GetProperty("type").GetString());
        // По source панель и убирает полоску — чужую не тронет.
        Assert.Equal("localwhisper", message.GetProperty("source").GetString());
        Assert.Equal("LocalWhisper", message.GetProperty("name").GetString());
    }

    [Fact]
    public void A_running_dictation_is_live_with_a_timer_and_a_stop_button()
    {
        var message = Parse(ActivityTone.Live, "0:07");

        Assert.Equal("live", message.GetProperty("tone").GetString());
        Assert.Equal("0:07", message.GetProperty("label").GetString());

        var actions = message.GetProperty("actions");
        Assert.Equal(1, actions.GetArrayLength());
        Assert.Equal("stop", actions[0].GetProperty("id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(actions[0].GetProperty("label").GetString()));
    }

    [Fact]
    public void Recognition_is_busy_and_offers_nothing_to_press()
    {
        var message = Parse(ActivityTone.Busy, "0:12");

        Assert.Equal("busy", message.GetProperty("tone").GetString());
        Assert.Equal("0:12", message.GetProperty("label").GetString());
        // Останавливать нечего: работа идёт без участия человека.
        Assert.False(message.TryGetProperty("actions", out _));
    }

    [Fact]
    public void Success_is_ok()
    {
        Assert.Equal("ok", Parse(ActivityTone.Ok, "").GetProperty("tone").GetString());
    }

    [Fact]
    public void Idle_removes_the_strip_and_carries_nothing_else()
    {
        var message = Parse(ActivityTone.Idle, "0:07");

        Assert.Equal("idle", message.GetProperty("tone").GetString());
        Assert.False(message.TryGetProperty("label", out _));
        Assert.False(message.TryGetProperty("actions", out _));
    }

    [Fact]
    public void One_message_is_always_one_line()
    {
        // Протокол построчный: перенос внутри сообщения разорвал бы его надвое.
        foreach (var tone in Enum.GetValues<ActivityTone>())
        {
            Assert.DoesNotContain('\n', PopupBridge.Compose(tone, "0:07"));
        }
    }

    [Fact]
    public void Cyrillic_travels_as_cyrillic()
    {
        // Экранирование по умолчанию превратило бы «Стоп» в набор \uXXXX:
        // формально верно, но журнал панели после этого не прочитать.
        Assert.DoesNotContain("\\u0", PopupBridge.Compose(ActivityTone.Live, "0:07"));
    }
}
