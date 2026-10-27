using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LocalWhisper.Interop;
using LocalWhisper.Services;

namespace LocalWhisper.UI;

/// <summary>
/// Создание и правка режима.
///
/// Отдельное окно, а не строка в списке: у режима шесть свойств, и половина из
/// них — текст, который пишут, а не выбирают. Править такое прямо в списке
/// значит превращать список в форму.
/// </summary>
public sealed class ModeDialog : Window
{
    private static readonly string[] Languages =
        ["auto", "ru", "en", "es", "fr", "de", "it", "pt", "pl", "tr", "zh", "ja", "ko"];

    private readonly Mode _mode;
    private readonly TextBox _name = PaneKit.Field();
    private readonly TextBox _hotkey = PaneKit.Field();
    private readonly TextBox _prompt = PaneKit.Field(multiline: true);
    private readonly TextBlock _hotkeyHint;

    private CheckBox? _super;
    private Border? _promptBox;

    private ModeDialog(Mode mode, string title)
    {
        _mode = mode;
        _hotkeyHint = PaneKit.Text("", 10.5, "TextTertiary");
        _hotkeyHint.TextTrimming = TextTrimming.None;

        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");

        Content = Build(title);
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) DialogResult = false;
        };
    }

    /// <summary>
    /// Показывает окно правки. Возвращает <c>true</c>, если человек сохранил, —
    /// и только тогда <paramref name="mode"/> оказывается изменённым.
    /// </summary>
    public static bool Show(Window? owner, Mode mode, string title)
    {
        var dialog = new ModeDialog(mode, title);
        if (owner is not null) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }

    private UIElement Build(string title)
    {
        var stack = new StackPanel();
        stack.Children.Add(PaneKit.Title(title));

        _name.Text = _mode.Name;
        stack.Children.Add(Labeled(Strings.L("Mode name"),
            PaneKit.FieldBox(PaneKit.WithPlaceholder(_name, "Voice to Text"))));

        _hotkey.Text = _mode.Hotkey;
        _hotkey.TextChanged += (_, _) => SyncHotkeyHint();
        var hotkeyStack = new StackPanel();
        hotkeyStack.Children.Add(PaneKit.FieldBox(PaneKit.WithPlaceholder(_hotkey, "ctrl+alt+space")));
        _hotkeyHint.Margin = new Thickness(2, 5, 0, 0);
        hotkeyStack.Children.Add(_hotkeyHint);
        stack.Children.Add(Labeled(Strings.L("Global hotkey"), hotkeyStack));
        SyncHotkeyHint();

        stack.Children.Add(Labeled(Strings.L("Speech language"),
            PaneKit.Combo(Languages.Select(Display), Display(_mode.Language),
                value => _mode.Language = Languages.FirstOrDefault(l => Display(l) == value) ?? "auto")));

        _super = PaneKit.Check(Strings.L("Clean the text up with an LLM"), _mode.SuperMode, SyncPrompt);
        _super.Margin = new Thickness(0, 6, 0, 12);
        stack.Children.Add(_super);

        _prompt.Text = _mode.SystemPrompt;
        _prompt.MinHeight = 84;
        _prompt.MaxHeight = 140;
        _promptBox = PaneKit.FieldBox(
            PaneKit.WithPlaceholder(_prompt, Strings.L("Instructions for the cleanup model…")), 84);
        stack.Children.Add(Labeled(Strings.L("System prompt"), _promptBox));
        SyncPrompt(_mode.SuperMode);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        buttons.Children.Add(PaneKit.TextButton(Strings.L("Cancel"), () => DialogResult = false));
        var save = PaneKit.TextButton(Strings.L("Save"), Commit);
        save.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(save);
        stack.Children.Add(buttons);

        return new Border
        {
            Margin = new Thickness(14),
            Background = PaneKit.Brush("PanelFill"),
            CornerRadius = new CornerRadius(Theme.WindowRadius),
            BorderBrush = PaneKit.Brush("Hairline"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22, 20, 22, 20),
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = 0.55,
                Color = Colors.Black,
            },
            Child = stack,
        };
    }

    private static string Display(string language) =>
        language == "auto" ? Strings.L("auto") : language;

    private static UIElement Labeled(string label, UIElement control)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var caption = PaneKit.Text(label, 11.5, "TextSecondary");
        caption.Margin = new Thickness(2, 0, 0, 6);
        stack.Children.Add(caption);
        stack.Children.Add(control);
        return stack;
    }

    /// <summary>
    /// Говорит, разберётся ли сочетание, прямо во время набора. Проверять это
    /// при сохранении поздно: окно уже закрылось, а клавиша молча не работает.
    /// </summary>
    private void SyncHotkeyHint()
    {
        var text = _hotkey.Text.Trim();
        if (text.Length == 0)
        {
            _hotkeyHint.Text = "";
            return;
        }

        if (HotkeyCombination.TryParse(text, out _))
        {
            _hotkeyHint.Text = string.Join(" + ", HotkeyCombination.Parts(text));
            _hotkeyHint.Foreground = PaneKit.Brush("TextTertiary");
        }
        else
        {
            _hotkeyHint.Text = Strings.L("Could not register hotkeys");
            _hotkeyHint.Foreground = PaneKit.Brush("Danger");
        }
    }

    private void SyncPrompt(bool enabled)
    {
        _prompt.IsEnabled = enabled;
        if (_promptBox is not null) _promptBox.Opacity = enabled ? 1 : 0.4;
    }

    private void Commit()
    {
        var name = _name.Text.Trim();
        // Режим без имени — строка-призрак в списке. Вместо диалога с ошибкой
        // просто не выпускаем: поле само показывает, чего не хватает.
        if (name.Length == 0)
        {
            _name.Focus();
            return;
        }

        _mode.Name = name;
        _mode.Hotkey = _hotkey.Text.Trim().ToLowerInvariant();
        _mode.SuperMode = _super?.IsChecked == true;
        _mode.SystemPrompt = _prompt.Text.Trim();
        if (string.IsNullOrWhiteSpace(_mode.Id)) _mode.Id = Guid.NewGuid().ToString();
        DialogResult = true;
    }
}
