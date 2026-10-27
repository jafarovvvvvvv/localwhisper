using System.Windows;
using LocalWhisper.Model;
using LocalWhisper.Services;

namespace LocalWhisper;

public partial class App : Application
{
    private Mutex? _instanceLock;
    private AppController? _controller;
    private EventWaitHandle? _showSignal;
    private volatile bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Двух LocalWhisper на одной машине быть не может: они дрались бы за
        // одни и те же горячие клавиши, за микрофон и за файл истории —
        // причём вторая копия проиграла бы молча.
        var background = e.Args.Contains("--background");
        _instanceLock = new Mutex(initiallyOwned: true, "LocalWhisper.SingleInstance", out var isFirst);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "LocalWhisper.ShowSettings");
        if (!isFirst)
        {
            // Повторный запуск не плодит копию, а просит первую показать окно (так Clicky открывает настройки).
            if (!background) _showSignal.Set();
            Shutdown();
            return;
        }

        AppPaths.EnsureCreated();
        Log.Info("app", "запуск");

        // Необработанное исключение в потоке интерфейса не должно уносить с
        // собой приложение, которое всё это время сидело в трее. В журнал —
        // обязательно: молча проглоченная ошибка хуже упавшего окна.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("app", "необработанное исключение", args.Exception);
            args.Handled = true;
        };

        _controller = new AppController();
        _controller.Start(showSettings: !background);

        var signal = _showSignal;
        new Thread(() =>
        {
            while (signal.WaitOne())
            {
                if (_exiting) return;
                Dispatcher.BeginInvoke(() => _controller?.ShowSettings());
            }
        }) { IsBackground = true, Name = "show-settings-signal" }.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("app", "выход");
        _exiting = true;
        try { _showSignal?.Set(); } catch { }
        _controller?.Dispose();
        _instanceLock?.Dispose();
        base.OnExit(e);
    }
}
