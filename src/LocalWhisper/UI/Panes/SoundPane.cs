using System.Globalization;
using System.Windows.Controls;
using LocalWhisper.Model;
using LocalWhisper.Services;

namespace LocalWhisper.UI.Panes;

/// <summary>
/// Звук: короткие сигналы, по которым слышно, что происходит, не глядя на экран.
///
/// Сигналов ровно четыре, и у каждого своя высота — это и есть весь смысл:
/// начало от конца отличается на слух, без пилюли и без окна.
/// </summary>
public sealed class SoundPane(AppController app, Action refresh) : Pane(app, refresh)
{
    public override UIElement Build()
    {
        var stack = new StackPanel();
        stack.Children.Add(PaneKit.Title(Strings.L("Sound")));
        stack.Children.Add(PaneKit.Subtitle(
            Strings.L("Short chimes when a dictation starts, ends and lands")));

        var toggle = PaneKit.Check(Strings.L("Sound signals"), App.Config.SoundEnabled, enabled =>
        {
            App.Config.SoundEnabled = enabled;
            App.SaveConfig();
            Refresh();
        });
        stack.Children.Add(PaneKit.Card(toggle, new Thickness(16, 14, 16, 14)));

        var list = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var chimes = new (Chime Chime, string Title)[]
        {
            (Chime.Start, Strings.L("Recording started")),
            (Chime.Stop, Strings.L("Recording stopped")),
            (Chime.Success, Strings.L("Text pasted")),
            (Chime.ModeSwitch, Strings.L("Mode switched")),
        };

        foreach (var (chime, title) in chimes) list.Children.Add(BuildRow(chime, title));
        stack.Children.Add(list);

        list.Opacity = App.Config.SoundEnabled ? 1 : 0.4;
        return PaneKit.List(stack);
    }

    private UIElement BuildRow(Chime chime, string title)
    {
        var (frequency, duration) = Sound.Tone(chime);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = PaneKit.Text(title, 12.5);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var tone = PaneKit.Text(
            string.Format(CultureInfo.InvariantCulture, "{0} Hz · {1} ms", frequency, duration),
            11, "TextTertiary");
        tone.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(tone, 1);
        grid.Children.Add(tone);

        // Проверить сигнал можно и с выключенным звуком: иначе, чтобы услышать,
        // что выбираешь, пришлось бы сначала включить, а потом вернуть обратно.
        var play = PaneKit.IconButton("IconPlay", () => App.PlayChime(chime),
            tooltip: Strings.L("Play"));
        Grid.SetColumn(play, 2);
        grid.Children.Add(play);

        return PaneKit.Row(grid);
    }
}
