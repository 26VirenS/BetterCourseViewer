using System.Windows;

namespace Simpl.Shell;

/// <summary>The app's questions and failures (the Mac's alerts): Windows' own message boxes, owned by the window.</summary>
public static class Dialogs
{
    /// <summary>A question the page asks (a destructive answer said as such): true for OK.</summary>
    public static bool Ask(Window? owner, string title, string note, string ok, string? cancel, bool danger)
    {
        var text = string.IsNullOrEmpty(note) ? title : $"{title}\n\n{note}";
        var buttons = cancel == null ? MessageBoxButton.OK : MessageBoxButton.OKCancel;
        var icon = danger ? MessageBoxImage.Warning : MessageBoxImage.Question;
        var r = owner != null ? MessageBox.Show(owner, text, "Simpl", buttons, icon) : MessageBox.Show(text, "Simpl", buttons, icon);
        return r == MessageBoxResult.OK;
    }

    /// <summary>Something that could not be done, said.</summary>
    public static void Fail(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        if (owner != null) MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        else MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
