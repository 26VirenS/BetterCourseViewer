using Simpl.Models;
using Button = System.Windows.Controls.Button;

namespace Simpl.Screens;

/// <summary>
/// A row's quick look (the Mac's work preview): a click on a row grows the app's quick-look card out of it
/// (<see cref="WorkPreview"/>, as the Dashboard's rows have it) — what the work is, when it is due, its points, the first
/// words of what it says, with Mark done, its own action and Open; a double-click (or a Ctrl-click) opens the work whole.
/// </summary>
public sealed partial class TodoScreen
{
    /// <summary>The rows as drawn, by id: where each one's quick look grows from.</summary>
    private readonly Dictionary<string, Button> rowButtons = new();

    private void Press(WorkRow row)
    {
        if (!rowButtons.TryGetValue(row.Id, out var from)) return;
        WorkPreview.Press(WorkPreview.Work(row, engine, on => Toggle(row, on)), from);
    }
}
