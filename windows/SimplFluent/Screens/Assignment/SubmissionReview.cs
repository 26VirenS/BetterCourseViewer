using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// Your work and what was said about it, in a dialog over the assignment (the Mac's SubmissionReview): on the left the file
/// in the school's own viewer (DocViewer: the teacher's marks drawn on it, as Canvas shows them), one file at a time where
/// there are several; on the right the comments. A file the school has no viewer for is fetched and shown here; work with
/// no file at all shows the submission's own page; handed-in text shows as text. Escape or Done closes it.
/// </summary>
internal sealed class SubmissionReview : Grid
{
    private readonly Engine.Engine engine;
    private readonly AssignmentData a;
    private readonly List<Attachment> files;
    private Attachment? picked;
    private readonly Border viewer = new();
    private readonly ContentControl more = new() { Focusable = false };
    private readonly List<WebView2> owned = new();
    private string? folder;
    private int generation;
    public Action? Done;

    /// <summary>The popup opened on a file (the first where none is named), or on the work as a whole. Most of the window
    /// it opens over.</summary>
    public static void Show(Engine.Engine engine, AssignmentData a, Attachment? file)
    {
        var win = Application.Current?.MainWindow;
        var w = win is { ActualWidth: > 0 } ? win.ActualWidth : 1280;
        var h = win is { ActualHeight: > 0 } ? win.ActualHeight : 820;
        var width = Math.Min(Math.Max(w - 64, 760), 1500);
        var height = Math.Min(Math.Max(h - 80, 520), 1000);
        var view = new SubmissionReview(engine, a, file, width);
        var s = new Sheet { Bare = true, Width = width, Height = height, Body = view };
        view.Done = s.Close;
        view.Loaded += (_, _) =>
        {
            if (s.Card is { } card && s.Host is { ActualWidth: > 0 } host) card.MaxWidth = Math.Max(280, host.ActualWidth - 48);
        };
        s.Closed += view.Release;
        Dialogs.Show(s, win);
    }

