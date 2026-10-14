using System.Windows;
using Simpl.Kit;
using Simpl.Tools;

namespace Simpl.Screens;

/// <summary>
/// A tool open on the Tools page (the Mac's ToolScreen): pushed on the Tools place, so the page's breadcrumb reads "Tools ›
/// Calculator", the breadcrumb's "Tools" and Back go to the tiles, and the pane and the title bar's pins stay at hand while
/// it is open — a course looked at beside it, the tool there again on coming back (ToolsCenter.Open). Its name and line
/// are the page's title and the line under it; a page of its own that it shows comes after its name ("Tools › Periodic
/// table › Lewis structures"); its own controls (a biology tool's pages, the Periodic Table's search) and its pin are the
/// page's commands. The tool is made once and kept with its page.
/// </summary>
public sealed class ToolScreen : Screen
{
    private readonly ToolsScreen.Kind kind;
    private readonly ToolContext context;
    private FrameworkElement? view;
    private FrameworkElement? commands;
    /// <summary>The page of the tool's own shown (null: its first), and the way back to its first.</summary>
    private string? page;
    private Action? pageBack;
    private bool note = true;

    public ToolScreen(Engine.Engine engine, string key) : base(engine)
    {
        kind = ToolsCenter.KindOf(key) ?? new ToolsScreen.Kind(key, key, "", Wpf.Ui.Controls.SymbolRegular.Toolbox24, "#8a8a8a");
        context = new ToolContext
        {
            Engine = engine, Key = key, InPage = true, Owner = Application.Current?.MainWindow,
            ShowPage = (p, back) =>
            {
                if (p == page && back == pageBack) return;
                page = p;
                pageBack = back;
                HeaderChange();
            },
            ShowNote = on =>
            {
                if (on == note) return;
                note = on;
                HeaderChange();
            },
        };
    }

    public string Key => kind.Key;

    public override string Title => page ?? kind.Name;

    /// <summary>The tool's line (none for the Periodic Table, nor while a page of its own or a studied set shows).</summary>
    public override string? Subtitle => page != null || !note || kind.Key == "ptable" || kind.Note.Length == 0 ? null : kind.Note;

    /// <summary>A page of the tool's own showing: the tool's name before it, going back to the tool's first page.</summary>
    public override IReadOnlyList<(string Title, Action Go)> Before =>
        page != null && pageBack != null ? new[] { (kind.Name, pageBack) } : Array.Empty<(string, Action)>();

    /// <summary>The tool's own controls and its pin, made once (the same commands each time keep the heading as drawn).</summary>
    public override FrameworkElement? Commands
    {
        get
        {
            if (commands != null) return commands;
            var row = K.H(8);
            if (ToolRegistry.Header(context) is { } extra) { extra.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(extra); }
            row.Children.Add(ToolHeader.PinButton(kind, squished: ToolHeader.Squished(kind.Key)));
            return commands = row;
        }
    }

    /// <summary>The tool scrolls its own parts (ToolKit.Page, the Periodic Table's pages).</summary>
    public override bool Scrolls => false;

    /// <summary>Shown: the tool the Tools place opens on while you are elsewhere; files or details handed to it while its
    /// page was kept are taken by the tool made again.</summary>
    public override void Refresh()
    {
        if (Away) return;
        ToolsCenter.Open = kind.Key;
        if (view != null && ToolsCenter.Handed(kind.Key))
        {
            view = null;
            Render();
        }
    }

    /// <summary>Nothing to read ahead: a tool reads only what it is handed.</summary>
    protected override void ReadAhead() { }

    protected override FrameworkElement Build()
    {
        if (view != null) return view;
        var tool = ToolRegistry.Full(context) ?? ToolPopup.Missing(kind);
        view = ToolKit.Edged(tool);
        return view;
    }
}
