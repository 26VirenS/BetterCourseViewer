using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Simpl.Kit;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.SignIn;

/// <summary>
/// The first run (the Mac's SchoolPicker): which Canvas or Brightspace to open. The school is found by its name — the
/// listings carried in the app, and Instructure's own "Find my school" for what they lack — as in Windows' own search:
/// the field keeps the keyboard, ↑ and ↓ move through the schools found, Enter opens the one chosen; the address can
/// still be typed by hand. The school chosen is shown before it is opened — a small square of its sign-in page and
/// "Is this your school?": Yes opens it, No comes back to the search as it was left.
/// </summary>
public sealed class SchoolPickerView : Grid
{
    private readonly Action<string> open;
    private readonly Window owner;
    private readonly SchoolSearch search = new();
    private readonly Grid searching = new();
    private readonly TextBox query;
    private readonly Border results;
    private readonly Stack list = new() { Orientation = Orientation.Vertical, Spacing = 2 };
    private readonly ScrollViewer scroller;
    private readonly TextBlock note;
    private readonly ProgressRing asking = K.Spinner(16);
    private readonly Dictionary<string, (Button Row, Border Pill, FrameworkElement Enter)> rows = new();
    private string? chosen;
    private bool confirmAsked;
    private SchoolConfirm? confirm;

    public SchoolPickerView(Action<string> open, Window owner)
    {
        this.open = open;
        this.owner = owner;

        // the welcome: the app's mark, its name, what to do
        var icon = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 84, Height = 84, HorizontalAlignment = HorizontalAlignment.Center };
        var title = K.Text("Welcome to Simpl", Ty.Title).Center();
        var sub = K.Text("Find your school’s Canvas or Brightspace.", Ty.BodyLarge, B.Text2).Center();

