using System.Windows.Media.Animation;

namespace LocalWhisper.UI;

/// <summary>
/// Числовая половина темы — то, что читает код, а не разметка.
/// Перенесена из Popup вместе с длительностями: два окна одного хозяина должны
/// не только выглядеть, но и двигаться одинаково.
/// </summary>
public static class Theme
{
    public const double WindowRadius = 12;
    public const double CardRadius = 10;
    public const double RowRadius = 7;

    /// <summary>
    /// Раскрытие и схлопывание. В оригинале это пружина со слабым перелётом;
    /// в WPF пружин нет, поэтому <see cref="BackEase"/> с малой амплитудой —
    /// содержимое чуть проскакивает конечный размер и возвращается.
    /// </summary>
    public static Duration OpenDuration => new(TimeSpan.FromSeconds(0.30));

    public static IEasingFunction OpenEasing => new BackEase
    {
        Amplitude = 0.12,
        EasingMode = EasingMode.EaseOut,
    };

    /// <summary>
    /// Смена разделов: уходящий исчезает быстрее, чем приходит новый, — так они
    /// никогда не висят оба полупрозрачными.
    /// </summary>
    public static Duration PaneInDuration => new(TimeSpan.FromSeconds(0.20));
    public static TimeSpan PaneInDelay => TimeSpan.FromSeconds(0.04);

    /// <summary>
    /// Появление и исчезновение.
    ///
    /// Задержка подставляется нулём, а не <c>null</c>: у <c>Timeline</c> пустой
    /// <c>BeginTime</c> означает «не запускать никогда», а вовсе не «начать
    /// сразу», и такая анимация молча не проигрывается.
    /// </summary>
    public static DoubleAnimation Fade(double to, Duration duration, TimeSpan? delay = null) => new()
    {
        To = to,
        Duration = duration,
        BeginTime = delay ?? TimeSpan.Zero,
        FillBehavior = FillBehavior.HoldEnd,
    };
}
