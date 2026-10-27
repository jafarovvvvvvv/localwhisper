using LocalWhisper.Model;

namespace LocalWhisper.UI.Panes;

/// <summary>
/// Один раздел окна.
///
/// Разделы собираются заново целиком при каждом показе, а не правятся по
/// кусочкам. Так дешевле думать: состояние живёт в настройках и истории, а не
/// размазано по дереву элементов, и «перерисовать» всегда означает одно и то же.
/// Разделы здесь маленькие, и цена такой сборки — доли миллисекунды.
/// </summary>
public abstract class Pane(AppController app, Action refresh)
{
    protected AppController App { get; } = app;

    /// <summary>Перерисовать себя — например после правки, сделанной прямо в разделе.</summary>
    protected void Refresh() => refresh();

    /// <summary>Перейти в другой раздел по его номеру в боковой колонке.</summary>
    public Action<int>? Navigate { get; set; }

    public abstract UIElement Build();

    // Номера разделов — по порядку в боковой колонке. Держатся здесь, чтобы
    // переходы между разделами читались словами, а не цифрами.
    public const int HomeIndex = 0;
    public const int ModesIndex = 1;
    public const int VocabularyIndex = 2;
    public const int SettingsIndex = 3;
    public const int SoundIndex = 4;
    public const int HistoryIndex = 5;
}
