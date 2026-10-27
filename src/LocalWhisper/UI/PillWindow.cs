using System.Globalization;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LocalWhisper.Interop;

namespace LocalWhisper.UI;

/// <summary>Четыре состояния пилюли — больше их не бывает.</summary>
public enum PillState
{
    Idle,
    Recording,
    Processing,
    Success,
}

/// <summary>
/// Пилюля: маленькая полоска в правом нижнем углу, по которой видно, слышит ли
/// приложение голос.
///
/// Окно фиксированного размера, а растёт и уменьшается только нарисованная
/// внутри него пилюля — так её никогда не приходится двигать при смене
/// состояния, и она не дёргается по экрану.
///
/// Три вещи, которые здесь важнее остального:
///
/// * **Окно живёт от старта до выхода.** На его HWND зарегистрированы
///   глобальные клавиши, и <c>WM_HOTKEY</c> приходит именно сюда. Закрытие или
///   пересоздание окна молча сломало бы все сочетания в приложении. Поэтому
///   даже отдав своё дело панели Popup, пилюля не прячется через
///   <c>Hide()</c>, а становится прозрачной и сквозной.
///
/// * **Рисуется в <c>OnRender</c>, а не наследованием от <c>Shape</c>.**
///   <c>Shape</c> считает геометрию один раз и держит её в кэше, который
///   сбрасывается только сменой размера, — а здесь всё меняется при неизменном
///   размере окна.
///
/// * **Наведение определяется опросом курсора, а не событиями мыши.** Пилюля
///   почти всегда click-through, и её собственные <c>MouseMove</c> до неё
///   просто не доходят.
/// </summary>
public sealed class PillWindow : Window
{
    // Размеры пилюли внутри окна. Перенесены из питоновской версии как есть:
    // силуэт узнаваем, и менять его при переезде значило бы отобрать у глаза
    // выученное без всякой выгоды.
    private const double IdleWidth = 74, IdleHeight = 16;
    private const double HoverWidth = 132, HoverHeight = 40;
    private const double RecordWidth = 200, RecordHeight = 42;
    private const double WindowWidth = 240, WindowHeight = 72;

    /// <summary>Доля пути к нужному размеру за кадр. Подобрано под 60 кадров в секунду.</summary>
    private const double Ease = 0.25;

    private readonly PillVisual _visual = new();
    private readonly DispatcherTimer _animation;
    private readonly DispatcherTimer _hover;
    private readonly DispatcherTimer _recordingClock;
    private readonly DispatcherTimer _successHold;

    private bool _clickThrough;
    private bool _hovered;
    private bool _dragging;
    private bool _moved;
    private Vector _dragOffset;
    private int _elapsedMs;

    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action<PillState>? PillStateChanged;

    /// <summary>Пилюлю перетащили. Место запоминается, чтобы она не убегала при перезапуске.</summary>
    public event Action<double, double>? Moved;

    public PillState State { get; private set; } = PillState.Idle;

    /// <summary>Сколько идёт запись — «2:07». Тот же текст уезжает в панель Popup.</summary>
    public string TimeString
    {
        get
        {
            var seconds = _elapsedMs / 1000;
            return $"{seconds / 60}:{seconds % 60:00}";
        }
    }

    /// <summary>Отдала ли пилюля своё дело панели Popup.</summary>
    public bool IsRemote { get; private set; }

    public PillWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = WindowWidth;
        Height = WindowHeight;
        SnapsToDevicePixels = true;
        Content = _visual;

        _animation = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _animation.Tick += (_, _) => Advance();

