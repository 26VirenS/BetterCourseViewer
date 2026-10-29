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

    /// <summary>The colour of the course (or group) the screen is of, for the square before its title that ties the page
    /// to its card and its square in the pane (the Mac's ScreenHeading color); null for a screen of no course.</summary>
    public virtual System.Windows.Media.Color? TitleColor => null;

    /// <summary>The page scrolls as one (most do); a screen with its own scrolling parts says false.</summary>
    public virtual bool Scrolls => true;

    /// <summary>(0.3.84) The page has no header of its own and is drawn edge to edge (the Dashboard's Silent mode).</summary>
    public virtual bool Bare => false;

    /// <summary>The widest the content is laid out (a reading width), or null for the whole page.</summary>
    public virtual double? MaxContentWidth => null;

    /// <summary>How wide the page's heading is, so its commands end where the content does (a page that keeps its content
    /// to a width of its own on a wide window: an assignment); null, the page's whole width.</summary>
    public virtual double? HeaderWidth => null;

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
        // (the screenshot suite's live runs: each build said, so its checks can tell that a check built nothing)
        if (Shell.LiveCheck.Says) Support.Log.Write($"live: {LiveName} built");
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
    /// says what the last one did, not at all; one a live check found new, as <see cref="LiveApplied"/> says). The live
    /// check reads it again while the screen shows.</summary>
    protected Loader<T> Watch<T>(Loader<T> l) where T : class
    {
        l.Changed += () => { if (!l.LastSame && !l.LastLive) RenderSoon(); };
        drawn.Add(l.Drawn);
        reading.Add(() => l.Loading);
        Checked(l, builds: true);
        return l;
    }

    // ---- the live check (Shell/LiveCheck.cs) ----

    /// <summary>What the live check reads again while the screen shows, and of those, what builds the screen again when
    /// it changes.</summary>
    private readonly List<Func<ILiveRead?>> live = new();
    private readonly HashSet<ILiveRead> builds = new();

    /// <summary>A read the live check asks again while the screen shows (a loader the screen follows by itself — the
    /// Inbox's — or a part's); <paramref name="builds"/>: a new answer builds the screen again.</summary>
    private protected void Checked<T>(Loader<T> l, bool builds = false) where T : class
    {
        l.Driven = () => LiveDriven;
        live.Add(() => l);
        if (builds) this.builds.Add(l);
    }

    /// <summary>A read the live check asks again while the screen shows, whichever it is at the time (the conversation
    /// open beside the Inbox's list), shown by its own view as it changes.</summary>
    private protected void Checked(Func<ILiveRead?> read) => live.Add(read);

    /// <summary>The window's live check looks after this screen (one of the main window's): what is only old is not read
    /// again as it is shown — the check, asked at once, reads it with the engine's maxAge and changes only what changed.</summary>
    public bool LiveDriven { get; set; }

    /// <summary>What it shows that a live check can read again now (each has something on screen, nothing on its way).</summary>
    public IReadOnlyList<ILiveRead> LiveReads => live.Select(f => f()).OfType<ILiveRead>().Where(r => r.CanCheck).ToList();

    /// <summary>A course's page (its home, a section, a page, an assignment, a discussion, its grades): checked every ten
    /// minutes, not every minute.</summary>
    public virtual bool CoursePage => false;

    /// <summary>The screen as the log names it ("dashboard", "todo"): never anything of the student's.</summary>
    public virtual string LiveName => GetType().Name.Replace("Screen", "").ToLowerInvariant();

    /// <summary>Why a check waits on this screen now — What-If on, a panel or a reply open on it — or null.</summary>
    public virtual string? LiveHold => Busy ? "something of yours in it" : null;

    /// <summary>
    /// A live check found new answers and they are now the loaders' (each told its watchers as an answer is, marked
    /// <see cref="Loader{T}.LastLive"/>): the screen shows them. By default it is built again, once, as for any answer
    /// (where the scroll stays); the Dashboard changes only the parts that changed, in place. What changed, for the log.
    /// </summary>
    public virtual string LiveApplied(IReadOnlyList<ILiveRead> changed)
    {
        if (changed.Any(builds.Contains))
        {
            KeepFocus();
            RenderSoon();
        }
        return string.Join(",", changed.Select(c => c.Call));
    }

    /// <summary>
    /// The keyboard's place kept through a build a live check asked for: the element that had it (a row, a button, a
    /// link) found again in the new build by its kind and its name — the same one of that name, counted from the top —
    /// and given the keyboard back once it is drawn, unless the student has put it somewhere else meanwhile. Built again
    /// whole (To Do, Notifications, Grades …), a screen threw the keyboard, and Narrator with it, out to the window.
    /// </summary>
    private void KeepFocus()
    {
        if (!host.IsKeyboardFocusWithin || System.Windows.Input.Keyboard.FocusedElement is not FrameworkElement had || FocusName(had) is not { } name) return;
        var nth = Focusables(host).Where(e => FocusName(e) == name).TakeWhile(e => !ReferenceEquals(e, had)).Count();
        // (only the build this asked for: one long after — the screen left meanwhile — takes nothing back)
        var until = Environment.TickCount64 + 3000;
        void Back()
        {
            Built -= Back;
            if (Environment.TickCount64 > until) return;
            host.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                // (moved by the student meanwhile — anywhere but the window itself, or still on what had it: left there)
                var now = System.Windows.Input.Keyboard.FocusedElement;
                if (now != null && now is not Window && !ReferenceEquals(now, had) && now is UIElement { IsVisible: true }) return;
                var same = Focusables(host).Where(e => FocusName(e) == name).ToList();
                if (same.Count > 0) same[Math.Min(nth, same.Count - 1)].Focus();
            });
        }
        Built += Back;
    }

    /// <summary>What an element that can have the keyboard is known by from one build to the next: its kind and the name
    /// Narrator reads (else its words, else its tooltip); null for one with none.</summary>
    private static string? FocusName(FrameworkElement e)
    {
        var name = System.Windows.Automation.AutomationProperties.GetName(e);
        if (string.IsNullOrEmpty(name)) name = (e as ContentControl)?.Content as string ?? e.ToolTip as string;
        return string.IsNullOrEmpty(name) ? null : $"{e.GetType().Name}:{name}";
    }

    /// <summary>The elements under <paramref name="root"/> that can have the keyboard now, top to bottom.</summary>
    private static IEnumerable<FrameworkElement> Focusables(DependencyObject root)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Focusable: true, IsVisible: true, IsEnabled: true } f) yield return f;
            if (child is UIElement { IsVisible: false }) continue;
            foreach (var inner in Focusables(child)) yield return inner;
        }
    }

    /// <summary>A part's own loaders, for the live check (a course part's), each building the screen again when it changes.</summary>
    private protected void CheckedPart(IEnumerable<ILiveRead> reads)
    {
        foreach (var r in reads)
        {
            live.Add(() => r);
            builds.Add(r);
        }
    }

    /// <summary>Told each time the screen is built, as the loaders it watches are (a part's own).</summary>
    private protected void WhenBuilt(Action a) => drawn.Add(a);

    /// <summary>A part's own loaders, for <see cref="Reading"/>.</summary>
    private protected void WhenReading(Func<bool> r) => reading.Add(r);
}