    private SubmissionReview(Engine.Engine engine, AssignmentData a, Attachment? file, double width)
    {
        this.engine = engine;
        this.a = a;
        files = a.Submission?.Files ?? new List<Attachment>();
        picked = file ?? files.FirstOrDefault();
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(Top().Row(0));
        Children.Add(new Border().Bg(B.Divider).Row(1));
        var commentsWidth = Math.Min(360, Math.Max(280, width * 0.28));
        var body = K.Columns($"*, 1, {commentsWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            viewer.Col(0), new Border().Bg(B.Divider).Col(1), Comments().Col(2));
        Children.Add(body.Row(2));
        ShowWork();
    }

    // ---- the top ----

    private FrameworkElement Top()
    {
        var titles = K.V(1, K.Text("Feedback", Ty.BodyStrong), K.Text(a.Title, Ty.Body, B.Text2));
        titles.VerticalAlignment = VerticalAlignment.Center;
        var g = K.Columns("auto, *, auto, auto, auto",
            K.IconTile(SymbolRegular.Comment24, K.Hex(a.Color, Appearance.Accent), 30).Margin(0, 0, 12, 0).Col(0),
            titles.Col(1));
        if (files.Count > 1)
        {
            // which file to look at
            var pick = new ComboBox { MaxWidth = 260, MinWidth = 160, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Which file to look at" };
            foreach (var f in files) pick.Items.Add(f.Name);
            pick.SelectedIndex = Math.Max(0, picked == null ? 0 : files.IndexOf(picked));
            pick.SelectionChanged += (_, _) =>
            {
                if (pick.SelectedIndex < 0 || pick.SelectedIndex >= files.Count || files[pick.SelectedIndex] == picked) return;
                picked = files[pick.SelectedIndex];
                ShowWork();
            };
            g.Children.Add(pick.Margin(12, 0, 0, 0).Col(2));
        }
        g.Children.Add(more.Margin(8, 0, 0, 0).Col(3));
        var done = K.Button("Done", () => Done?.Invoke(), accent: true);
        done.ToolTip = "Close (Esc)";
        g.Children.Add(done.Margin(8, 0, 0, 0).Col(4));
        g.Margin = new Thickness(20, 14, 20, 14);
        return g;
    }

    /// <summary>The More menu for the file looked at (its app, Download, Copy Link).</summary>
    private void PaintMore()
    {
        if (picked is not { } f) { more.Content = null; return; }
        Button? b = null;
        b = K.IconButton(SymbolRegular.MoreHorizontal24, () =>
        {
            var m = AssignKit.FileMenu(engine, f.Url, f.Name);
            m.PlacementTarget = b;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            m.IsOpen = true;
        }, $"More for {f.Name}");
        more.Content = b;
    }

    // ---- the work ----

    private void ShowWork()
    {
        generation++;
        ReleaseViews();
        PaintMore();
        if (picked is { } f)
        {
            if (f.Preview is { } p && engine.Absolute(p) is { } u) viewer.Child = CanvasViewer(u);
            else FetchAndShow(f);
        }
        else if (a.Submission?.Viewer is { } v && engine.Absolute(v) is { } vu) viewer.Child = CanvasViewer(vu);
        else if (a.Submission?.Text is { Length: > 0 } text)
        {
            var words = AssignKit.Selectable(text, Ty.Body);
            words.MaxWidth = 760;
            words.HorizontalAlignment = HorizontalAlignment.Center;
            viewer.Child = new ScrollViewer { Content = words, Padding = new Thickness(28), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
        else viewer.Child = Unavailable(SymbolRegular.Document24, "Nothing to show", "This work has no file to look at.", null);
        Shot.Moved();
    }

    private static FrameworkElement Unavailable(SymbolRegular icon, string title, string note, Action? retry)
    {
        var v = K.V(10, K.Icon(icon, 40, B.Text3), K.Text(title, Ty.Subtitle).Center(), K.Wrapped(note, Ty.Body, B.Text2).Center());
        if (retry != null)
        {
            var again = K.Button("Try Again", retry);
            again.HorizontalAlignment = HorizontalAlignment.Center;
            v.Children.Add(again.Margin(0, 6, 0, 0));
        }
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.MaxWidth = 380;
        return new Grid { Children = { v } };
    }

    /// <summary>The school's own page for the work, with the session (its viewer, its marks), a thin bar while it loads.</summary>
    private FrameworkElement CanvasViewer(Uri url)
    {
        var bar = new ProgressBar { IsIndeterminate = true, Height = 2, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
        var web = AssignKit.Web(url, async core =>
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(AssignDocViewerSkin.Script());
            core.NewWindowRequested += (_, e) => { e.Handled = true; engine.GoTo(e.Uri, ""); };
            core.NavigationStarting += (_, _) => { bar.Visibility = Visibility.Visible; Shot.Moved(); };
            core.NavigationCompleted += (_, _) => { bar.Visibility = Visibility.Collapsed; Shot.Moved(); };
        });
        owned.Add(web);
        return new Grid { Children = { web, bar } };
    }

    /// <summary>A file the school has no viewer for, fetched with the session and shown here.</summary>
    private async void FetchAndShow(Attachment f)
    {
        var gen = generation;
        viewer.Child = K.V(12, K.Spinner(28), K.Text($"Loading {f.Name}…", Ty.Body, B.Text2).Center()).Align(HorizontalAlignment.Center, VerticalAlignment.Center);
        try
        {
            folder ??= Path.Combine(Path.GetTempPath(), $"SimplReview-{Guid.NewGuid()}");
            var path = await AssignKit.Fetch(engine, f.Url, f.Name, folder);
            if (gen != generation) return;
            viewer.Child = AssignKit.Preview(path, f.Name, owned);
        }
        catch (Exception e)
        {
            if (gen != generation) return;
            viewer.Child = Unavailable(SymbolRegular.Warning24, "No Preview", e.Message, () => FetchAndShow(f));
        }
    }

    // ---- the comments ----

    private FrameworkElement Comments()
    {
        var list = a.Comments;
        var head = K.Columns("*, auto", K.Text("Comments", Ty.BodyStrong).Col(0));
        if (list.Count > 0) head.Children.Add(K.Text(list.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), Ty.Body, B.Text2).Col(1));
        head.Margin = new Thickness(18, 14, 18, 14);
        if (list.Count == 0) return K.V(0, head, K.Empty(SymbolRegular.Comment24, "No comments yet.").Margin(18, 0, 18, 0));
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 16, Margin = new Thickness(18, 0, 18, 20) };
        foreach (var c in list) v.Children.Add(Comment(c));
        var scroll = new ScrollViewer { Content = v, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        return K.Rows("auto, *", head.Row(0), scroll.Row(1));
    }

    private FrameworkElement Comment(CommentRow c)
    {
        var top = K.H(6, K.Text(c.Author, Ty.BodyStrong));
        if (c.Attempt is > 0) top.Children.Add(K.Text($"Attempt {c.Attempt}", Ty.Caption, B.Text3).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom));
        var words = K.V(4, top);
        if (!string.IsNullOrEmpty(c.When)) words.Children.Add(K.Text(c.When!, Ty.Caption, B.Text2));
        if (c.Text.Length > 0) words.Children.Add(AssignKit.Selectable(c.Text.Trim(), Ty.Body).Margin(-2, 2, -2, 0));
        foreach (var f in c.Attachments ?? new List<Attachment>())
        {
            var file = f;
            var link = K.Subtle(K.H(6, AssignKit.FileIcon(f.Name, 16), K.Text(f.Name, Ty.Body, B.AccentText)), () => AssignKit.OpenFile(engine, file.Url, file.Name), new Thickness(4, 2, 6, 2));
            link.HorizontalAlignment = HorizontalAlignment.Left;
            link.Margin = new Thickness(-4, 0, 0, 0);
            link.ToolTip = $"Open {f.Name}";
            link.Cursor = Cursors.Hand;
            words.Children.Add(link);
        }
        var avatar = K.Avatar(c.Author, null, c.Avatar, 28);
        avatar.VerticalAlignment = VerticalAlignment.Top;
        return K.Columns("auto, *", avatar.Margin(0, 0, 10, 0).Col(0), words.Col(1));
    }

    // ---- letting go ----

    private void ReleaseViews()
    {
        foreach (var w in owned) { try { w.Dispose(); } catch { } }
        owned.Clear();
    }

    public void Release()
    {
        generation++;
        ReleaseViews();
        viewer.Child = null;
        if (folder != null)
        {
            var f = folder;
            folder = null;
            _ = Task.Delay(800).ContinueWith(_ => { try { Directory.Delete(f, true); } catch { } });
        }
    }
}

/// <summary>
/// The school's file viewer (DocViewer) dressed to sit in a Windows 11 dialog (the Mac's AssignDocViewerSkin): its toolbar a
/// quiet bar in Segoe UI, its buttons rounded and lit under the pointer, the tool in use in the accent, its dashed rules
/// hairlines, its page field a rounded field — light or dark as the page is. DocViewer's markup is its own and unnamed, so
/// the bar is found by its shape (a bar across the top of the page holding several buttons), looked for again as the viewer
/// draws itself; a viewer it does not find is left exactly as the school draws it.
/// </summary>
internal static class AssignDocViewerSkin
{
    public static string Script()
    {
        var c = Appearance.Accent;
        var accent = $"#{c.R:x2}{c.G:x2}{c.B:x2}";
        return """
        (() => {
          if (window.__simplSkin) return; window.__simplSkin = true;
          const accent =
        """ + Json.Str(accent) + """
        ;
          const css = `
            .simpl-bar { background: rgba(243,243,243,.86) !important; -webkit-backdrop-filter: blur(30px) saturate(150%); backdrop-filter: blur(30px) saturate(150%);
              border: 0 !important; border-bottom: 1px solid rgba(0,0,0,.08) !important; box-shadow: none !important; color: rgba(0,0,0,.9) !important; }
            .simpl-bar, .simpl-bar * { font-family: "Segoe UI Variable Text", "Segoe UI", sans-serif !important; letter-spacing: 0 !important; text-transform: none !important; }
            .simpl-bar button, .simpl-bar [role="button"], .simpl-bar a { border-radius: 4px !important; background: transparent !important; color: inherit !important;
              box-shadow: none !important; transition: background-color .083s ease, color .083s ease; }
            .simpl-bar button:hover, .simpl-bar [role="button"]:hover, .simpl-bar a:hover { background: rgba(0,0,0,.037) !important; }
            .simpl-bar button:active, .simpl-bar [role="button"]:active { background: rgba(0,0,0,.024) !important; color: rgba(0,0,0,.62) !important; }
            .simpl-bar :is(button, [role="button"])[aria-pressed="true"], .simpl-bar :is(button, [role="button"])[aria-checked="true"],
            .simpl-bar :is(button, [role="button"])[aria-selected="true"], .simpl-bar .simpl-on { background: ${accent} !important; color: #fff !important; }
            .simpl-bar :is(button, [role="button"])[aria-pressed="true"] *, .simpl-bar .simpl-on * { color: #fff !important; fill: currentColor; }
            .simpl-bar input { border-radius: 4px !important; border: 1px solid rgba(0,0,0,.06) !important; border-bottom-color: rgba(0,0,0,.45) !important;
              background: rgba(255,255,255,.7) !important; color: rgba(0,0,0,.9) !important; box-shadow: none !important; text-align: center; }
            .simpl-bar input:focus { outline: none !important; border-bottom: 2px solid ${accent} !important; background: #fff !important; }
            .simpl-bar .simpl-rule { border-style: solid !important; border-color: rgba(0,0,0,.08) !important; }
            .simpl-empty { display: none !important; }
            .simpl-bar .simpl-bar { background: transparent !important; border-bottom: 0 !important; -webkit-backdrop-filter: none; backdrop-filter: none; }
            @media (prefers-color-scheme: dark) {
              .simpl-bar { background: rgba(32,32,32,.86) !important; border-bottom-color: rgba(255,255,255,.08) !important; color: #fff !important; }
              .simpl-bar button:hover, .simpl-bar [role="button"]:hover, .simpl-bar a:hover { background: rgba(255,255,255,.06) !important; }
              .simpl-bar button:active, .simpl-bar [role="button"]:active { background: rgba(255,255,255,.04) !important; color: rgba(255,255,255,.78) !important; }
              .simpl-bar input { background: rgba(255,255,255,.06) !important; border-color: rgba(255,255,255,.07) !important; border-bottom-color: rgba(255,255,255,.55) !important; color: #fff !important; }
              .simpl-bar input:focus { background: rgba(30,30,30,.9) !important; }
              .simpl-bar .simpl-rule { border-color: rgba(255,255,255,.08) !important; }
            }`;
          const put = () => { if (document.head && !document.getElementById('simpl-skin')) { const st = document.createElement('style'); st.id = 'simpl-skin'; st.textContent = css; document.head.appendChild(st); } };
          // the bar: across the top of the page, short, holding three buttons or more
          const find = () => {
            put();
            const W = window.innerWidth || 1;
            for (const b of document.querySelectorAll('button, [role="button"]')) {
              let el = b.parentElement;
              while (el && el !== document.body) {
                const r = el.getBoundingClientRect();
                if (r.top <= 4 && r.width >= W * .7 && r.height >= 30 && r.height <= 160 && el.querySelectorAll('button, [role="button"]').length >= 3) {
                  if (!el.classList.contains('simpl-bar')) {
                    el.classList.add('simpl-bar');
                    for (const x of el.querySelectorAll('*')) { const cs = getComputedStyle(x); if (/dashed|dotted/.test(cs.borderLeftStyle + cs.borderRightStyle + cs.borderTopStyle + cs.borderBottomStyle)) x.classList.add('simpl-rule'); }
                  }
                  // the whole bar across the top it sits in (the school's grey band, the options' row with it), dressed the same
                  let top = el;
                  while (top.parentElement && top.parentElement !== document.body) {
                    const pr = top.parentElement.getBoundingClientRect();
                    if (pr.top <= 4 && pr.width >= W * .7 && pr.height <= 220) top = top.parentElement; else break;
                  }
                  top.classList.add('simpl-bar');
                  tidy(top);
                  return;
                }
                el = el.parentElement;
              }
            }
          };
          // a row of the bar with nothing in it (the drawing tools' options, while the pointer is the tool) folded away; it
          // comes back the moment something is put in it
          const tidy = (bar) => {
            const bw = bar.getBoundingClientRect().width;
            const rows = [...bar.children].flatMap((c) => [c, ...c.children]);
            for (const r of rows) {
              if (/^(INPUT|SELECT|TEXTAREA|BUTTON|IMG|SVG|CANVAS)$/i.test(r.tagName)) continue;
              const was = r.classList.contains('simpl-empty');
              if (was) r.classList.remove('simpl-empty');
              const rr = r.getBoundingClientRect();
              const row = rr.width >= bw * .6 && rr.height >= 16;
              const empty = row && !r.querySelector('button, [role="button"], input, select, textarea, svg, canvas, img') && !r.textContent.trim();
              r.classList.toggle('simpl-empty', empty);
            }
          };
          let t = 0;
          const soon = () => { clearTimeout(t); t = setTimeout(find, 120); };
          const go = () => { find(); new MutationObserver(soon).observe(document.documentElement, { childList: true, subtree: true }); };
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', go, { once: true }); else go();
        })();
        """;
    }
}
