using LocalWhisper.Interop;

namespace LocalWhisper.Services;

/// <summary>Что именно случилось — от этого зависит высота сигнала.</summary>
public enum Chime
{
    Start,
    Stop,
    Success,
    ModeSwitch,
}

/// <summary>
/// Короткие сигналы: началась запись, закончилась, текст лёг на место.
///
/// Ноты и длительности перенесены из питоновской версии один в один. Смысл у
/// них ровно один: не глядя на экран понять, слышит ли тебя приложение, — а для
/// этого важно, чтобы звук не менялся от версии к версии.
/// </summary>
public static class Sound
{
    public static (uint Frequency, uint Duration) Tone(Chime chime) => chime switch
    {
        Chime.Start => (900u, 100u),
        Chime.Stop => (700u, 100u),
        Chime.Success => (1100u, 120u),
        Chime.ModeSwitch => (1000u, 80u),
        _ => (0u, 0u),
    };

    /// <summary>
    /// Сигнал звучит в отдельном потоке: <c>Beep</c> держит вызвавшего до конца
    /// звучания, и сотня миллисекунд тишины в интерфейсе на каждой диктовке —
    /// это ровно то подтормаживание, за которое приложения не любят.
    /// </summary>
    public static void Play(Chime chime, bool enabled)
    {
        if (!enabled) return;
        var (frequency, duration) = Tone(chime);
        if (frequency == 0) return;

        _ = Task.Run(() =>
        {
            try
            {
                Win32.Beep(frequency, duration);
            }
            catch (Exception e)
            {
                Log.Warn("sound", $"сигнал не прозвучал: {e.Message}");
            }
        });
    }
}
