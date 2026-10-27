using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Path = System.Windows.Shapes.Path;

namespace LocalWhisper.UI;

/// <summary>
/// Мелочь, из которой собраны все разделы: карточки, строки, иконки, поля.
///
/// Вынесена сюда не ради экономии строк, а ради того, чтобы шесть разделов
/// выглядели одним окном. Как только у каждого заводится свой отступ и свой
/// оттенок серого, окно перестаёт читаться как единое целое, а поправить это
/// потом — значит править шесть мест. Прежний LocalWhisper жил именно так:
/// стили лежали строками прямо в разделах, и один и тот же серый был записан
/// в файле девять раз.
///
/// Текст сюда приезжает уже переведённым: <see cref="Services.Strings.L"/>
/// зовёт тот, кто знает, что именно он показывает.
/// </summary>
public static class PaneKit
{
    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>Контурная иконка из <c>Icons.xaml</c>.</summary>
    public static Path Icon(string key, string brushKey = "TextSecondary", double scale = 1)
    {
        var path = new Path
        {
            Data = (Geometry)(Application.Current.TryFindResource(key) ?? Geometry.Empty),
            Stroke = Brush(brushKey),
            StrokeThickness = 1.3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 16,
            Height = 16,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (Math.Abs(scale - 1) > 0.001)
        {
            path.RenderTransformOrigin = new Point(0.5, 0.5);
            path.RenderTransform = new ScaleTransform(scale, scale);
        }
        return path;
    }

    public static TextBlock Text(
        string text, double size = 12, string brushKey = "TextPrimary", bool bold = false) => new()
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Brush(brushKey),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

    /// <summary>Заголовок раздела.</summary>
    public static TextBlock Title(string text) => new()
    {
        Text = text,
        FontSize = 17,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush("TextPrimary"),
        Margin = new Thickness(0, 0, 0, 2),
    };

    /// <summary>Подпись под заголовком: зачем этот раздел вообще нужен.</summary>
    public static TextBlock Subtitle(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Brush("TextSecondary"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 4),
    };

    /// <summary>Вертикальный список с тонкой полосой прокрутки.</summary>
    public static ScrollViewer List(Panel content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Background = Brushes.Transparent,
        Padding = new Thickness(0, 0, 6, 0),
    };

    /// <summary>Строка списка: подсвечивается наведением, отвечает на клик.</summary>
    public static Border Row(UIElement content, Action? onClick = null)
    {
        var row = new Border
        {
            Child = content,
            CornerRadius = new CornerRadius(Theme.RowRadius),
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 7, 8, 7),
            Margin = new Thickness(0, 0, 0, 3),
            Cursor = onClick is null ? Cursors.Arrow : Cursors.Hand,
        };
        row.MouseEnter += (_, _) => row.Background = Brush("Surface");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        if (onClick is not null) row.MouseLeftButtonUp += (_, _) => onClick();
        return row;
    }

    /// <summary>Плоская карточка — фон под группой, а не рамка вокруг неё.</summary>
    public static Border Card(UIElement child, Thickness? padding = null, Action? onClick = null)
    {
        var card = new Border
        {
            Child = child,
            CornerRadius = new CornerRadius(Theme.CardRadius),
            Background = Brush("Surface"),
            Padding = padding ?? new Thickness(16, 14, 16, 14),
            Cursor = onClick is null ? Cursors.Arrow : Cursors.Hand,
        };
        if (onClick is not null)
        {
            card.MouseEnter += (_, _) => card.Background = Brush("SurfaceHover");
            card.MouseLeave += (_, _) => card.Background = Brush("Surface");
            card.MouseLeftButtonUp += (_, _) => onClick();
        }
        return card;
    }

