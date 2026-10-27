using System.Windows.Controls;
using System.Windows.Media;
using LocalWhisper.Interop;
using LocalWhisper.Model;
using LocalWhisper.Services;

namespace LocalWhisper.UI.Panes;

/// <summary>
/// Режимы: у каждого своя клавиша, свой язык и своя чистка текста.
///
/// Активный режим отмечен зелёной точкой — это тот, что запускает кнопка на
/// пилюле и <c>Ctrl+Shift+M</c>. Клавиша самого режима работает всегда и
/// активность не смотрит: нажал сочетание супер-режима — получил супер-режим,
/// какой бы режим ни считался активным.
/// </summary>
public sealed class ModesPane(AppController app, Action refresh) : Pane(app, refresh)
{
    /// <summary>
    /// Режимы, которые нельзя удалить. Не прихоть: на «default» ссылается
    /// <c>active_mode_id</c> по умолчанию, и приложение без единого режима
    /// перестаёт быть приложением — ни одна клавиша ничего не делает.
    /// </summary>
    private static readonly string[] Permanent = ["default", "super_mode"];

    public override UIElement Build()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = PaneKit.Title(Strings.L("Modes"));
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        var create = PaneKit.TextButton(Strings.L("New mode"), CreateMode, "IconPlus");
        create.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(create, 1);
        header.Children.Add(create);

        header.Margin = new Thickness(0, 0, 0, 14);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var list = new StackPanel();
        if (App.Config.Modes.Count == 0)
        {
            list.Children.Add(PaneKit.Empty(Strings.L("No modes yet")));
        }
        else
        {
            foreach (var mode in App.Config.Modes) list.Children.Add(BuildRow(mode));
        }

        var scroll = PaneKit.List(list);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(2, 14, 0, 0),
        };
        footer.Children.Add(PaneKit.Keys(HotkeyCombination.Parts(AppController.CycleHotkey)));
        var caption = PaneKit.Text(Strings.L("Change active mode"), 11, "TextTertiary");
        caption.Margin = new Thickness(6, 0, 0, 0);
        footer.Children.Add(caption);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private UIElement BuildRow(Mode mode)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = PaneKit.Icon(mode.SuperMode ? "IconModes" : "IconMic", "TextTertiary");
        icon.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(PaneKit.Text(mode.Name, 12.5, "TextPrimary", bold: true));

        if (mode.Id == App.Config.ActiveModeId)
        {
            nameRow.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = PaneKit.Brush("Success"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        text.Children.Add(nameRow);

        var details = string.Join(" · ", new[]
        {
            mode.Language == "auto" ? Strings.L("auto") : mode.Language,
            mode.SuperMode ? Strings.L("Clean the text up with an LLM") : null,
        }.Where(part => !string.IsNullOrEmpty(part)));
        var caption = PaneKit.Text(details, 11, "TextTertiary");
        caption.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(caption);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var keys = PaneKit.Keys(HotkeyCombination.Parts(mode.Hotkey));
        keys.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(keys, 2);
        grid.Children.Add(keys);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (mode.Id != App.Config.ActiveModeId)
        {
            actions.Children.Add(PaneKit.IconButton("IconCheck",
                () => App.SetActiveMode(mode.Id), tooltip: Strings.L("Set active")));
        }
        actions.Children.Add(PaneKit.IconButton("IconEdit",
            () => EditMode(mode), tooltip: Strings.L("Edit")));
        if (!Permanent.Contains(mode.Id))
        {
            actions.Children.Add(PaneKit.IconButton("IconTrash",
                () => DeleteMode(mode), tooltip: Strings.L("Delete")));
        }
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        var row = PaneKit.Row(grid);
        row.Background = PaneKit.Brush("Surface");
        row.Padding = new Thickness(14, 10, 8, 10);
        row.Margin = new Thickness(0, 0, 0, 6);
        row.MouseEnter += (_, _) => row.Background = PaneKit.Brush("SurfaceHover");
        row.MouseLeave += (_, _) => row.Background = PaneKit.Brush("Surface");
        return row;
    }

    // MARK: - Правка

    private void CreateMode()
    {
        var draft = new Mode
        {
            Id = Guid.NewGuid().ToString(),
            Language = "auto",
        };
        if (!ModeDialog.Show(Owner(), draft, Strings.L("Create mode"))) return;
        App.Config.Modes.Add(draft);
        App.SaveConfig();
        Refresh();
    }

    private void EditMode(Mode mode)
    {
        var draft = mode.Clone();
        if (!ModeDialog.Show(Owner(), draft, Strings.L("Edit mode"))) return;

        var index = App.Config.Modes.FindIndex(m => m.Id == mode.Id);
        if (index < 0) return;
        App.Config.Modes[index] = draft;
        App.SaveConfig();
        Refresh();
    }

    private void DeleteMode(Mode mode)
    {
        App.Config.Modes.RemoveAll(m => m.Id == mode.Id);
        // Удалили тот, что был активным, — активным становится первый
        // оставшийся. Иначе кнопка на пилюле указывала бы в пустоту.
        if (App.Config.ActiveModeId == mode.Id && App.Config.Modes.Count > 0)
            App.Config.ActiveModeId = App.Config.Modes[0].Id;
        App.SaveConfig();
        Refresh();
    }

    private static Window? Owner() =>
        Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
}
