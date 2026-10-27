using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LocalWhisper.Model;
using LocalWhisper.Services;
using LocalWhisper.UI.Panes;

namespace LocalWhisper.UI;

/// <summary>
/// Главное окно: боковая колонка с шестью разделами и рабочая область справа.
///
/// Без системной рамки — как и панель Popup, — поэтому заголовок, перетаскивание
/// и кнопки окна нарисованы здесь. Взамен окно выглядит одним предметом, а не
/// чёрным содержимым в светлой рамке Windows.
///
/// Закрытие прячет окно, а не выходит из приложения: диктовка по горячей
/// клавише работает и без единого открытого окна, и закрывать вместе с ним всю
/// программу значило бы каждый раз запускать её заново.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppController _app;
    private readonly List<(Border Row, TextBlock Label, System.Windows.Shapes.Path Icon)> _navRows = [];
    private readonly ContentControl _host = new();
    private readonly List<Pane> _panes;

    private ComboBox? _micCombo;
    private TextBlock? _status;
    private Button? _maximizeButton;
    private Rect _restoreBounds;
    private bool _maximized;
    private int _current;
    private bool _reallyClosing;

    public SettingsWindow(AppController app)
    {
        _app = app;
        _panes =
        [
            new HomePane(app, Refresh),
            new ModesPane(app, Refresh),
            new VocabularyPane(app, Refresh),
            new SettingsPane(app, Refresh),
            new SoundPane(app, Refresh),
            new HistoryPane(app, Refresh),
        ];
        // Карточки на главной ведут в другие разделы: «настроить сочетания» —
        // это тот же список режимов, и заводить для него второе место было бы
        // обманом.
        foreach (var pane in _panes) pane.Navigate = Open;

        Title = "LocalWhisper";
        Width = 1080;
        Height = 720;
        MinWidth = 900;
        MinHeight = 600;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        Content = BuildShell();
        Select(0);

        _app.Changed += OnAppChanged;
        Closing += (_, e) =>
        {
            if (_reallyClosing) return;
            // Крестик прячет окно. Выход живёт в трее — там, где его ищут,
            // когда окна на экране уже нет.
            e.Cancel = true;
            Hide();
        };
    }

    private void OnAppChanged()
    {
        SyncMicrophones();
        if (_status is not null) _status.Text = _app.ModelStatus;
        Refresh();
    }

    /// <summary>Перерисовать текущий раздел. Дёшево: разделы собираются заново целиком.</summary>
    public void Refresh() => Select(_current, animate: false);

    // MARK: - Каркас

    private UIElement BuildShell()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(238) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        grid.Children.Add(sidebar);

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var topBar = BuildTopBar();
        Grid.SetRow(topBar, 0);
        right.Children.Add(topBar);

        _host.Margin = new Thickness(28, 18, 28, 22);
        Grid.SetRow(_host, 1);
        right.Children.Add(_host);

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return new Border
        {
            Margin = new Thickness(14),
            Background = PaneKit.Brush("PanelFill"),
            CornerRadius = new CornerRadius(Theme.WindowRadius),
            BorderBrush = PaneKit.Brush("Hairline"),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 6,
                Direction = 270,
                Opacity = 0.55,
                Color = Colors.Black,
            },
            Child = new Border
            {
                // Обрезаем содержимое по тому же радиусу: иначе боковая колонка
                // вылезает квадратным углом из скруглённого окна.
                CornerRadius = new CornerRadius(Theme.WindowRadius),
                Child = grid,
                ClipToBounds = true,
            },
        };
    }

    private UIElement BuildSidebar()
    {
        var stack = new StackPanel { Margin = new Thickness(14, 20, 14, 14) };

        var title = PaneKit.Text("LocalWhisper", 16, "TextPrimary", bold: true);
        title.Margin = new Thickness(10, 0, 0, 18);
        stack.Children.Add(title);

        var items = new (string Icon, string Title)[]
        {
            ("IconHome", "Home"),
            ("IconModes", "Modes"),
            ("IconVocabulary", "Vocabulary"),
            ("IconSettings", "Settings"),
            ("IconSound", "Sound"),
            ("IconHistory", "History"),
        };

        for (var i = 0; i < items.Length; i++)
        {
            var index = i;
            var icon = PaneKit.Icon(items[i].Icon, "TextTertiary");
            var label = PaneKit.Text(Strings.L(items[i].Title), 12.5, "TextSecondary");

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            icon.Margin = new Thickness(0, 0, 11, 0);
            content.Children.Add(icon);
            content.Children.Add(label);

            var row = new Border
            {
                Child = content,
                Padding = new Thickness(10, 9, 10, 9),
                Margin = new Thickness(0, 0, 0, 2),
                CornerRadius = new CornerRadius(Theme.RowRadius),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            row.MouseEnter += (_, _) =>
            {
                if (index != _current) row.Background = PaneKit.Brush("Surface");
            };
            row.MouseLeave += (_, _) =>
            {
                if (index != _current) row.Background = Brushes.Transparent;
            };
            // Нажатие останавливается здесь и не всплывает до боковой колонки.
            // Иначе его подхватывает перетаскивание окна: DragMove уводит мышь
            // в свой модальный цикл, отпускание до строки уже не доходит, и
            // разделы не переключаются вовсе — при живой подсветке наведения,
            // из-за которой всё выглядит исправным.
            row.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
            row.MouseLeftButtonUp += (_, _) => Select(index);

            _navRows.Add((row, label, icon));
            stack.Children.Add(row);
        }

        var footer = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 10, 4) };

        // Состояние движка стоит здесь, а не в разделе настроек: при первом
        // запуске приложение полтора гигабайта качает модель, и человек,
        // которого об этом не предупредили, решает, что оно просто сломано.
        _status = PaneKit.Text("", 11, "TextSecondary");
        _status.TextTrimming = TextTrimming.None;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 0, 0, 12);
        footer.Children.Add(_status);

        var line = PaneKit.Text(Strings.L("100% local and free"), 11, "TextTertiary");
        line.TextTrimming = TextTrimming.None;
        var line2 = PaneKit.Text(Strings.L("No account, no cloud"), 11, "TextTertiary");
        line2.TextTrimming = TextTrimming.None;
        line2.Margin = new Thickness(0, 2, 0, 0);
        footer.Children.Add(line);
        footer.Children.Add(line2);

        var container = new Grid();
        container.Children.Add(stack);
        container.Children.Add(footer);

        var sidebar = new Border
        {
            Background = PaneKit.Brush("Surface"),
            Child = container,
        };
        // Боковую колонку тоже можно взять и потащить: в окне без рамки за что
        // хвататься — не самоочевидно, и чем больше площадь, тем лучше.
        sidebar.MouseLeftButtonDown += (_, e) => BeginDrag(e);
        return sidebar;
    }

    private UIElement BuildTopBar()
    {
        var grid = new Grid { Margin = new Thickness(20, 0, 12, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Микрофон стоит в шапке, а не в настройках: его меняют чаще всего
        // остального — гарнитуру надели, гарнитуру сняли.
        var micRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var micIcon = PaneKit.Icon("IconMic", "TextTertiary", 0.85);
        micIcon.Margin = new Thickness(0, 0, 8, 0);
        micRow.Children.Add(micIcon);

        _micCombo = new ComboBox
        {
            Style = (Style)Application.Current.FindResource("NotchComboBox"),
            Width = 300,
        };
        _micCombo.SelectionChanged += OnMicrophoneChangedHandler;
        micRow.Children.Add(_micCombo);
        Grid.SetColumn(micRow, 1);
        grid.Children.Add(micRow);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        buttons.Children.Add(WindowButton("IconMinimize", () => WindowState = WindowState.Minimized));
        _maximizeButton = WindowButton("IconMaximize", ToggleMaximize);
        buttons.Children.Add(_maximizeButton);
        buttons.Children.Add(WindowButton("IconClose", Hide, danger: true));
        Grid.SetColumn(buttons, 3);
        grid.Children.Add(buttons);

        var bar = new Border
        {
            Child = grid,
            Background = Brushes.Transparent,
            BorderBrush = PaneKit.Brush("Hairline"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else BeginDrag(e);
        };
        SyncMicrophones();
        return bar;
    }

    private Button WindowButton(string iconKey, Action onClick, bool danger = false)
    {
        var icon = PaneKit.Icon(iconKey, "TextSecondary", 0.85);
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("WindowButton"),
            Content = icon,
        };
        button.Click += (_, _) => onClick();
        if (danger)
        {
            button.MouseEnter += (_, _) => icon.Stroke = PaneKit.Brush("Danger");
            button.MouseLeave += (_, _) => icon.Stroke = PaneKit.Brush("TextSecondary");
        }
        return button;
    }

    private void BeginDrag(MouseButtonEventArgs e)
    {
        if (_maximized || e.ButtonState != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Кнопку успели отпустить — тащить уже нечего.
        }
    }

    /// <summary>
    /// Разворот руками, а не через <c>WindowState.Maximized</c>: у окна без
    /// рамки системный разворот уезжает под панель задач на восемь пикселей с
    /// каждой стороны, и лечится это ровно тем же — своей рабочей областью.
    /// </summary>
    private void ToggleMaximize()
    {
        if (_maximized)
        {
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            _maximized = false;
        }
        else
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            var work = SystemParameters.WorkArea;
            Left = work.Left;
            Top = work.Top;
            Width = work.Width;
            Height = work.Height;
            _maximized = true;
        }

        if (_maximizeButton is not null)
            _maximizeButton.Content = PaneKit.Icon(_maximized ? "IconRestore" : "IconMaximize",
                "TextSecondary", 0.85);
    }

    // MARK: - Микрофон

    private void SyncMicrophones()
    {
        if (_micCombo is null) return;

        var wanted = _app.Config.ActiveMicName;
        var items = new List<string> { Strings.L("Default input device") };
        items.AddRange(_app.Microphones);

        if (_micCombo.Items.Count == items.Count &&
            _micCombo.Items.Cast<string>().SequenceEqual(items))
        {
            return;
        }

        _micCombo.SelectionChanged -= OnMicrophoneChangedHandler;
        _micCombo.Items.Clear();
        foreach (var item in items) _micCombo.Items.Add(item);
        // Имя из конфига может уже ничего не значить: устройство отключили или
        // это ещё подпись «по умолчанию», которую писала прежняя версия.
        var index = items.IndexOf(wanted);
        _micCombo.SelectedIndex = index >= 0 ? index : 0;
        _micCombo.SelectionChanged += OnMicrophoneChangedHandler;
    }

    private void OnMicrophoneChangedHandler(object sender, SelectionChangedEventArgs e) => OnMicrophoneChosen();

    private void OnMicrophoneChosen()
    {
        if (_micCombo?.SelectedItem is not string chosen) return;
        var name = _micCombo.SelectedIndex <= 0 ? "" : chosen;
        if (name == _app.Config.ActiveMicName) return;
        _app.Config.ActiveMicName = name;
        _app.SaveConfig();
    }

    // MARK: - Разделы

    private void Select(int index, bool animate = true)
    {
        if (index < 0 || index >= _panes.Count) return;
        _current = index;

        for (var i = 0; i < _navRows.Count; i++)
        {
            var (row, label, icon) = _navRows[i];
            var active = i == index;
            row.Background = active ? PaneKit.Brush("SurfaceHover") : Brushes.Transparent;
            label.Foreground = PaneKit.Brush(active ? "TextPrimary" : "TextSecondary");
            label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            icon.Stroke = PaneKit.Brush(active ? "TextPrimary" : "TextTertiary");
        }

        _host.Content = _panes[index].Build();
        if (animate)
        {
            _host.Opacity = 0;
            _host.BeginAnimation(OpacityProperty,
                Theme.Fade(1, Theme.PaneInDuration, Theme.PaneInDelay));
        }
    }

    /// <summary>Открыть раздел снаружи — например с карточки на главной.</summary>
    public void Open(int index) => Select(index);

    public void CloseForGood()
    {
        _reallyClosing = true;
        Close();
    }
}
