using System.Windows.Controls;
using System.Windows.Input;
using LocalWhisper.Model;
using LocalWhisper.Services;

namespace LocalWhisper.UI.Panes;

/// <summary>
/// Словарь: имена, термины и сокращения, которые модель иначе слышит неверно.
///
/// Слова уезжают в подсказку модели одной строкой через запятую — так же, как
/// это делала питоновская версия. Это не переобучение, а намёк: модель начинает
/// ждать эти слова и перестаёт заменять их похожими.
/// </summary>
public sealed class VocabularyPane(AppController app, Action refresh) : Pane(app, refresh)
{
    public override UIElement Build()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(PaneKit.Title(Strings.L("Vocabulary")));
        header.Children.Add(PaneKit.Subtitle(
            Strings.L("Add names, terms and abbreviations so the model hears them right")));
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var input = PaneKit.Field();
        var adder = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        adder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        adder.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = PaneKit.FieldBox(PaneKit.WithPlaceholder(input, Strings.L("New word")));
        Grid.SetColumn(box, 0);
        adder.Children.Add(box);

        void Add()
        {
            var word = input.Text.Trim();
            if (word.Length == 0) return;
            // Повтор в подсказке ничего не усиливает, только занимает место в
            // ограниченном по длине приглашении.
            if (!App.Config.Vocabulary.Contains(word, StringComparer.CurrentCultureIgnoreCase))
                App.Config.Vocabulary.Add(word);
            input.Clear();
            App.SaveConfig();
            Refresh();
        }

        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Add();
        };

        var button = PaneKit.TextButton(Strings.L("Add word"), Add, "IconPlus");
        button.Margin = new Thickness(8, 0, 0, 0);
        button.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(button, 1);
        adder.Children.Add(button);
        Grid.SetRow(adder, 1);
        root.Children.Add(adder);

        var list = new StackPanel();
        if (App.Config.Vocabulary.Count == 0)
        {
            list.Children.Add(PaneKit.Empty(Strings.L("Vocabulary is empty")));
        }
        else
        {
            foreach (var word in App.Config.Vocabulary) list.Children.Add(BuildRow(word));
        }

        var scroll = PaneKit.List(list);
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        input.Focus();
        return root;
    }

    private UIElement BuildRow(string word)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = PaneKit.Text(word, 12.5);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var remove = PaneKit.IconButton("IconTrash", () =>
        {
            App.Config.Vocabulary.Remove(word);
            App.SaveConfig();
            Refresh();
        }, tooltip: Strings.L("Delete"));
        Grid.SetColumn(remove, 1);
        grid.Children.Add(remove);

        return PaneKit.Row(grid);
    }
}
