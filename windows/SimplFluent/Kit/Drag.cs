using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Hyperlink = System.Windows.Documents.Hyperlink;

namespace Simpl.Kit;

/// <summary>
/// What moves a popup by its heading: a press there that is not on a control (a button, a field, a list, a slider, a
/// link, a page) takes hold of it. The popups floating in windows of their own (Shell/FloatPopup.cs) and the quick look
/// floating on the page, which grows out of its row, share it. (A Dashboard counter's panel is not moved: it grows in
/// place out of its card and folds back into it.)
/// </summary>
public static class Drag
{
    /// <summary>Whether a press is on something that takes it itself.</summary>
    public static bool OnControl(DependencyObject d)
    {
        for (DependencyObject? p = d; p != null && p is not Window; p = Parent(p))
        {
            if (p is ButtonBase or TextBoxBase or System.Windows.Controls.PasswordBox or Selector or Thumb or RangeBase or Hyperlink or HwndHost) return true;
            if (p is FrameworkElement { Cursor: { } c } && c == Cursors.Hand) return true;
        }
        return false;
    }

    /// <summary>Whether <paramref name="d"/> is <paramref name="root"/> or inside it.</summary>
    public static bool Within(DependencyObject d, DependencyObject root)
    {
        for (DependencyObject? p = d; p != null; p = Parent(p))
            if (ReferenceEquals(p, root)) return true;
        return false;
    }

    private static DependencyObject? Parent(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    /// <summary>A card on a canvas moved by its top <paramref name="band"/> (its heading), as below.</summary>
    public static void ByHeading(FrameworkElement card, double band, Action? moved = null, Action? clicked = null) =>
        ByHeading(card, () => band, moved, clicked);

    /// <summary>
    /// A card on a canvas moved by its heading, the top <paramref name="band"/> of it as it is at the press (a heading that
    /// grows with what it says): it takes hold of the card only once the pointer has gone Windows' drag distance from the
    /// press, then follows it until it is let go, its heading kept inside the canvas. <paramref name="moved"/> is told once
    /// it has been moved; a press let go before that is a click, not a move — <paramref name="clicked"/> is told, and the
    /// card has not been touched (a card still growing out of its row goes on growing).
    /// </summary>
    public static void ByHeading(FrameworkElement card, Func<double> band, Action? moved = null, Action? clicked = null)
    {
        Point? from = null;
        var shifted = false;
        var at = new Point();
        card.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (card.Parent is not Canvas canvas || e.ClickCount > 1 || e.OriginalSource is not DependencyObject source) return;
            if (e.GetPosition(card).Y > band() || OnControl(source)) return;
            from = e.GetPosition(canvas);
            shifted = false;
            card.CaptureMouse();
            e.Handled = true;
        };
        card.PreviewMouseMove += (_, e) =>
        {
            if (from is not { } f || card.Parent is not Canvas canvas || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(canvas);
            e.Handled = true;
            if (!shifted)
            {
                if (Math.Abs(p.X - f.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - f.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                shifted = true;
                // (where it is now, whatever was moving it: its growing out of its row stopped where it has got to)
                at = new Point(Canvas.GetLeft(card), Canvas.GetTop(card));
                card.BeginAnimation(Canvas.LeftProperty, null);
                card.BeginAnimation(Canvas.TopProperty, null);
                Canvas.SetLeft(card, at.X);
                Canvas.SetTop(card, at.Y);
            }
            const double keep = 80;
            var x = Math.Clamp(at.X + p.X - f.X, keep - card.ActualWidth, Math.Max(keep - card.ActualWidth, canvas.ActualWidth - keep));
            var y = Math.Clamp(at.Y + p.Y - f.Y, 0, Math.Max(0, canvas.ActualHeight - band()));
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);
        };
        // (let go: a move if it went the drag distance, else a click — unless the hold was lost some other way, to another
        //  window)
        void Let(bool click)
        {
            if (from == null) return;
            var was = shifted;
            from = null;
            shifted = false;
            if (card.IsMouseCaptured) card.ReleaseMouseCapture();
            if (was) moved?.Invoke();
            else if (click) clicked?.Invoke();
        }
        card.PreviewMouseLeftButtonUp += (_, e) => { if (from != null) { Let(click: true); e.Handled = true; } };
        card.LostMouseCapture += (_, _) => Let(click: false);
    }
}
