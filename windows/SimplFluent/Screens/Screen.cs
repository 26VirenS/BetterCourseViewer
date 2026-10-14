using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Simpl.Kit;

namespace Simpl.Screens;

/// <summary>
/// A screen of the app (a place in the navigation pane, or one pushed on it): its title for the page header and the
/// breadcrumb, the commands beside the title, and its content — built in code from what it has read, and built again as
/// answers come. The shell builds it at once as it shows it (from the answers kept, when there are some) and brings the
/// page in itself; later builds simply replace the content in place. An answer that says what the last one did builds
/// nothing; answers coming close together build once; a screen out of view is built when it is shown again.
/// </summary>
public abstract class Screen
{
    /// <summary>Answers coming within this many milliseconds of a build are built once, together, this long after it.</summary>
    private const int Quiet = 150;

    protected readonly Engine.Engine engine;
    private readonly ContentControl host = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    /// <summary>Told each time the screen is built: the loaders it watches.</summary>
    private readonly List<Action> drawn = new();
    /// <summary>The loaders it watches: whether one is still reading, for the shell's prefetch.</summary>
    private readonly List<Func<bool>> reading = new();
    private bool built, pending, gone = true, stale;
    private double width, window;
    /// <summary>When it was last built (Environment.TickCount64), and the build waiting for the answers coming close behind.</summary>
    private long builtAt;
    private DispatcherTimer? soon;
    /// <summary>Which build a posted one is: a build done at once meanwhile makes it unneeded.</summary>
    private int ticket;

    protected Screen(Engine.Engine engine) => this.engine = engine;

    /// <summary>The title in the page header (and the breadcrumb's last part).</summary>
    public abstract string Title { get; }

    /// <summary>A line under the title (a date, a course's term), or null.</summary>
    public virtual string? Subtitle => null;

    /// <summary>How many lines the title may wrap to before it is cut (an assignment's long name: two), its whole words a
    /// tooltip once it is cut.</summary>
    public virtual int TitleLines => 1;

    /// <summary>Breadcrumb parts of the screen's own before its title (the Tools page's "Tools" before a tool open on it).</summary>
    public virtual IReadOnlyList<(string Title, Action Go)> Before => Array.Empty<(string, Action)>();

    /// <summary>The commands beside the title (a view picker, New Task), or null.</summary>
    public virtual FrameworkElement? Commands => null;

    /// <summary>The page scrolls as one (most do); a screen with its own scrolling parts says false.</summary>
    public virtual bool Scrolls => true;

    /// <summary>The widest the content is laid out (a reading width), or null for the whole page.</summary>
    public virtual double? MaxContentWidth => null;

    /// <summary>Something of the student's own is in it still (a reply being written): the shell keeps it, whatever else it lets go.</summary>
    public virtual bool Busy => false;

    /// <summary>The header changed (its title, its commands): the shell draws it again.</summary>
    public event Action? HeaderChanged;

    /// <summary>Built (its content replaced): the shell, waiting to bring the page in until there is something to show.</summary>
    public event Action? Built;

    /// <summary>The screen's content, for the shell to place.</summary>
    public FrameworkElement View => host;

    /// <summary>The width the page lays the content out in.</summary>
    public double Width => width;

    /// <summary>The window's width, the navigation pane's and the page's together (0 until the shell gives it): for a
    /// layout that follows the window rather than the page left beside the pane — the Dashboard's counters.</summary>
    public double WindowWidth => window;

    /// <summary>Built at least once, and not waiting to be built again.</summary>
    public bool Ready => built && !stale && !pending;

    /// <summary>What it shows is only the spinner it shows while nothing has been read yet (not why it could not be).</summary>
    public bool Placeholder { get; private set; }

    /// <summary>A loader it watches is still reading.</summary>
    public bool Reading => reading.Any(r => r());

    /// <summary>Not in view (not shown yet, another screen over it, or only read ahead).</summary>
    protected bool Away => gone;

    /// <summary>
    /// Shown (first, or again after Back): read what it shows, and draw — again if answers came while it was away. Asked
    /// <paramref name="now"/> (the shell, bringing it in), it is built before this returns, so the page comes in with
    /// what it shows; else after whatever else is happening.
    /// </summary>
    public void Show(bool now = false)
    {
        gone = false;
        Refresh();
        if (built && !stale && !pending) return;
        if (now) RenderNow(); else Render();
    }