        _hover = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(30),
        };
        _hover.Tick += (_, _) => PollHover();

        _recordingClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _recordingClock.Tick += (_, _) =>
        {
            _elapsedMs += 100;
            _visual.Timer = TimeString;
            _visual.InvalidateVisual();
        };

        _successHold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _successHold.Tick += (_, _) => SetState(PillState.Idle);

        Loaded += (_, _) => OnReady();
    }

    // MARK: - Место на экране

    /// <summary>
    /// Ставит пилюлю туда, где её оставили, или в правый нижний угол.
    /// Рабочая область, а не весь экран: за панелью задач пилюли не видно.
    /// </summary>
    public void PlaceAt(double? x, double? y)
    {
        var work = SystemParameters.WorkArea;
        var left = x ?? work.Right - WindowWidth - 24;
        var top = y ?? work.Bottom - WindowHeight - 12;

        // Место могло стать невозможным: монитор отключили, разрешение сменили.
        // Пилюля за краем экрана — это пилюля, которой нет.
        if (left < work.Left - WindowWidth + 40 || left > work.Right - 40 ||
            top < work.Top - WindowHeight + 20 || top > work.Bottom - 20)
        {
            left = work.Right - WindowWidth - 24;
            top = work.Bottom - WindowHeight - 12;
        }

        Left = left;
        Top = top;
    }

    private void OnReady()
    {
        var hwnd = Win32.HandleOf(this);
        if (hwnd != IntPtr.Zero)
        {
            // Не в Alt-Tab и не забирает фокус: щелчок по кнопке записи не
            // должен уводить курсор из того окна, куда сейчас поедет текст.
            Win32.ToggleExStyle(hwnd, Win32.WS_EX_TOOLWINDOW, true);
            Win32.ToggleExStyle(hwnd, Win32.WS_EX_NOACTIVATE, true);
        }

        SetClickThrough(true);
        _animation.Start();
        _hover.Start();
        _visual.InvalidateVisual();
    }

    // MARK: - Состояние

    public void SetState(PillState state)
    {
        if (State == state) return;
        State = state;
        _visual.State = state;

        switch (state)
        {
            case PillState.Recording:
                _elapsedMs = 0;
                _visual.Timer = TimeString;
                _recordingClock.Start();
                _successHold.Stop();
                break;
            case PillState.Processing:
                _recordingClock.Stop();
                break;
            case PillState.Success:
                _recordingClock.Stop();
                _successHold.Stop();
                _successHold.Start();
                break;
            default:
                _recordingClock.Stop();
                _successHold.Stop();
                break;
        }

        PollHover();
        _visual.InvalidateVisual();
        PillStateChanged?.Invoke(state);
    }

    /// <summary>
    /// Отдать своё дело панели Popup — или забрать обратно.
    ///
    /// Окно не прячется и не закрывается: на нём висят глобальные клавиши.
    /// Нулевая прозрачность плюс принудительный сквозной клик делают его
    /// невидимым и неощутимым, а хэндл остаётся ровно там, где его ждут
    /// сочетания. Заодно останавливается анимация: перерисовывать волну,
    /// которую никто не видит, шестьдесят раз в секунду — чистый нагрев.
    /// </summary>
    public void SetRemote(bool remote)
    {
        if (IsRemote == remote) return;
        IsRemote = remote;

        if (remote)
        {
            Opacity = 0;
            _animation.Stop();
            _hover.Stop();
            _hovered = false;
            _visual.Hovered = false;
            SetClickThrough(true);
        }
        else
        {
            Opacity = 1;
            _animation.Start();
            _hover.Start();
            PollHover();
            _visual.InvalidateVisual();
        }
    }

    // MARK: - Анимация

    private void Advance()
    {
        var repaint = false;

        var (targetWidth, targetHeight) = TargetSize();
        if (Math.Abs(_visual.PillWidth - targetWidth) > 0.5 ||
            Math.Abs(_visual.PillHeight - targetHeight) > 0.5)
        {
            _visual.PillWidth += (targetWidth - _visual.PillWidth) * Ease;
            _visual.PillHeight += (targetHeight - _visual.PillHeight) * Ease;
            if (Math.Abs(_visual.PillWidth - targetWidth) <= 0.5) _visual.PillWidth = targetWidth;
            if (Math.Abs(_visual.PillHeight - targetHeight) <= 0.5) _visual.PillHeight = targetHeight;
            repaint = true;
        }

        // Шаг фазы подобран под шестнадцать миллисекунд кадра: волна бежит с той
        // же видимой скоростью, что и раньше.
        if (State == PillState.Recording)
        {
            _visual.Phase += 0.10;
            repaint = true;
        }
        else if (State == PillState.Processing)
        {
            _visual.Phase += 0.04;
            repaint = true;
        }

        if (repaint) _visual.InvalidateVisual();
    }

    private (double Width, double Height) TargetSize() => State switch
    {
        PillState.Recording or PillState.Processing or PillState.Success => (RecordWidth, RecordHeight),
        _ when _hovered => (HoverWidth, HoverHeight),
        _ => (IdleWidth, IdleHeight),
    };

    // MARK: - Наведение и клики

    private void PollHover()
    {
        // Пилюля отдала своё дело панели: внизу нечего наводить, а невидимое
        // окно, забирающее клики, съедало бы их у того, что человек видит.
        if (IsRemote) return;

        var cursor = ToLocal(Win32.CursorPosition());
        var pill = PillRect();
        pill.Inflate(8, 8);
        var inside = pill.Contains(cursor);

        if (inside != _hovered)
        {
            _hovered = inside;
            _visual.Hovered = inside;
            _visual.InvalidateVisual();
        }

        _visual.PointerOverButton = inside && ButtonRect().Contains(cursor);

        // Прозрачные поля окна всегда остаются сквозными: клик мимо пилюли
        // должен попадать в то, что под ней.
        if (!_dragging) SetClickThrough(!inside);
    }

    private Point ToLocal(Point screen)
    {
        try
        {
            return PointFromScreen(screen);
        }
        catch (InvalidOperationException)
        {
            // Окно ещё не показано — координат нет и быть не может.
            return new Point(-1000, -1000);
        }
    }

    private Rect PillRect()
    {
        var cx = WindowWidth / 2;
        var cy = WindowHeight / 2;
        return new Rect(
            cx - _visual.PillWidth / 2, cy - _visual.PillHeight / 2,
            _visual.PillWidth, _visual.PillHeight);
    }

    private Rect ButtonRect() => PillVisual.ButtonRectIn(PillRect());

    private void SetClickThrough(bool enabled)
    {
        if (_clickThrough == enabled) return;
        var hwnd = Win32.HandleOf(this);
        if (hwnd == IntPtr.Zero) return;
        _clickThrough = enabled;
        Win32.ToggleExStyle(hwnd, Win32.WS_EX_TRANSPARENT, enabled);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var point = e.GetPosition(this);
        var overButton = ButtonRect().Contains(point);

        if (overButton)
        {
            switch (State)
            {
                case PillState.Recording:
                    StopRequested?.Invoke();
                    e.Handled = true;
                    return;
                case PillState.Idle when _hovered:
                    StartRequested?.Invoke();
                    e.Handled = true;
                    return;
            }
        }

        // Всё остальное тело — ручка для перетаскивания.
        // Смещение курсора внутри окна. Пока окно едет за мышью, оно остаётся
        // тем же — по этому и считается, куда двигать.
        _dragging = true;
        _moved = false;
        _dragOffset = new Vector(point.X, point.Y);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var point = e.GetPosition(this);
        var dx = point.X - _dragOffset.X;
        var dy = point.Y - _dragOffset.Y;
        if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) return;

        Left += dx;
        Top += dy;
        _moved = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();
        if (_moved) Moved?.Invoke(Left, Top);
        PollHover();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Хэндл появился — самое раннее место, где к нему можно обращаться.
        var source = (HwndSource?)PresentationSource.FromVisual(this);
        if (source is null) return;
        Win32.ToggleExStyle(source.Handle, Win32.WS_EX_TOOLWINDOW, true);
        Win32.ToggleExStyle(source.Handle, Win32.WS_EX_NOACTIVATE, true);
    }
}

