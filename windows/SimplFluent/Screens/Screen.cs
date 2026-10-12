using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Simpl.Kit;

namespace Simpl.Screens;

/// <summary>
/// A screen of the app (a place in the navigation pane, or one pushed on it): its title for the page header and the
/// breadcrumb, the commands beside the title, and its content — built in code from what it has read, and built again as
/// answers come. The first build enters as Windows 11 pages do (up from a little below); later ones simply replace it.
/// An answer that says what the last one did builds nothing; answers coming close together build once; a screen out of
/// view is built when it is shown again.
/// </summary>
public abstract class Screen
{
    /// <summary>Answers coming within this many milliseconds of a build are built once, together, this long after it.</summary>
    private const int Quiet = 150;

    protected readonly Engine.Engine engine;
    private readonly ContentControl host = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    /// <summary>Told each time the screen is built: the loaders it watches (one still reading then shows its answer, whatever it says).</summary>
    private readonly List<Action> drawn = new();
    private bool built, pending, gone, stale;
    private double width;
    /// <summary>When it was last built (Environment.TickCount64), and the build waiting for the answers coming close behind.</summary>
    private long builtAt;
    private DispatcherTimer? soon;

    protected Screen(Engine.Engine engine) => this.engine = engine;

    /// <summary>The title in the page header (and the breadcrumb's last part).</summary>
    public abstract string Title { get; }

    /// <summary>A line under the title (a date, a course's term), or null.</summary>
    public virtual string? Subtitle => null;

    /// <summary>Breadcrumb parts of the screen's own before its title (the Tools page's "Tools" before a tool open on it).</summary>
    public virtual IReadOnlyList<(string Title, Action Go)> Before => Array.Empty<(string, Action)>();

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

    /// <summary>Shown (first, or again after Back): read what it shows, and draw — again if answers came while it was away.</summary>
    public void Show()
    {
        gone = false;
        Refresh();
        if (!built || stale) Render();
    }

    /// <summary>Gone from view (another screen over it, or another place).</summary>
    public virtual void Hide() => gone = true;

    /// <summary>What it shows may have changed (the engine's DataVersion): read again.</summary>
    public abstract void Refresh();

    /// <summary>A press anywhere in the window (the pane, the header, the page, the title bar), before it does what it
    /// does: a panel the screen has open over its page folds back when the press is outside it.</summary>
    public virtual void Pressed(System.Windows.Input.MouseButtonEventArgs e) { }

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

    /// <summary>Built again, once, after whatever else is happening now (several answers arriving together build once):
    /// for something done on the screen (a tick, a choice), with no wait for answers coming close behind.</summary>
    public void Render()
    {
        StopSoon();
        if (pending) return;
        pending = true;
        host.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            pending = false;
            stale = false;
            var first = !built;
            built = true;
            FrameworkElement content;
            var perfAt = Support.Perf.Stamp();
            try { content = Build(); }
            catch (Exception e)
            {
                Support.Log.Write($"{GetType().Name}: {e}");
                content = K.LoadState(e.Message, Render);
            }
            host.Content = content;
            Support.Perf.Built(GetType().Name, perfAt, first);
            if (first && !gone) Motion.Enter(content, 0, 28, 420);
            Support.Shot.Moved();
            builtAt = Environment.TickCount64;
            foreach (var d in drawn) d();
        });
    }

    /// <summary>
    /// Built again for an answer that came: as <see cref="Render"/> builds when it has not been built for a moment, else
    /// once, a moment after the last build, for every answer come by then. Out of view, it is built when shown again.
    /// </summary>
    protected void RenderSoon()
    {
        if (pending || soon is { IsEnabled: true }) return;
        if (gone) { stale = true; return; }
        var wait = Quiet - (Environment.TickCount64 - builtAt);
        if (!built || wait <= 0) { Render(); return; }
        if (soon == null)
        {
            soon = new DispatcherTimer(DispatcherPriority.Background, host.Dispatcher);
            soon.Tick += (_, _) =>
            {
                StopSoon();
                if (gone) stale = true;
                else Render();
            };
        }
        soon.Interval = TimeSpan.FromMilliseconds(wait);
        soon.Start();
        Support.Shot.Began(); // (the screenshot suite's picture waits for this build as for an answer)
    }

    private void StopSoon()
    {
        if (soon is not { IsEnabled: true }) return;
        soon.Stop();
        Support.Shot.Ended();
    }

    protected void HeaderChange() => HeaderChanged?.Invoke();

    /// <summary>A loader whose answers build the screen again (soon after, several close together once; an answer that
    /// says what the last one did, not at all).</summary>
    protected Loader<T> Watch<T>(Loader<T> l) where T : class
    {
        l.Changed += () => { if (!l.LastSame) RenderSoon(); };
        drawn.Add(l.Drawn);
        return l;
    }

    /// <summary>Told each time the screen is built, as the loaders it watches are (a part's own).</summary>
    private protected void WhenBuilt(Action a) => drawn.Add(a);
}