        // the field: Windows' search box, large
        query = new TextBox
        {
            PlaceholderText = "School name", Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 }, ClearButtonEnabled = true,
            FontSize = 16, MinHeight = 40, Padding = new Thickness(10, 9, 10, 9),
        };
        query.TextChanged += (_, _) => Find();
        query.PreviewKeyDown += OnFieldKey;
        asking.Visibility = Visibility.Collapsed;
        asking.HorizontalAlignment = HorizontalAlignment.Right;
        asking.Margin = new Thickness(0, 0, 40, 0);
        var fieldGrid = new Grid { Children = { query, asking } };

        // what matches, in a card that grows in under the query
        scroller = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        results = K.Card(scroller, new Thickness(0));
        results.Height = 280;
        results.Visibility = Visibility.Collapsed;

        note = K.Wrapped("If your school’s sign-in page has a username and password, Simpl shows its own sign-in card for it, and can keep you logged in on this PC if you choose.", Ty.Body, B.Text2).Center();
        note.TextAlignment = TextAlignment.Center;

        // the address by hand
        var address = new TextBox { PlaceholderText = "school.instructure.com", ClearButtonEnabled = true };
        var go = K.Button("Continue", () => PickTyped(address.Text), accent: true);
        go.IsEnabled = false;
        address.TextChanged += (_, _) => go.IsEnabled = address.Text.Trim().Length > 0;
        address.KeyDown += (_, e) => { if (e.Key == Key.Enter) { PickTyped(address.Text); e.Handled = true; } };
        var manualRow = K.Columns("*, 8, auto", address.Col(0), go.Col(2));
        var manual = new CardExpander
        {
            Header = K.Text("Enter the address yourself", Ty.Body),
            Icon = new SymbolIcon { Symbol = SymbolRegular.Globe24 },
            Content = K.V(8, manualRow, K.Wrapped("The address you open Canvas or Brightspace at in a browser, for example school.instructure.com or school.brightspace.com.", Ty.Caption, B.Text2)),
        };

        var column = K.V(22, K.V(10, icon, title, sub), fieldGrid, results, note, manual);
        column.Width = 520;
        column.HorizontalAlignment = HorizontalAlignment.Center;
        column.VerticalAlignment = VerticalAlignment.Center;
        column.Margin = new Thickness(24, 12, 24, 36);
        searching.Children.Add(new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Children.Add(searching);

        search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SchoolSearch.Results)) ShowResults();
            if (e.PropertyName == nameof(SchoolSearch.Asking)) { asking.Visibility = search.Asking ? Visibility.Visible : Visibility.Collapsed; ShowResults(); }
        };
        Loaded += async (_, _) =>
        {
            Motion.Stagger(column.Children.OfType<UIElement>(), 60);
            await search.Load();
            query.Focus();
            Keyboard.Focus(query);
            // (the screenshot suite: --picker-query and --picker-confirm)
            if (Launch.Get("picker-query") is { } q) query.Text = q;
        };
    }

    private string Trimmed => query.Text.Trim();

    /// <summary>What Enter opens, in order: the address typed, then each school found.</summary>
    private List<string> Choices
    {
        get
        {
            var outList = new List<string>();
            if (SchoolSearch.AddressLike(query.Text) is { } typed) outList.Add("open:" + typed);
            outList.AddRange(search.Results.Select(r => r.Id));
            return outList;
        }
    }

    private void Find()
    {
        search.Find(query.Text);
        chosen = null;
        ShowResults();
    }

    private void ShowResults()
    {
        var shown = Trimmed.Length > 0;
        if (shown != (results.Visibility == Visibility.Visible))
        {
            results.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            note.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
            if (shown) Motion.Enter(results, 0, -12, 300); else Motion.FadeIn(note);
        }
        list.Children.Clear();
        rows.Clear();
        if (!shown) return;
        if (SchoolSearch.AddressLike(query.Text) is { } typed)
            list.Children.Add(Row("open:" + typed, $"Open {typed}", "The address as typed", SymbolRegular.Open24, () => PickTyped(typed)));
        foreach (var s in search.Results)
        {
            var school = s;
            list.Children.Add(Row(s.Id, s.Name, s.Domain, SymbolRegular.BuildingBank24, () => Pick(school)));
        }
        if (search.Asking && search.Results.Count == 0)
            list.Children.Add(K.H(10, K.Spinner(16), K.Text("Searching…", Ty.Body, B.Text2)).Margin(12));
        else if (search.Results.Count == 0 && SchoolSearch.AddressLike(query.Text) == null)
            list.Children.Add(K.Wrapped("No school by that name. Try another part of its name, or enter its address below.", Ty.Body, B.Text2).Margin(12));
        var all = Choices;
        if (chosen == null || !all.Contains(chosen)) chosen = all.FirstOrDefault();
        Highlight();
        // (the screenshot suite: --picker-confirm shows the first school found, to be confirmed)
        if (!confirmAsked && Launch.Flag("picker-confirm") && search.Results.FirstOrDefault() is { } first)
        {
            confirmAsked = true;
            Pick(first);
        }
    }

    /// <summary>A school in the list (Windows 11's list item: the selection's fill, the accent's pill at its left).</summary>
    private Button Row(string id, string title, string sub, SymbolRegular symbol, Action act)
    {
        var pill = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Hidden };
        pill.SetResourceReference(Border.BackgroundProperty, B.Accent);
        var enter = K.Icon(SymbolRegular.ArrowEnterLeft24, 14, B.Text2);
        enter.Visibility = Visibility.Hidden;
        var words = K.V(0, K.Text(title, Ty.Body), K.Text(sub, Ty.Caption, B.Text2));
        var grid = K.Columns("auto, *, auto",
            K.Icon(symbol, 18, B.AccentText).Margin(6, 0, 14, 0).Col(0),
            words.Col(1),
            enter.Margin(8, 0, 6, 0).Col(2));
        var holder = new Grid { Children = { grid, pill } };
        var b = K.Subtle(holder, act, new Thickness(6, 7, 6, 7));
        pill.Margin = new Thickness(-6, 0, 0, 0);
        b.MouseEnter += (_, _) => { chosen = id; Highlight(); };
        rows[id] = (b, pill, enter);
        return b;
    }

    private void Highlight()
    {
        foreach (var (id, (row, pill, enter)) in rows)
        {
            var on = id == chosen;
            if (on) row.SetResourceReference(Control.BackgroundProperty, B.Subtle); else row.Background = Brushes.Transparent;
            pill.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            enter.Visibility = on ? Visibility.Visible : Visibility.Hidden;
        }
    }

    private void OnFieldKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up)
        {
            var all = Choices;
            if (all.Count == 0) return;
            var step = e.Key == Key.Down ? 1 : -1;
            var i = chosen != null ? all.IndexOf(chosen) : (step > 0 ? -1 : all.Count);
            chosen = all[Math.Clamp(i + step, 0, all.Count - 1)];
            Highlight();
            if (rows.TryGetValue(chosen, out var r)) r.Row.BringIntoView();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            OpenChosen();
            e.Handled = true;
        }
    }

    private void OpenChosen()
    {
        var id = chosen ?? Choices.FirstOrDefault();
        if (id == null) return;
        if (id.StartsWith("open:")) PickTyped(id[5..]);
        else if (search.Results.FirstOrDefault(r => r.Id == id) is { } s) Pick(s);
    }

    /// <summary>An address typed: shown to be confirmed under its own name.</summary>
    private void PickTyped(string raw)
    {
        if (SchoolSearch.NormalizeHost(raw) is not { } host)
        {
            Dialogs.Fail("That does not look like a Canvas or Brightspace address", "Enter the site’s address, such as school.instructure.com or school.brightspace.com.", owner);
            return;
        }
        Pick(new SchoolSearch.School(host, host));
    }

    /// <summary>A school chosen: its sign-in page shown small, to be confirmed.</summary>
    private void Pick(SchoolSearch.School school)
    {
        confirm = new SchoolConfirm(school, () => { confirm?.Stop(); open(school.Domain); }, Back);
        searching.Visibility = Visibility.Collapsed;
        Children.Add(confirm);
        Motion.DrillIn(confirm);
        Shot.Moved();
    }

    /// <summary>No: back to the search as it was left, the same school still picked out.</summary>
    private void Back()
    {
        if (confirm != null) { confirm.Stop(); Children.Remove(confirm); }
        confirm = null;
        searching.Visibility = Visibility.Visible;
        Motion.FadeIn(searching);
        Dispatcher.BeginInvoke(() => { query.Focus(); Keyboard.Focus(query); }, System.Windows.Threading.DispatcherPriority.Input);
    }
}