    /// <summary>Gone from view (another screen over it, or another place).</summary>
    public virtual void Hide() => gone = true;

    /// <summary>
    /// Read what it shows without showing it (the shell's prefetch, a pointer resting on the way to it): answers that come
    /// build it when it is next shown.
    /// </summary>
    public void Prefetch()
    {
        if (!gone) return;
        ReadAhead();
    }

    /// <summary>What <see cref="Prefetch"/> reads: what <see cref="Refresh"/> does, unless refreshing does more than read.</summary>
    protected virtual void ReadAhead() => Refresh();

    /// <summary>Let go of for good (the shell keeps only so many): whatever it listens to outside itself, stopped.</summary>
    public virtual void Release() => StopSoon();

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

    /// <summary>The window's width changed: a screen whose layout follows the window says whether it must be built again.</summary>
    protected virtual bool WindowLayoutChanges(double before, double now) => false;

    /// <summary>The page's width <paramref name="w"/> and the window's <paramref name="win"/>, as the shell lays them out now.</summary>
    public void SetWidth(double w, double win)
    {
        if (Math.Abs(w - width) < 0.5 && Math.Abs(win - window) < 0.5) return;
        var (before, windowBefore) = (width, window);
        width = w;
        window = win;
        if (!built || !(LayoutChanges(before, w) || WindowLayoutChanges(windowBefore, win))) return;
        if (gone) stale = true; else Render();
    }

    /// <summary>The look changed (light, dark, the accent): built again — now if it shows, else when it is next shown.</summary>
    public void Restyle()
    {
        if (!built) return;
        if (gone) stale = true; else Render();
    }

    /// <summary>Built again, once, after whatever else is happening now (several answers arriving together build once):
    /// for something done on the screen (a tick, a choice), with no wait for answers coming close behind.</summary>
    public void Render()
    {
        StopSoon();
        // (out of view: built when it is shown again)
        if (gone) { stale = built; return; }
        if (pending) return;
        pending = true;
        var mine = ++ticket;
        Support.Perf.BuildDue(1);
        host.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            try
            {
                if (mine != ticket) return; // (built at once meanwhile)
                pending = false;
                Rebuild();
            }
            finally { Support.Perf.BuildDue(-1); }
        });
    }

    /// <summary>Built now (the shell showing it): whatever build was waiting is this one.</summary>
    public void RenderNow()
    {
        StopSoon();
        pending = false;
        ticket++;
        Rebuild();
    }

    private void Rebuild()
    {
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
        Placeholder = K.IsPlaceholder(content);
        if (!ReferenceEquals(host.Content, content)) host.Content = content;
        Support.Perf.Built(GetType().Name, perfAt, first);
        Support.Shot.Moved();
        builtAt = Environment.TickCount64;
        foreach (var d in drawn) d();
        Built?.Invoke();
    }

    /// <summary>
    /// Built again for an answer that came: as <see cref="Render"/> builds when it has not been built for a moment, else
    /// once, a moment after the last build, for every answer come by then. Out of view, it is built when shown again.
    /// </summary>
    protected void RenderSoon()
    {
        if (pending || soon is { IsEnabled: true }) return;
        if (gone) { stale = built; return; }
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
        Support.Perf.BuildDue(1);
    }

    private void StopSoon()
    {
        if (soon is not { IsEnabled: true }) return;
        soon.Stop();
        Support.Shot.Ended();
        Support.Perf.BuildDue(-1);
    }

    protected void HeaderChange() => HeaderChanged?.Invoke();

    /// <summary>A loader whose answers build the screen again (soon after, several close together once; an answer that
    /// says what the last one did, not at all).</summary>
    protected Loader<T> Watch<T>(Loader<T> l) where T : class
    {
        l.Changed += () => { if (!l.LastSame) RenderSoon(); };
        drawn.Add(l.Drawn);
        reading.Add(() => l.Loading);
        return l;
    }

    /// <summary>Told each time the screen is built, as the loaders it watches are (a part's own).</summary>
    private protected void WhenBuilt(Action a) => drawn.Add(a);

    /// <summary>A part's own loaders, for <see cref="Reading"/>.</summary>
    private protected void WhenReading(Func<bool> r) => reading.Add(r);
}
