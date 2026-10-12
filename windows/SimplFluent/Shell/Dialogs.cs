using System.Windows;
using Simpl.Kit;

namespace Simpl.Shell;

/// <summary>A window that shows sheets over itself (the main window, Settings).</summary>
public interface ISheetWindow
{
    SheetHost Sheets { get; }
}

/// <summary>
/// The app's questions and failures (the Mac's alerts), as Windows 11's dialogs over the window they belong to — the
/// window dimmed behind them.
/// </summary>
public static class Dialogs
{
    private static SheetHost? HostOf(Window? w)
    {
        w ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) ?? Application.Current?.MainWindow;
        return (w as ISheetWindow)?.Sheets ?? (Application.Current?.MainWindow as ISheetWindow)?.Sheets;
    }

    /// <summary>A sheet over a window (the active one when none is named).</summary>
    public static void Show(Sheet s, Window? over = null)
    {
        if (HostOf(over) is { } h) h.Show(s);
    }

    /// <summary>A question (the page's, the app's): true for OK.</summary>
    public static Task<bool> Ask(Window? owner, string title, string note, string ok, string? cancel, bool danger = false)
    {
        var done = new TaskCompletionSource<bool>();
        var s = new Sheet { Title = title, Body = K.Wrapped(note, Ty.Body, B.Text) };
        if (string.IsNullOrEmpty(note)) s.Body = new System.Windows.Controls.Grid();
        s.Add(ok, SheetButtonKind.Primary, () => done.TrySetResult(true));
        if (cancel != null) s.Add(cancel, SheetButtonKind.Close, () => done.TrySetResult(false));
        s.Closed += () => done.TrySetResult(false);
        if (HostOf(owner) is { } h) h.Show(s); else done.TrySetResult(false);
        return done.Task;
    }

    /// <summary>Something to confirm before it is done (Sign Out…, Erase Everything): its own words on the button.</summary>
    public static void Confirm(Window? owner, string title, string message, string go, Action onGo, string cancel = "Cancel")
    {
        var s = new Sheet { Title = title, Body = K.Wrapped(message, Ty.Body, B.Text) };
        s.Add(go, SheetButtonKind.Primary, onGo);
        s.Add(cancel, SheetButtonKind.Close);
        if (HostOf(owner) is { } h) h.Show(s);
    }

    /// <summary>Something that could not be done, said.</summary>
    public static void Fail(string title, string message, Window? owner = null)
    {
        var s = new Sheet { Title = title, Body = K.Wrapped(message, Ty.Body, B.Text) };
        s.Add("OK", SheetButtonKind.Primary);
        if (HostOf(owner) is { } h) h.Show(s);
        else MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
