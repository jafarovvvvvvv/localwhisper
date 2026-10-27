using System.Globalization;
using System.Windows.Controls;
using LocalWhisper.Model;
using LocalWhisper.Services;

namespace LocalWhisper.UI.Panes;

/// <summary>
/// История расшифровок: что было сказано, когда, каким режимом и куда уехало.
///
/// Новые сверху — их и ищут. Сырой текст показывается только тогда, когда он
/// отличается от вычищенного: одинаковые строки подряд читаются как ошибка
/// приложения, а не как «чистить было нечего».
/// </summary>
public sealed class HistoryPane(AppController app, Action refresh) : Pane(app, refresh)
{
    private string _query = "";

    public override UIElement Build()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });

        var title = PaneKit.Title(Strings.L("History"));
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        var search = PaneKit.Field();
        search.Text = _query;
        search.TextChanged += (_, _) =>
        {
            _query = search.Text;
            Refresh();
        };
        var searchRow = new Grid();
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = PaneKit.Icon("IconSearch", "TextTertiary", 0.8);
        icon.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(icon, 0);
        searchRow.Children.Add(icon);
        var field = PaneKit.WithPlaceholder(search, Strings.L("Search"));
        Grid.SetColumn(field, 1);
        searchRow.Children.Add(field);

        var searchBox = PaneKit.FieldBox(searchRow, 30);
        searchBox.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(searchBox, 1);
        header.Children.Add(searchBox);

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var list = new StackPanel();
        var items = Filter(App.History.Items, _query);

        if (App.History.Items.Count == 0) list.Children.Add(PaneKit.Empty(Strings.L("Nothing recorded yet")));
        else if (items.Count == 0) list.Children.Add(PaneKit.Empty(Strings.L("Nothing found")));
        else foreach (var entry in items) list.Children.Add(BuildCard(entry));

        var scroll = PaneKit.List(list);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        // Курсор в поиске сразу: раздел открывают, чтобы что-то найти.
        if (_query.Length > 0) search.CaretIndex = _query.Length;
        return root;
    }

    /// <summary>
    /// Отбор по подстроке — по обоим вариантам текста и по имени приложения.
    /// Открыто ради тестов: поиск, который молча не находит очевидное, хуже
    /// отсутствующего.
    /// </summary>
    public static IReadOnlyList<HistoryEntry> Filter(IReadOnlyList<HistoryEntry> items, string query)
    {
        var trimmed = query.Trim();
        var newestFirst = items.Reverse();
        if (trimmed.Length == 0) return newestFirst.ToList();

        return newestFirst
            .Where(entry =>
                Contains(entry.Clean, trimmed) ||
                Contains(entry.Raw, trimmed) ||
                Contains(entry.Application, trimmed) ||
                Contains(entry.Mode, trimmed))
            .ToList();
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    private UIElement BuildCard(HistoryEntry entry)
    {
        var stack = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var when = PaneKit.Text(FormatTime(entry), 10.5, "TextTertiary");
        Grid.SetColumn(when, 0);
        header.Children.Add(when);

        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 0, 0, 0),
        };
        if (entry.Mode.Length > 0) meta.Children.Add(PaneKit.Badge(entry.Mode));
        if (entry.Application.Length > 0) meta.Children.Add(PaneKit.Badge(entry.Application));
        meta.Children.Add(PaneKit.Badge($"{entry.Words} {Strings.L("words")}"));
        meta.Children.Add(PaneKit.Badge($"{entry.Wpm} {Strings.L("WPM")}"));
        Grid.SetColumn(meta, 1);
        header.Children.Add(meta);

        var copy = PaneKit.IconButton("IconCopy", () => CopyText(entry.Clean), 22, Strings.L("Copy"));
        Grid.SetColumn(copy, 2);
        header.Children.Add(copy);

        stack.Children.Add(header);

        var text = PaneKit.Text(entry.Clean, 12.5);
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        text.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(text);

        if (entry.Raw.Length > 0 && entry.Raw != entry.Clean)
        {
            var raw = PaneKit.Text($"{Strings.L("Raw")}: {entry.Raw}", 11, "TextTertiary");
            raw.TextWrapping = TextWrapping.Wrap;
            raw.TextTrimming = TextTrimming.None;
            raw.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(raw);
        }

        var card = PaneKit.Card(stack, new Thickness(16, 12, 12, 14));
        card.Margin = new Thickness(0, 0, 0, 8);
        return card;
    }

    private static string FormatTime(HistoryEntry entry) =>
        entry.At is { } at
            ? at.ToString("d MMMM, HH:mm", Strings.Culture)
            : entry.Time;

    private void CopyText(string text)
    {
        try
        {
            Clipboard.SetDataObject(text, copy: true);
            App.Notify(Strings.L("Copied"));
        }
        catch (Exception e)
        {
            Log.Warn("history", $"не скопировать: {e.Message}");
        }
    }
}
