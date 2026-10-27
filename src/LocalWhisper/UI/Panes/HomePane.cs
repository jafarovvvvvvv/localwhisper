using System.Globalization;
using System.Windows.Controls;
using LocalWhisper.Interop;
using LocalWhisper.Model;
using LocalWhisper.Services;

namespace LocalWhisper.UI.Panes;

/// <summary>
/// Главная: четыре цифры про то, сколько наговорено, и четыре двери в
/// остальные разделы.
///
/// Цифры считаются из истории — единственного места, где хоть что-то
/// накапливается. Пока истории нет, они честно нулевые: выдуманные числа на
/// первом экране обесценивают настоящие.
/// </summary>
public sealed class HomePane(AppController app, Action refresh) : Pane(app, refresh)
{
    /// <summary>Когда случилось то, о чём написано в «Что нового».</summary>
    private static readonly DateTime ReleaseDate = new(2026, 8, 8);

    public override UIElement Build()
    {
        var stack = new StackPanel();
        stack.Children.Add(PaneKit.Card(BuildStats(), new Thickness(22, 18, 22, 18)));

        var started = PaneKit.Text(Strings.L("Get started"), 12, "TextSecondary", bold: true);
        started.Margin = new Thickness(2, 22, 0, 10);
        stack.Children.Add(started);
        stack.Children.Add(BuildShortcuts());

        var news = PaneKit.Text(Strings.L("What's new"), 12, "TextSecondary", bold: true);
        news.Margin = new Thickness(2, 22, 0, 10);
        stack.Children.Add(news);
        stack.Children.Add(BuildNews());

        return PaneKit.List(stack);
    }

    private UIElement BuildStats()
    {
        var stats = Stats.From(App.History.Items, DateTime.Now);
        var row = new StackPanel { Orientation = Orientation.Horizontal };

        var columns = new (string Value, string Caption)[]
        {
            ($"{stats.AverageWpm} {Strings.L("WPM")}", Strings.L("Average speed")),
            (stats.WordsThisWeek.ToString("N0", Strings.Culture), Strings.L("Words this week")),
            (stats.AppsUsed.ToString(CultureInfo.InvariantCulture), Strings.L("Apps used")),
            ($"{stats.HoursSaved} {Strings.L("h")}", Strings.L("Saved this week")),
        };

        for (var i = 0; i < columns.Length; i++)
        {
            if (i > 0) row.Children.Add(PaneKit.Divider());
            row.Children.Add(PaneKit.Stat(columns[i].Value, columns[i].Caption));
        }
        return row;
    }

    private UIElement BuildShortcuts()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        var activeHotkey = App.Config.ActiveMode?.Hotkey;

        var cards = new (string Icon, string Title, string Text, IReadOnlyList<string> Keys, Action Click)[]
        {
            ("IconRecord", Strings.L("Start recording"), Strings.L("Turn your voice into text"),
                HotkeyCombination.Parts(activeHotkey), App.RequestRecording),
            ("IconModes", Strings.L("Customize your shortcuts"), Strings.L("Every mode has its own hotkey"),
                [], () => Navigate?.Invoke(ModesIndex)),
            ("IconPlus", Strings.L("Create a mode"), Strings.L("Build the perfect mode"),
                [], () => Navigate?.Invoke(ModesIndex)),
            ("IconVocabulary", Strings.L("Add vocabulary"), Strings.L("Teach it names and terms"),
                [], () => Navigate?.Invoke(VocabularyIndex)),
        };

        for (var i = 0; i < cards.Length; i++)
        {
            var card = BuildShortcutCard(cards[i]);
            card.Margin = new Thickness(i % 2 == 0 ? 0 : 6, i < 2 ? 0 : 10, i % 2 == 0 ? 6 : 0, 0);
            Grid.SetColumn(card, i % 2);
            Grid.SetRow(card, i / 2);
            grid.Children.Add(card);
        }

        return grid;
    }

    private static FrameworkElement BuildShortcutCard(
        (string Icon, string Title, string Text, IReadOnlyList<string> Keys, Action Click) card)
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = PaneKit.Icon(card.Icon, "TextSecondary");
        icon.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(icon, 0);
        layout.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(PaneKit.Text(card.Title, 12.5, "TextPrimary", bold: true));
        var caption = PaneKit.Text(card.Text, 11, "TextSecondary");
        caption.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(caption);
        Grid.SetColumn(text, 1);
        layout.Children.Add(text);

        if (card.Keys.Count > 0)
        {
            var keys = PaneKit.Keys(card.Keys);
            keys.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(keys, 2);
            layout.Children.Add(keys);
        }

        return PaneKit.Card(layout, new Thickness(16, 13, 12, 13), card.Click);
    }

    private UIElement BuildNews()
    {
        var stack = new StackPanel();
        // Дата новости, а не сегодняшняя: «что нового» с сегодняшним числом
        // каждый день — это не новость, а часы.
        var date = PaneKit.Text(
            ReleaseDate.ToString("d MMMM yyyy", Strings.Culture).ToUpper(Strings.Culture),
            10.5, "TextTertiary", bold: true);
        stack.Children.Add(date);

        var title = PaneKit.Text(Strings.L("The engine moved to WPF"), 12.5, "TextPrimary", bold: true);
        title.Margin = new Thickness(0, 6, 0, 4);
        stack.Children.Add(title);

        var body = PaneKit.Text(Strings.L(
            "The interface, hotkeys, tray and settings are C# now; recognition stayed on " +
            "faster-whisper in a headless Python worker. The models on disk are the same ones — " +
            "nothing is downloaded again."), 11.5, "TextSecondary");
        body.TextWrapping = TextWrapping.Wrap;
        body.TextTrimming = TextTrimming.None;
        stack.Children.Add(body);

        return PaneKit.Card(stack);
    }
}
