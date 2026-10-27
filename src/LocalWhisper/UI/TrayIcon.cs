using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LocalWhisper.Model;
using LocalWhisper.Services;
// Единственный файл, которому нужен WinForms, — и единственный, где имя
// Application приходится разводить руками.
using Application = System.Windows.Application;

namespace LocalWhisper.UI;

/// <summary>
/// Иконка в трее: то место, откуда приложение достают, когда его окно закрыто.
///
/// Закрытие окна не закрывает приложение — иначе диктовка по горячей клавише
/// работала бы только пока открыто окно, которое в работе не нужно вовсе.
/// Поэтому выход живёт здесь и только здесь.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AppController _app;

    public TrayIcon(AppController app)
    {
        _app = app;
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "LocalWhisper",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _app.ToggleSettings();
        };
        _icon.DoubleClick += (_, _) => _app.ShowSettings();
    }

    /// <summary>
    /// Иконка приложения, если она лежит рядом, и нарисованная — если нет.
    /// Собственный файл важнее: он уже узнаваем на панели задач и в трее.
    /// </summary>
    private static Icon LoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LocalWhisper.ico");
        if (File.Exists(path))
        {
            try
            {
                return new Icon(path);
            }
            catch (Exception e)
            {
                Log.Warn("tray", $"иконка не читается: {e.Message}");
            }
        }
        return DrawIcon();
    }

    /// <summary>Пилюля в миниатюре — на случай, если файла иконки рядом не оказалось.</summary>
    private static Icon DrawIcon()
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.White);
            g.FillEllipse(brush, 3, 11, 10, 10);
            g.FillEllipse(brush, 19, 11, 10, 10);
            g.FillRectangle(brush, 8, 11, 16, 10);
        }

        var handle = bitmap.GetHicon();
        try
        {
            // Клонируем: иконка, сделанная из HICON, владеет чужим хэндлом и
            // перестаёт быть годной, как только его освободят.
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };

        menu.Items.Add(new ToolStripMenuItem(Strings.L("Settings and dashboard"), null,
            (_, _) => _app.ShowSettings()));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Strings.L("Open data folder"), null,
            (_, _) => Shell.OpenFolder(AppPaths.Root)));
        menu.Items.Add(new ToolStripMenuItem(Strings.L("Settings file"), null,
            (_, _) =>
            {
                // Файл настроек открывают, чтобы его править. Показать вместо
                // него папку — значит попросить сделать ещё один клик там, где
                // его можно не делать.
                if (!File.Exists(AppPaths.Config)) _app.Config.Save();
                Shell.OpenFile(AppPaths.Config);
            }));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Strings.L("Quit"), null,
            (_, _) => Application.Current.Shutdown()));

        return menu;
    }

    public void Notify(string message)
    {
        try
        {
            _icon.ShowBalloonTip(4000, "LocalWhisper", message, ToolTipIcon.Info);
        }
        catch (Exception e)
        {
            Log.Warn("tray", $"уведомление не показалось: {e.Message}");
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);
    }
}