/// <summary>
/// Собственно рисунок пилюли. Отдельный элемент, потому что рисовать надо
/// в <c>OnRender</c>, а окно с прозрачностью рисует свой фон само.
/// </summary>
public sealed class PillVisual : FrameworkElement
{
    public PillState State { get; set; } = PillState.Idle;
    public bool Hovered { get; set; }
    public bool PointerOverButton { get; set; }
    public double Phase { get; set; }
    public string Timer { get; set; } = "0:00";

    public double PillWidth { get; set; } = 74;
    public double PillHeight { get; set; } = 16;

    private static readonly Brush IdleFill = Freeze(Color.FromArgb(200, 30, 30, 30));
    private static readonly Brush HoverFill = Freeze(Color.FromArgb(235, 28, 28, 28));
    private static readonly Brush BusyFill = Freeze(Color.FromArgb(238, 22, 22, 22));
    private static readonly Brush SuccessFill = Freeze(Color.FromArgb(235, 52, 199, 89));

    private static readonly Brush IdleDots = Freeze(Color.FromRgb(120, 120, 120));
    private static readonly Brush BodyDots = Freeze(Color.FromRgb(128, 128, 128));
    private static readonly Brush White = Freeze(Colors.White);
    private static readonly Brush AlmostWhite = Freeze(Color.FromArgb(235, 255, 255, 255));
    private static readonly Brush RecordRed = Freeze(Color.FromRgb(214, 52, 52));
    private static readonly Brush RecordRedHot = Freeze(Color.FromRgb(235, 64, 64));
    private static readonly Brush StopRed = Freeze(Color.FromRgb(204, 34, 34));
    private static readonly Brush StopRedHot = Freeze(Color.FromRgb(238, 51, 51));
    private static readonly Brush Grey = Freeze(Color.FromRgb(100, 100, 100));
    private static readonly Brush LightGrey = Freeze(Color.FromRgb(230, 230, 230));
    private static readonly Brush DimGrey = Freeze(Color.FromRgb(200, 200, 200));