    /// <summary>Маленькая круглая кнопка с иконкой — удалить, скопировать, изменить.</summary>
    public static Button IconButton(string iconKey, Action onClick, double size = 26, string? tooltip = null)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("NotchButton"),
            Width = size,
            Height = size,
            Content = Icon(iconKey, "TextTertiary", 0.85),
            ToolTip = tooltip,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Кнопка с подписью.</summary>
    public static Button TextButton(string label, Action onClick, string? iconKey = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (iconKey is not null)
        {
            var icon = Icon(iconKey, "TextSecondary", 0.8);
            icon.Margin = new Thickness(0, 0, 6, 0);
            content.Children.Add(icon);
        }
        content.Children.Add(Text(label, 11, "TextSecondary"));

        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("NotchTextButton"),
            Content = content,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>
    /// Клавиша на подпись: серый чип с моноширинным текстом. Тем же приёмом
    /// сочетания показаны в списке режимов и на главной.
    /// </summary>
    public static Border Badge(string text) => new()
    {
        Background = Brush("Surface"),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(7, 2, 7, 3),
        Margin = new Thickness(0, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            Foreground = Brush("TextSecondary"),
        },
    };

    /// <summary>Ряд чипов — сочетание клавиш целиком.</summary>
    public static StackPanel Keys(IEnumerable<string> parts)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var part in parts) panel.Children.Add(Badge(part));
        return panel;
    }

    /// <summary>Подпись «здесь пока ничего нет», одинаковая во всех разделах.</summary>
    public static UIElement Empty(string text)
    {
        var label = Text(text, 12, "TextTertiary");
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextTrimming = TextTrimming.None;
        label.Margin = new Thickness(0, 28, 0, 28);
        return label;
    }

    /// <summary>Поле ввода в стиле окна.</summary>
    public static TextBox Field(bool multiline = false) => new()
    {
        Style = (Style)Application.Current.FindResource("NotchTextBox"),
        AcceptsReturn = multiline,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
        VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
    };

    /// <summary>Подложка поля: тот же фон и радиус, что у карточек и кнопок.</summary>
    public static Border FieldBox(UIElement content, double height = 32) => new()
    {
        Child = content,
        Background = Brush("Surface"),
        CornerRadius = new CornerRadius(Theme.RowRadius),
        Padding = new Thickness(10, 6, 10, 6),
        MinHeight = height,
    };

    /// <summary>
    /// Подсказка в пустом поле. Рисуется отдельным слоем под полем, а не
    /// текстом внутри него: текст-заглушку внутри поля приходится стирать
    /// вручную, и первым, что человек делает в окне, оказывается уборка.
    /// </summary>
    public static Grid WithPlaceholder(TextBox box, string placeholder)
    {
        var hint = Text(placeholder, box.FontSize, "TextTertiary");
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = box.VerticalContentAlignment;

        void Sync() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Sync();
        Sync();

        var grid = new Grid();
        grid.Children.Add(hint);
        grid.Children.Add(box);
        return grid;
    }

    /// <summary>Выпадающий список из готовых подписей.</summary>
    public static ComboBox Combo(IEnumerable<string> items, string? selected, Action<string> onChange)
    {
        var combo = new ComboBox
        {
            Style = (Style)Application.Current.FindResource("NotchComboBox"),
        };
        foreach (var item in items) combo.Items.Add(item);
        if (selected is not null && combo.Items.Contains(selected)) combo.SelectedItem = selected;
        else if (combo.Items.Count > 0) combo.SelectedIndex = 0;

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string value) onChange(value);
        };
        return combo;
    }

    /// <summary>
    /// Выпадающий список, у которого показанное и сохранённое — разные вещи.
    /// В файле лежит <c>clipboard</c>, а человек читает «через буфер»; заводить
    /// ради этого второй словарь в каждом разделе не стоит.
    /// </summary>
    public static ComboBox Combo(
        IReadOnlyList<(string Value, string Label)> options, string? selected, Action<string> onChange)
    {
        var combo = Combo(options.Select(o => o.Label),
            options.FirstOrDefault(o => o.Value == selected).Label ?? options[0].Label,
            label =>
            {
                var match = options.FirstOrDefault(o => o.Label == label);
                if (match.Value is not null) onChange(match.Value);
            });
        return combo;
    }

    public static CheckBox Check(string label, bool value, Action<bool> onChange)
    {
        var box = new CheckBox
        {
            Style = (Style)Application.Current.FindResource("NotchCheckBox"),
            Content = Text(label, 12),
            IsChecked = value,
        };
        box.Checked += (_, _) => onChange(true);
        box.Unchecked += (_, _) => onChange(false);
        return box;
    }

    /// <summary>
    /// Строка настройки: подпись слева, поле справа. Одинаковая ширина колонки
    /// подписей во всём окне — то, из-за чего список настроек читается сверху
    /// вниз, а не рассыпается на отдельные строки.
    /// </summary>
    public static Grid FormRow(string label, UIElement control, string? hint = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var caption = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        caption.Children.Add(Text(label, 12, "TextPrimary"));
        if (hint is not null)
        {
            var note = Text(hint, 10.5, "TextTertiary");
            note.TextTrimming = TextTrimming.None;
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(0, 2, 12, 0);
            caption.Children.Add(note);
        }
        caption.Margin = new Thickness(0, 0, 12, 0);

        Grid.SetColumn(caption, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(caption);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>Одна цифра на главной: крупное число и подпись под ним.</summary>
    public static StackPanel Stat(string value, string caption)
    {
        var panel = new StackPanel { MinWidth = 110 };
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextPrimary"),
        });
        panel.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = Brush("TextSecondary"),
        });
        return panel;
    }

    /// <summary>Волосяная линия между колонками статистики.</summary>
    public static Border Divider() => new()
    {
        Width = 1,
        Background = Brush("Hairline"),
        Margin = new Thickness(18, 2, 18, 2),
    };

    /// <summary>
    /// Укрытие содержимого. Размытие, а не звёздочки: размытая строка сохраняет
    /// силуэт — видно, что это ключ, а не адрес, — но прочитать её через плечо
    /// нельзя.
    /// </summary>
    public static void ApplyCover(UIElement element, bool covered) =>
        element.Effect = covered ? new BlurEffect { Radius = 7, KernelType = KernelType.Gaussian } : null;
}
