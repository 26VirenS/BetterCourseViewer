using System.Windows;
using System.Windows.Threading;

namespace Simpl.Kit;

/// <summary>
/// Getting a screen ready before it is asked for: a pointer resting on something that opens a screen (a pane's row, a
/// course's card, a row of work) for a moment has that screen's answers read; pressed on it, the screen is built and
/// laid out too, unseen — so by the time the click is done (a press lasts a tenth of a second or so) the screen only has
/// to come in. Nothing shows, and a pointer passing over on its way elsewhere asks for nothing.
/// </summary>
public static class Ahead
{
    /// <summary>How long a pointer rests before what it rests on is read.</summary>
    private const int Dwell = 90;

    /// <summary><paramref name="prepare"/>(false) once a pointer has rested on the element a moment; (true) as it is pressed.</summary>
    public static T On<T>(T e, Action<bool> prepare) where T : UIElement
    {
        DispatcherTimer? dwell = null;
        e.MouseEnter += (_, _) =>
        {
            if (dwell == null)
            {
                dwell = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(Dwell) };
                dwell.Tick += (_, _) =>
                {
                    dwell.Stop();
                    if (e.IsMouseOver) prepare(false);
                };
            }
            dwell.Stop();
            dwell.Start();
        };
        e.MouseLeave += (_, _) => dwell?.Stop();
        e.PreviewMouseLeftButtonDown += (_, _) =>
        {
            dwell?.Stop();
            prepare(true);
        };
        return e;
    }
}