    private static readonly Pen CheckPen = FreezePen(White, 3);

    private static readonly Typeface Mono = new(
        new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FreezePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    /// <summary>Круглая кнопка слева. Общая с окном: по ней же идёт попадание мышью.</summary>
    public static Rect ButtonRectIn(Rect pill)
    {
        const double padding = 4;
        var diameter = Math.Max(0, pill.Height - padding * 2);
        return new Rect(pill.Left + padding, pill.Top + padding, diameter, diameter);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth > 0 ? ActualWidth : 240;
        var height = ActualHeight > 0 ? ActualHeight : 72;

        var pill = new Rect(
            width / 2 - PillWidth / 2, height / 2 - PillHeight / 2, PillWidth, PillHeight);
        var radius = pill.Height / 2;

        var background = State switch
        {
            PillState.Success => SuccessFill,
            PillState.Recording or PillState.Processing => BusyFill,
            _ when Hovered => HoverFill,
            _ => IdleFill,
        };
        dc.DrawRoundedRectangle(background, null, pill, radius, radius);

        switch (State)
        {
            case PillState.Idle when Hovered:
                DrawIdleHover(dc, pill);
                break;
            case PillState.Idle:
                DrawIdleMinimal(dc, pill);
                break;
            case PillState.Recording:
                DrawRecording(dc, pill);
                break;
            case PillState.Processing:
                DrawProcessing(dc, pill);
                break;
            case PillState.Success:
                DrawSuccess(dc, pill);
                break;
        }
    }

    /// <summary>Три точки в покое — минимум, по которому пилюля всё ещё узнаётся.</summary>
    private static void DrawIdleMinimal(DrawingContext dc, Rect pill)
    {
        const int count = 3;
        const double size = 4, spacing = 6;
        var total = count * size + (count - 1) * spacing;
        var x = pill.Left + pill.Width / 2 - total / 2 + size / 2;
        var y = pill.Top + pill.Height / 2;

        for (var i = 0; i < count; i++)
            dc.DrawEllipse(IdleDots, null, new Point(x + i * (size + spacing), y), size / 2, size / 2);
    }

    /// <summary>Наведение: красная кнопка записи слева, тело справа — за него тянут.</summary>
    private void DrawIdleHover(DrawingContext dc, Rect pill)
    {
        var button = ButtonRectIn(pill);
        dc.DrawEllipse(PointerOverButton ? RecordRedHot : RecordRed, null,
            Center(button), button.Width / 2, button.Width / 2);

        var inner = button.Width * 0.30 / 2;
        dc.DrawEllipse(AlmostWhite, null, Center(button), inner, inner);

        DrawBodyDots(dc, pill, button);
    }

    private static void DrawBodyDots(DrawingContext dc, Rect pill, Rect button)
    {
        const int count = 4;
        const double size = 5;
        var left = button.Right + 12;
        var right = pill.Right - 14;
        var step = right > left ? (right - left) / (count - 1) : 0;
        var y = pill.Top + pill.Height / 2;

        for (var i = 0; i < count; i++)
            dc.DrawEllipse(BodyDots, null, new Point(left + i * step, y), size / 2, size / 2);
    }

    /// <summary>Запись: кнопка «стоп», бегущая волна, моноширинный таймер.</summary>
    private void DrawRecording(DrawingContext dc, Rect pill)
    {
        var button = ButtonRectIn(pill);
        dc.DrawEllipse(PointerOverButton ? StopRedHot : StopRed, null,
            Center(button), button.Width / 2, button.Width / 2);
        DrawStopSquare(dc, button, White);
        DrawTimer(dc, pill, White);

        var (left, right) = WaveBounds(pill, button);
        const int count = 8;
        var spacing = right > left ? (right - left) / (count - 1) : 0;
        var y = pill.Top + pill.Height / 2;

        for (var i = 0; i < count; i++)
        {
            var angle = Phase + i * 0.6;
            var size = 4 + 6 * Math.Pow(Math.Sin(angle), 2);
            dc.DrawEllipse(White, null, new Point(left + i * spacing, y), size / 2, size / 2);
        }
    }

    /// <summary>Обработка: та же картинка, но без цвета и без бега — работа идёт без нас.</summary>
    private void DrawProcessing(DrawingContext dc, Rect pill)
    {
        var button = ButtonRectIn(pill);
        dc.DrawEllipse(Grey, null, Center(button), button.Width / 2, button.Width / 2);
        DrawStopSquare(dc, button, LightGrey);
        DrawTimer(dc, pill, DimGrey);

        var (left, right) = WaveBounds(pill, button);
        const int count = 8;
        var spacing = right > left ? (right - left) / (count - 1) : 0;
        var y = pill.Top + pill.Height / 2;
        var size = 4 + 5 * Math.Pow(Math.Sin(Phase), 2);

        for (var i = 0; i < count; i++)
            dc.DrawEllipse(DimGrey, null, new Point(left + i * spacing, y), size / 2, size / 2);
    }

    private static void DrawSuccess(DrawingContext dc, Rect pill)
    {
        var cx = pill.Left + pill.Width / 2;
        var cy = pill.Top + pill.Height / 2;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(cx - 9, cy), isFilled: false, isClosed: false);
            ctx.LineTo(new Point(cx - 2, cy + 7), true, false);
            ctx.LineTo(new Point(cx + 11, cy - 7), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, CheckPen, geometry);
    }

    private static (double Left, double Right) WaveBounds(Rect pill, Rect button) =>
        (button.Right + 12, pill.Right - 60);

    private static void DrawStopSquare(DrawingContext dc, Rect button, Brush brush)
    {
        var side = button.Width * 0.34;
        var center = Center(button);
        var square = new Rect(center.X - side / 2, center.Y - side / 2, side, side);
        dc.DrawRoundedRectangle(brush, null, square, 2, 2);
    }

    private void DrawTimer(DrawingContext dc, Rect pill, Brush brush)
    {
        var text = new FormattedText(
            Timer,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Mono,
            15,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var x = pill.Right - 8 - text.Width;
        var y = pill.Top + (pill.Height - text.Height) / 2;
        dc.DrawText(text, new Point(x, y));
    }

    private static Point Center(Rect rect) => new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
}
