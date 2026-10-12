using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Simpl.Kit;

namespace Simpl.Screens;

/// <summary>
/// A screen of the app (a place in the navigation pane, or one pushed on it): its title for the page header and the
/// breadcrumb, the commands beside the title, and its content — built in code from what it has read, and built again as
/// answers come. The first build enters as Windows 11 pages do (up from a little below); later ones simply replace it.
/// </summary>
public abstract class Screen
{
    protected readonly Engine.Engine engine;
    private readonly ContentControl host = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private bool built, pending, gone;
    private double width;

    protected Screen(Engine.Engine engine) => this.engine = engine;

    /// <summary>The title in the page header (and the breadcrumb's last part).</summary>
    public abstract string Title { get; }

    /// <summary>A line under the title (a date, a course's term), or null.</summary>
    public virtual string? Subtitle => null;

    /// <summary>The commands beside the title (a view picker, New Task), or null.</summary>
    public virtual FrameworkElement? Commands => null;

    /// <summary>The page scrolls as one (most do); a screen with its own scrolling parts says false.</summary>
    public virtual bool Scrolls => true;

    /// <summary>The widest the content is laid out (a reading width), or null for the whole page.</summary>
    public virtual double? MaxContentWidth => null;

    /// <summary>The header changed (its title, its commands): the shell draws it again.</summary>
    public event Action? HeaderChanged;

    /// <summary>The screen's content, for the shell to place.</summary>
    public FrameworkElement View => host;

    /// <summary>The width the page lays the content out in.</summary>
    public double Width => width;

    /// <summary>Shown (first, or again after Back): read what it shows, and draw.</summary>
    public void Show()
    {
        gone = false;
        Refresh();
        if (!built) Render();
    }

    /// <summary>Gone from view (another screen over it, or another place).</summary>
    public virtual void Hide() => gone = true;

    /// <summary>What it shows may have changed (the engine's DataVersion): read again.</summary>
    public abstract void Refresh();

    /// <summary>Escape pressed with nothing else to take it: true if the screen used it (a panel folded back).</summary>
    public virtual bool Escape() => false;

    /// <summary>The content for what is read now.</summary>
    protected abstract FrameworkElement Build();

    /// <summary>The page's width changed: a screen whose layout follows the width says whether it must be built again.</summary>
    protected virtual bool LayoutChanges(double before, double now) => false;

    public void SetWidth(double w)
    {
        if (Math.Abs(w - width) < 0.5) return;
        var before = width;
        width = w;
        if (built && LayoutChanges(before, w)) Render();
    }

    /// <summary>Built again, once, after whatever else is happening now (several answers arriving together build once).</summary>
    public void Render()
    {
        if (pending) return;
        pending = true;
        host.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            pending = false;
            var first = !built;
            built = true;
            FrameworkElement content;
            try { content = Build(); }
            catch (Exception e)
            {
                Support.Log.Write($"{GetType().Name}: {e}");
                content = K.LoadState(e.Message, Render);
            }
            host.Content = content;
            if (first && !gone) Motion.Enter(content, 0, 28, 420);
            Support.Shot.Moved();
        });
    }

    protected void HeaderChange() => HeaderChanged?.Invoke();

    /// <summary>A loader whose answers build the screen again.</summary>
    protected Loader<T> Watch<T>(Loader<T> l) where T : class
    {
        l.Changed += Render;
        return l;
    }
}
