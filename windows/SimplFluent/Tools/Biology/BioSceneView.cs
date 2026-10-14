using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// The biology toolkit's 3D view (spec §3.4, §3.5, §9), the Mac's BioSceneView: WPF's Viewport3D with the molecule
/// builder's lights on a clear ground, and an orbit camera — drag to turn, scroll to zoom, right-drag to pan; framing the
/// content's ball with a 40° field of view at 1.15·R/sin 20°; double-click empty space to reset, an object to zoom to it.
/// A scene is made off the UI thread from values (<see cref="BioSceneContent"/>, by <see cref="Show"/>) and batched one
/// frozen mesh per material; a click picks an object by its id (the nearest opaque one, through translucent membranes);
/// the pointer over an object outlines it in the accent and turns to a hand; the selection glows and the rest dims to
/// 35%; labels with leader lines sit in 2D over it (§3.5: two columns, at most 14, "+N more", pressable, fading in
/// 120 ms); ← → ↑ ↓ turn it 15° a press, + and − zoom, Escape lets go. Auto-turn is off unless asked (<see
/// cref="Turning"/>, the "Turn" button), never with Windows' animations off, and waits three seconds after a touch;
/// camera moves take 500 ms, or jump with animations off. While a scene is being made the screenshot suite's picture
/// waits for it (Shot.Began/Ended).
/// </summary>
public sealed class BioSceneView : Grid
{
    private static readonly double Tan20 = Math.Tan(20 * Math.PI / 180);

    private readonly Viewport3D view = new() { ClipToBounds = true, IsHitTestVisible = true };
    private readonly PerspectiveCamera camera = new() { LookDirection = new Vector3D(0, 0, -1), UpDirection = new Vector3D(0, 1, 0), FieldOfView = 40 };
    private readonly ModelVisual3D world = new(), billboardLayer = new();
    private readonly Model3DGroup worldGroup = new(), billboardGroup = new();
    private readonly QuaternionRotation3D turnRotation = new();
    private readonly RotateTransform3D turn;
    private readonly Canvas overlay = new() { ClipToBounds = true };
    private readonly Canvas labelLayer = new();
    private readonly Canvas markLayer = new() { IsHitTestVisible = false };
    // (charges' signs over the labels too: a label too wide for its room, moved in over the model, hid the carboxyl's −)
    private readonly Canvas chargeLayer = new() { IsHitTestVisible = false };
    private readonly Path leaders = new() { StrokeThickness = 1, IsHitTestVisible = false, Opacity = 0.55 };
    private readonly ContentControl spinnerHost = new() { Focusable = false, IsHitTestVisible = false, Margin = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Dictionary<string, FrameworkElement> labelViews = new();
    private FrameworkElement? moreText;
    private string? moreWords;

    // (what is drawn: the scene, its batches by model, and what each model's vertices belong to)
    private BioSceneContent? content;
    private BioSphere framing = new(BioVec.Zero, 1);
    /// <summary>The content as up to 256 boxes in its own space (least and greatest x, y, z of a run of items), for the
    /// labels' room round the model: one box round it all would be drawn far wider than the model by its near corners.</summary>
    private List<(BioVec Min, BioVec Max)> extents = new();
    private List<(BioMaterial Material, int Order, BioMeshBatch Batch, MeshGeometry3D Mesh)> batches = new();
    private readonly Dictionary<GeometryModel3D, (BioMeshBatch? Batch, string? Id, bool Translucent)> pickMap = new();
    private List<(BioBillboard Board, TranslateTransform3D Move)> boards = new();
    private readonly Dictionary<GeometryModel3D, BioBillboard> boardPick = new();
    private string? builtKey, buildingKey, framedKey;
    private bool framedOnce;
    private Func<IEnumerable<BioBillboard>>? extras;
    private Func<IEnumerable<BioMark>>? markSource;
    private List<(BioMark Mark, TextBlock View)> marks = new();

    // (the camera: what it looks at, how far it is; the model's turn)
    private Point3D target;
    private double distance = 5;
    private Quaternion orientation = Quaternion.Identity;
    private DateTime pausedUntil;
    private TimeSpan? lastFrame;
    private (double Start, double Duration, Point3D T0, Point3D T1, double D0, double D1, Quaternion Q0, Quaternion Q1)? move;

    // (the pointer: where it was pressed; where it was on the screen when last noted, so a still one points at nothing)
    private Point? downAt;
    private Point? pointerAt;
    private bool dragged, panning;
    private DateTime lastHover = DateTime.MinValue;
    private string? hovered;
    private readonly ModelVisual3D hoverLayer = new();

    // (the labels: when they were laid out, and what hid them when the view was last still)
    private string labelSignature = "";
    private DateTime stillSince = DateTime.UtcNow;
    private bool raysDone;
    private readonly Dictionary<string, bool> rayVisible = new();

    public BioSceneView(string accessibility = "3D model")
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Arrow;
        view.Camera = camera;
        var lights = new Model3DGroup();
        // (the molecule builder's lights: SceneKit's ambient 380, key 1000 and fill 350, as WPF lights)
        lights.Children.Add(new AmbientLight(Color.FromRgb(0x61, 0x61, 0x61)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0xe0, 0xe0, 0xe0), new Vector3D(-0.48, -0.50, -0.73)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x59, 0x59, 0x59), new Vector3D(0.675, -0.353, 0.647)));
        view.Children.Add(new ModelVisual3D { Content = lights });
        turn = new RotateTransform3D(turnRotation);
        world.Content = worldGroup;
        world.Transform = turn;
        hoverLayer.Transform = turn;
        view.Children.Add(world);
        view.Children.Add(hoverLayer);
        billboardLayer.Content = billboardGroup;
        view.Children.Add(billboardLayer);
        Children.Add(view);
        leaders.SetResourceReference(Shape.StrokeProperty, B.Text);
        overlay.Children.Add(markLayer);
        overlay.Children.Add(leaders);
        overlay.Children.Add(labelLayer);
        overlay.Children.Add(chargeLayer);
        Children.Add(overlay);
        Children.Add(spinnerHost);
        Accessibility = accessibility;

        Loaded += (_, _) => Wake();
        Unloaded += (_, _) => Sleep();
        SizeChanged += (_, e) =>
        {
            // (40° is the height's field of view, as SceneKit's; WPF's is the width's)
            if (e.NewSize.Height > 0) camera.FieldOfView = 2 * Math.Atan(Tan20 * e.NewSize.Width / e.NewSize.Height) * 180 / Math.PI;
            labelSignature = "";
            Refit();
            PlaceMarks();
            Wake();
        };
    }

    private bool ticking;

    /// <summary>Frame by frame while something moves (the camera, auto-turn, labels settling); asleep otherwise, so a still
    /// scene costs nothing.</summary>
    private void Wake()
    {
        if (ticking || !IsLoaded) return;
        ticking = true;
        lastFrame = null;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Sleep()
    {
        if (!ticking) return;
        ticking = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    // ---- what a tool sets ----

    /// <summary>What Narrator says the view shows ("3D model of an animal cell, cut open; 16 kinds of parts; selected: …").</summary>
    public string Accessibility
    {
        get => System.Windows.Automation.AutomationProperties.GetName(this);
        set => System.Windows.Automation.AutomationProperties.SetName(this, value);
    }

    /// <summary>The selected object: an id, or the start of ids ("organelle:mito" selects every "organelle:mito:…").</summary>
    public string? Selection
    {
        get => selection;
        set
        {
            if (selection == value) return;
            selection = value;
            ApplySelection();
        }
    }
    private string? selection;

    /// <summary>Dim everything but the selection to 35%.</summary>
    public bool DimOthers
    {
        get => dimOthers;
        set { if (dimOthers == value) return; dimOthers = value; ApplySelection(); }
    }
    private bool dimOthers = true;

    /// <summary>The labels with leader lines over the scene.</summary>
    public bool ShowLabels
    {
        get => showLabels;
        set { if (showLabels == value) return; showLabels = value; labelSignature = ""; if (!value) ClearLabels(); Refit(); Wake(); }
    }
    private bool showLabels = true;

    /// <summary>The view's bottom-left corner kept clear of labels (what a page lays over it there: Cell Explorer's scale
    /// bar), its width and height; none by default.</summary>
    public Size LabelKeepClear
    {
        get => labelKeepClear;
        set { if (labelKeepClear == value) return; labelKeepClear = value; labelSignature = ""; Wake(); }
    }
    private Size labelKeepClear;

    /// <summary>Auto-turn (the "Turn" button): once round in 30 s; never with Windows' animations off.</summary>
    public bool Turning
    {
        get => turning;
        set { turning = value; Wake(); }
    }
    private bool turning;

    /// <summary>A press on an object or its label, or on nothing (null). By default it sets <see cref="Selection"/>.</summary>
    public event Action<string?>? Picked;
    /// <summary>A double-click on an object (it is zoomed to), or on nothing (the view is reset).</summary>
    public event Action<string?>? DoubleClicked;
    /// <summary>The object under the pointer, or its label's, changed.</summary>
    public event Action<string?>? HoverChanged;
    /// <summary>"+N more" pressed (§3.5: it opens the parts list). Without a handler it is words only.</summary>
    public event Action? MorePressed;
    /// <summary>Whether a press sets <see cref="Selection"/> itself (off: the tool does, from <see cref="Picked"/>).</summary>
    public bool PickSelects { get; set; } = true;

    /// <summary>The scene shown, once made.</summary>
    public BioSceneContent? Content => content;
    /// <summary>The object under the pointer (or whose label is under it).</summary>
    public string? Hovered => hovered;

    /// <summary>
    /// The scene to show: made again (off the UI thread, by <paramref name="build"/>) when <paramref name="key"/> changes —
    /// put in the key everything the scene depends on (the page, the options, light or dark), and make the build capture
    /// copies of what it needs, never a view. The camera frames the first scene, and any scene whose
    /// <paramref name="frameKey"/> is new (a new cell, not a new option). <paramref name="billboards"/> are made on the
    /// UI thread with each new scene (an atom's symbol: BioMoleculeScene.Symbols), and so are <paramref name="marks"/>, drawn
    /// in 2D over the whole model (a strand's "3′", a carbon's number).
    /// </summary>
    public void Show(string key, Func<BioSceneContent> build, string? frameKey = null, Func<IEnumerable<BioBillboard>>? billboards = null,
        Func<IEnumerable<BioMark>>? marks = null)
    {
        extras = billboards;
        markSource = marks;
        if (key == builtKey)
        {
            // (back to the scene shown while another was being made — an option turned on and off again, light to dark and
            //  back: the other is let go of, so it is never shown in place of what was asked for last)
            if (buildingKey != null) { buildingKey = null; spinnerHost.Content = null; }
            return;
        }
        if (key == buildingKey) return;
        buildingKey = key;
        Shot.Began();
        var spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        spinnerTimer.Tick += (_, _) => { spinnerTimer.Stop(); if (buildingKey == key) spinnerHost.Content = K.Spinner(18); };
        spinnerTimer.Start();
        Task.Run(() =>
        {
            var made = build();
            return (Made: made, Parts: BioWpf.Batches(made.Items), Frame: made.Framing, Extents: Extents(made));
        }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            spinnerTimer.Stop();
            try
            {
                if (t.IsFaulted) { Log.Write($"bio scene {key}: {t.Exception?.GetBaseException().Message}"); if (buildingKey == key) buildingKey = null; return; }
                if (buildingKey != key) return;
                buildingKey = null;
                builtKey = key;
                spinnerHost.Content = null;
                extents = t.Result.Extents;
                Apply(t.Result.Made, t.Result.Parts, t.Result.Frame, frameKey);
            }
            finally { Shot.Ended(); }
        }), TaskScheduler.Default);
    }

    /// <summary>The camera put back (Reset view; a double-click on empty space).</summary>
    public void ResetView() => Frame(framing, animated: true, resetTurn: true);

    /// <summary>The camera brought to an object (its items' ball, a little room round it).</summary>
    public void ZoomTo(string id)
    {
        if (content == null) return;
        var items = content.ItemsMatching(id);
        if (items.Count == 0) return;
        var pts = new List<BioVec>();
        foreach (var it in items)
        {
            var (c, r) = it.Mesh.Bounds;
            pts.AddRange(new[] { c + new BioVec(r, 0, 0), c - new BioVec(r, 0, 0), c + new BioVec(0, r, 0), c - new BioVec(0, r, 0) });
        }
        var s = BioV.BoundingSphere(pts);
        // (the object's centre where the turn has taken it)
        var w = turn.Transform(BioWpf.P(s.Centre));
        MoveCamera(w, 1.15 * Math.Max(s.Radius * 1.3, 1e-3) / Math.Sin(20 * Math.PI / 180), orientation, Motion.On ? 0.5 : 0);
    }

    // ---- a scene made: drawn ----

    private void Apply(BioSceneContent made, List<(BioMaterial Material, int Order, BioMeshBatch Batch)> parts, BioSphere frame, string? frameKey)
    {
        content = made;
        framing = frame;
        batches = parts.Select(p => (p.Material, p.Order, p.Batch, BioWpf.Mesh(p.Batch.Mesh))).ToList();
        rayVisible.Clear();
        raysDone = false;
        labelSignature = "";
        boards = new List<(BioBillboard, TranslateTransform3D)>();
        boardPick.Clear();
        billboardGroup.Children.Clear();
        foreach (var b in extras?.Invoke() ?? Array.Empty<BioBillboard>())
        {
            var mv = new TranslateTransform3D();
            var holder = new Model3DGroup { Transform = mv };
            holder.Children.Add(b.Model);
            billboardGroup.Children.Add(holder);
            boards.Add((b, mv));
            if (b.Model is GeometryModel3D gm && b.Id != null) boardPick[gm] = b;
        }
        MakeMarks();
        ApplySelection();
        // (the camera frames the first scene, and a scene whose frame key is new; another keeps the camera where it is, its
        //  fit worked out afresh for the scene's own size, so a resize after it keeps the same share of it)
        if (!framedOnce || (frameKey != null && frameKey != framedKey))
        {
            framedOnce = true;
            framedKey = frameKey;
            Frame(framing, animated: false, resetTurn: true);
        }
        else
        {
            fitDistance = FitDistance(framing, fitTurn);
            Place();
        }
        SetHover(null);
    }

    /// <summary>The batches drawn again round the selection: the selected objects on their own, glowing; the rest dimmed
    /// (and so drawn after them, see-through); the opaque before the translucent, outer shells last.</summary>
    private void ApplySelection()
    {
        if (content == null) return;
        pickMap.Clear();
        worldGroup.Children.Clear();
        var sel = selection;
        var selected = sel == null ? new List<BioSceneItem>() : content.ItemsMatching(sel);
        var dim = selected.Count > 0 && dimOthers;
        foreach (var it in selected)
        {
            var m = it.Material with { Emission = Math.Max(it.Material.Emission, 0.35) };
            var g = new GeometryModel3D(BioWpf.Mesh(it.Mesh), BioWpf.Material(m));
            if (m.IsTranslucent || m.DoubleSided) g.BackMaterial = g.Material;
            pickMap[g] = (null, it.Id ?? sel, m.IsTranslucent);
            worldGroup.Children.Add(g);
        }
        // (when something is selected, its own items are drawn above and left out here: only the batches that held them
        // are made again)
        var drawn = batches;
        if (selected.Count > 0)
        {
            var skip = new HashSet<BioSceneItem>(selected);
            var touched = selected.Select(i => (i.Material, i.Order)).ToHashSet();
            var redone = BioWpf.Batches(content.Items.Where(i => touched.Contains((i.Material, i.Order)) && !skip.Contains(i)))
                .Select(p => (p.Material, p.Order, p.Batch, BioWpf.Mesh(p.Batch.Mesh)));
            drawn = batches.Where(b => !touched.Contains((b.Material, b.Order))).Concat(redone).ToList();
        }
        foreach (var b in drawn.OrderBy(b => b.Material.IsTranslucent || dim ? 1 : 0).ThenBy(b => b.Material.IsTranslucent ? 1 : 0).ThenBy(b => b.Order))
        {
            var fade = dim ? 0.35 : 1;
            var mat = BioWpf.Material(b.Material, fade);
            var g = new GeometryModel3D(b.Mesh, mat);
            if (b.Material.IsTranslucent || b.Material.DoubleSided || dim) g.BackMaterial = mat;
            pickMap[g] = (b.Batch, null, b.Material.IsTranslucent);
            worldGroup.Children.Add(g);
        }
        labelSignature = "";
        Outline(hovered);
        Wake();
    }

    // ---- the camera ----

    /// <summary>The camera framing a scene: 40° field of view, looking along −z at its ball's centre, as far off as fills
    /// its room with the model (<see cref="FitDistance"/>).</summary>
    private void Frame(BioSphere s, bool animated, bool resetTurn)
    {
        var q = resetTurn && !Turning ? Quaternion.Identity : orientation;
        var d = FitDistance(s, q);
        fitTurn = q;
        fitDistance = d;
        camera.NearPlaneDistance = Math.Max(0.001, d * 0.01);
        camera.FarPlaneDistance = d * 20;
        turn.CenterX = s.Centre.X;
        turn.CenterY = s.Centre.Y;
        turn.CenterZ = s.Centre.Z;
        MoveCamera(BioWpf.P(s.Centre), d, q, animated && Motion.On ? 0.5 : 0);
    }

    /// <summary>The distance the scene was last fitted at (0: none yet), and the turn it was fitted for.</summary>
    private double fitDistance;
    private Quaternion fitTurn = Quaternion.Identity;

    /// <summary>
    /// How far from its ball's centre the camera stands for the model, turned by <paramref name="q"/>, to fill 85% of its
    /// room each way: the view, less a column for the labels at each side while they are shown. Measured on the model's
    /// own boxes (<see cref="extents"/>), not its ball, so a long model (the helix) or a wide one (a cell) fills its view —
    /// a ball's worth of room round a narrow helix left it small in a big empty card. Before the view has a size, or for an
    /// empty scene: the ball at 1.15·R / sin 20°, as before.
    /// </summary>
    private double FitDistance(BioSphere s, Quaternion q)
    {
        var ball = 1.15 * Math.Max(s.Radius, 1e-3) / Math.Sin(20 * Math.PI / 180);
        if (ActualWidth < 40 || ActualHeight < 40 || extents.Count == 0) return ball;
        var f = ActualHeight / (2 * Tan20);
        var room = Math.Max(ActualWidth - 2 * LabelRoom(), ActualWidth * 0.55);
        double hw = 0.425 * room, hh = 0.425 * ActualHeight;
        var c = BioWpf.P(s.Centre);
        var m = Matrix3D.Identity;
        m.RotateAt(q, c);
        // (a scene that names the ball to frame keeps to it: a bacterium's long flagella reach far past the cell its ball
        //  holds, and fitting them all left the cell a speck)
        var held = content?.Frame != null;
        double Held(double v, double mid) => held ? Math.Clamp(v, mid - s.Radius, mid + s.Radius) : v;
        double d = 0, front = double.MinValue;
        foreach (var (lo, hi) in extents)
            for (var i = 0; i < 8; i++)
            {
                var corner = new Point3D(Held((i & 1) == 0 ? lo.X : hi.X, s.Centre.X), Held((i & 2) == 0 ? lo.Y : hi.Y, s.Centre.Y), Held((i & 4) == 0 ? lo.Z : hi.Z, s.Centre.Z));
                var p = m.Transform(corner) - c;
                // (the camera at the centre and d out along +z: this corner is d − p.Z in front of it, and shows
                //  |p.X|·f / (d − p.Z) from the view's middle)
                d = Math.Max(d, p.Z + Math.Max(Math.Abs(p.X) * f / hw, Math.Abs(p.Y) * f / hh));
                front = Math.Max(front, p.Z);
            }
        // (never inside the model)
        d = Math.Max(d, front + 0.05 * Math.Max(s.Radius, 1e-3));
        return double.IsFinite(d) && d > 0 ? d : ball;
    }

    /// <summary>The room each side of the model kept for a column of labels while they are shown: its widest label (up to
    /// 170), the column's 24 from the model and the view's 16 inset.</summary>
    private double LabelRoom()
    {
        if (!showLabels || content is not { Labels.Count: > 0 } c) return 0;
        return Math.Min(c.Labels.Max(l => LabelWidth(l.Text)), 170) + BioLabelLayout.ColumnOffset + BioLabelLayout.Inset;
    }

    /// <summary>The model kept filling its room as the view's size (or the labels' room) changes: the camera's distance
    /// scaled by how the fit has changed — the scene framed again if the student has not zoomed, a zoom of their own kept as
    /// the same share of it.</summary>
    private void Refit()
    {
        if (content == null || fitDistance <= 0) return;
        var fit = FitDistance(framing, fitTurn);
        var k = fit / fitDistance;
        fitDistance = fit;
        if (!double.IsFinite(k) || Math.Abs(k - 1) < 1e-3) return;
        distance *= k;
        if (move is { } mv) move = (mv.Start, mv.Duration, mv.T0, mv.T1, mv.D0 * k, mv.D1 * k, mv.Q0, mv.Q1);
        camera.NearPlaneDistance = Math.Max(0.001, fit * 0.01);
        camera.FarPlaneDistance = fit * 20;
        Place();
    }

    private static double Now => Environment.TickCount64 / 1000.0;

    private void MoveCamera(Point3D to, double dist, Quaternion q, double seconds)
    {
        if (seconds <= 0)
        {
            move = null;
            target = to;
            distance = dist;
            orientation = q;
            Place();
            return;
        }
        move = (Now, seconds, target, to, distance, dist, orientation, q);
        Wake();
    }

    /// <summary>The camera and the model's turn set as they are now; what follows the model moved with it.</summary>
    private void Place()
    {
        camera.Position = new Point3D(target.X, target.Y, target.Z + distance);
        turnRotation.Quaternion = orientation;
        foreach (var (b, mv) in boards)
        {
            var p = turn.Transform(BioWpf.P(b.Anchor));
            mv.OffsetX = p.X + b.ShiftX;
            mv.OffsetY = p.Y + b.ShiftY;
            mv.OffsetZ = p.Z + b.Lift;
        }
        PlaceMarks();
        Wake();
    }

    /// <summary>The marks' words for a new scene (in the page's text colour, or a charge's own), each with a halo of the
    /// ground round it, so it reads over atoms.</summary>
    private void MakeMarks()
    {
        markLayer.Children.Clear();
        chargeLayer.Children.Clear();
        marks = new List<(BioMark, TextBlock)>();
        var ground = Appearance.Dark ? Color.FromRgb(0x20, 0x20, 0x20) : Colors.White;
        foreach (var m in markSource?.Invoke() ?? Array.Empty<BioMark>())
        {
            var t = new TextBlock { Text = m.Text, FontWeight = FontWeights.Bold, IsHitTestVisible = false };
            if (m.Colour is { } colour) t.Foreground = new SolidColorBrush(colour);
            else t.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
            t.Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 5, Opacity = 1, Color = ground };
            (m.Charge ? chargeLayer : markLayer).Children.Add(t);
            marks.Add((m, t));
        }
    }

    /// <summary>The marks where their points are on the view now: 11–17 px tall as the zoom has them (a charge's sign
    /// 18–26), and an atom's number just outside the atom's disc, out from it the way its point is (or, seen end on, away
    /// from the model's centre); a charge's sign on the disc's edge at the upper right, as N⁺ is written.</summary>
    private void PlaceMarks()
    {
        if (marks.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var f = ActualHeight / (2 * Tan20);
        var c = Project(framing.Centre);
        foreach (var (m, t) in marks)
        {
            var (x, y, front, dist) = Project(m.Anchor);
            t.Visibility = front ? Visibility.Visible : Visibility.Collapsed;
            if (!front) continue;
            t.FontSize = m.Charge ? Math.Clamp(1.6 * m.Size * f / Math.Max(dist, 1e-6), 18, 26) : Math.Clamp(m.Size * f / Math.Max(dist, 1e-6), 11, 17);
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var (w, h) = (t.DesiredSize.Width, t.DesiredSize.Height);
            if (m.Atom is { } atom && Project(atom) is { InFront: true } a)
            {
                double dx = x - a.X, dy = y - a.Y;
                if (m.Charge) (dx, dy) = (1, -1);
                else if (Math.Sqrt(dx * dx + dy * dy) < 1) (dx, dy) = (a.X - c.X, a.Y - c.Y);
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1) (dx, dy, len) = (1, 0, 1);
                // (a sign's glyph is about a third of its line: set so it just overlaps the disc's edge, a superscript)
                var disc = m.AtomRadius * f / Math.Max(a.Distance, 1e-6);
                var reach = m.Charge ? disc + 0.3 * t.FontSize : disc + Math.Max(w, h) / 2 + 1;
                (x, y) = (a.X + dx / len * reach, a.Y + dy / len * reach);
            }
            Canvas.SetLeft(t, x - w / 2);
            Canvas.SetTop(t, y - h / 2);
        }
    }

    /// <summary>Handled (dragged, scrolled, a key): auto-turn waits three seconds after the last touch; labels wait for the
    /// view to be still before testing what hides them.</summary>
    private void Handled()
    {
        pausedUntil = DateTime.UtcNow.AddSeconds(3);
        stillSince = DateTime.UtcNow;
        raysDone = false;
        Wake();
    }

    private void TurnBy(double degrees, bool vertical, bool animated)
    {
        Handled();
        var axis = vertical ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
        var q = new Quaternion(axis, degrees) * orientation;
        q.Normalize();
        MoveCamera(target, distance, q, animated && Motion.On ? 0.25 : 0);
    }

    private void ZoomBy(double factor, bool animated)
    {
        Handled();
        var d = Math.Clamp(distance * factor, Math.Max(framing.Radius * 0.15, 1e-3), Math.Max(framing.Radius * 40, 1));
        MoveCamera(target, d, orientation, animated && Motion.On ? 0.2 : 0);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs re) return;
        var now = re.RenderingTime;
        var dt = lastFrame is { } before ? (now - before).TotalSeconds : 0;
        lastFrame = now;
        if (move is { } m)
        {
            var u = Math.Clamp((Now - m.Start) / m.Duration, 0, 1);
            var t = BioStepTiming.Ease(u);
            target = m.T0 + (m.T1 - m.T0) * t;
            distance = m.D0 + (m.D1 - m.D0) * t;
            orientation = Quaternion.Slerp(m.Q0, m.Q1, t);
            if (u >= 1) move = null;
            Place();
        }
        else if (Turning && Motion.On && downAt == null && DateTime.UtcNow >= pausedUntil && dt > 0 && dt < 1)
        {
            // (once round in thirty seconds, about the view's up)
            orientation = new Quaternion(new Vector3D(0, 1, 0), dt * 360 / 30) * orientation;
            orientation.Normalize();
            Place();
        }
        if (showLabels) TickLabels();
        // (nothing moving and the labels settled: asleep until something changes)
        var turningOn = Turning && Motion.On;
        var labelsSettled = !showLabels || content == null || content.Labels.Count == 0 || raysDone;
        if (move == null && !turningOn && labelsSettled && downAt == null) Sleep();
    }

    // ---- the pointer and the keys ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (IsOverLabel(e.OriginalSource)) return;
        Focus();
        if (e.ClickCount == 2)
        {
            var id = ObjectAt(e.GetPosition(view))?.Id;
            if (id != null) { ZoomTo(id); DoubleClicked?.Invoke(id); }
            else { ResetView(); DoubleClicked?.Invoke(null); }
            e.Handled = true;
            return;
        }
        downAt = e.GetPosition(this);
        dragged = false;
        panning = false;
        CaptureMouse();
        Handled();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (IsOverLabel(e.OriginalSource)) return;
        Focus();
        downAt = e.GetPosition(this);
        dragged = false;
        panning = true;
        CaptureMouse();
        Handled();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var at = e.GetPosition(this);
        if (downAt is { } from && IsMouseCaptured)
        {
            if (!dragged && (at - from).Length <= 3) return;
            dragged = true;
            var dx = at.X - from.X;
            var dy = at.Y - from.Y;
            downAt = at;
            move = null;
            if (panning)
            {
                // (the scene follows the pointer: a pixel is this far at the target's depth)
                var unit = 2 * distance * Tan20 / Math.Max(ActualHeight, 1);
                target = new Point3D(target.X - dx * unit, target.Y + dy * unit, target.Z);
            }
            else
            {
                var q = new Quaternion(new Vector3D(0, 1, 0), dx * 0.45) * new Quaternion(new Vector3D(1, 0, 0), dy * 0.45) * orientation;
                q.Normalize();
                orientation = q;
            }
            Place();
            Handled();
            return;
        }
        // (only the pointer's own movement points at a part: WPF tells a view that opens, or a scene drawn, under a still
        //  pointer as a move too — and the part under it then showed as pointed at with nothing done, its name over the
        //  view, its label ringed and, the plasma membrane's, its outline tinting the whole cell in the accent)
        var moved = ScreenPointer.Moved(ref pointerAt);
        if (IsOverLabel(e.OriginalSource) || !moved) return;
        // (a hit test at most every 50 ms)
        if ((DateTime.UtcNow - lastHover).TotalMilliseconds < 50) return;
        lastHover = DateTime.UtcNow;
        SetHover(ObjectAt(e.GetPosition(view))?.Id);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsMouseCaptured) SetHover(null);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (downAt == null || panning) return;
        var click = !dragged;
        downAt = null;
        dragged = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
        if (click) Pick(ObjectAt(e.GetPosition(view))?.Id);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (downAt == null || !panning) return;
        downAt = null;
        dragged = panning = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        downAt = null;
        dragged = panning = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomBy(Math.Pow(0.9, e.Delta / 120.0), animated: false);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        switch (e.Key)
        {
            case Key.Left: TurnBy(-15, false, true); break;
            case Key.Right: TurnBy(15, false, true); break;
            case Key.Up: TurnBy(-15, true, true); break;
            case Key.Down: TurnBy(15, true, true); break;
            case Key.Escape: Pick(null); break;
            case Key.OemPlus or Key.Add: ZoomBy(0.85, true); break;
            case Key.OemMinus or Key.Subtract: ZoomBy(1 / 0.85, true); break;
            default: return;
        }
        e.Handled = true;
    }

    private bool IsOverLabel(object source)
    {
        for (var d = source as DependencyObject; d != null && d != this; d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, labelLayer)) return true;
        return false;
    }

    /// <summary>A press on an object (or its label), or on nothing.</summary>
    public void Pick(string? id)
    {
        if (PickSelects) Selection = id;
        Picked?.Invoke(id);
    }

    // ---- picking ----

    /// <summary>The object at a point of the view (its id, and where on it the ray met it, in the scene's own frame), the
    /// nearest opaque one first (a translucent membrane does not hide what is inside it).</summary>
    private (string Id, double Distance)? ObjectAt(Point p)
    {
        if (content == null) return null;
        var hits = new List<(string Id, double Distance, bool Translucent)>();
        VisualTreeHelper.HitTest(view, null, r =>
        {
            if (r is RayMeshGeometry3DHitTestResult bm && bm.ModelHit is GeometryModel3D bg && boardPick.TryGetValue(bg, out var board) && board.Id != null)
            {
                var at = turn.Transform(BioWpf.P(board.Anchor)) + new Vector3D(board.ShiftX, board.ShiftY, board.Lift);
                hits.Add((board.Id, (at - camera.Position).Length, false));
                return HitTestResultBehavior.Continue;
            }
            if (r is RayMeshGeometry3DHitTestResult m && m.ModelHit is GeometryModel3D g && pickMap.TryGetValue(g, out var info))
            {
                var id = info.Batch?.ObjectAtVertex(m.VertexIndex1) ?? info.Id;
                if (!string.IsNullOrEmpty(id))
                {
                    // (where the ray met it, from the triangle's corners: then as far from the camera as that is)
                    var pos = m.MeshHit.Positions;
                    var local = new Point3D(
                        pos[m.VertexIndex1].X * m.VertexWeight1 + pos[m.VertexIndex2].X * m.VertexWeight2 + pos[m.VertexIndex3].X * m.VertexWeight3,
                        pos[m.VertexIndex1].Y * m.VertexWeight1 + pos[m.VertexIndex2].Y * m.VertexWeight2 + pos[m.VertexIndex3].Y * m.VertexWeight3,
                        pos[m.VertexIndex1].Z * m.VertexWeight1 + pos[m.VertexIndex2].Z * m.VertexWeight2 + pos[m.VertexIndex3].Z * m.VertexWeight3);
                    var w = turn.Transform(local);
                    hits.Add((id, (w - camera.Position).Length, info.Translucent));
                }
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(p));
        if (hits.Count == 0) return null;
        hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var opaque = hits.FirstOrDefault(h => !h.Translucent);
        var best = opaque.Id != null ? opaque : hits[0];
        return (best.Id, best.Distance);
    }

    // ---- hover ----

    private void SetHover(string? id)
    {
        if (id == hovered) return;
        hovered = id;
        // (the screenshot suite: what the pointer pointed at, for CI to read — nothing, under a still pointer)
        if (id != null && Shot.Asked) Log.Write($"bio scene: pointed at {id}");
        Cursor = id == null ? Cursors.Arrow : Cursors.Hand;
        Outline(id);
        foreach (var (key, v) in labelViews) Ring(v, Lit(LabelIdOf(key)));
        HoverChanged?.Invoke(id);
    }

    private bool Lit(string labelId) => hovered != null && (BioSceneContent.Matches(hovered, labelId) || BioSceneContent.Matches(labelId, hovered));

    private static readonly Dictionary<string, double> labelWidths = new();

    /// <summary>A label's capsule width: its words measured as the capsule draws them (12 semibold), its 10 of padding a
    /// side and its edge — never less than the shared estimate. The estimate alone cut short a short label of wide letters
    /// ("D a…" for "D arm").</summary>
    private double LabelWidth(string text)
    {
        if (labelWidths.TryGetValue(text, out var w)) return w;
        var face = new Typeface(K.TextFace, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 12, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        w = Math.Max(BioLabelLayout.EstimatedWidth(text), Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 22);
        return labelWidths[text] = w;
    }

    /// <summary>A label's view is kept by its object and its words together: two labels may name one object (a strand's
    /// first base and its 5′ end), and each is drawn.</summary>
    private static string LabelKey(string id, string text) => id + "\u0001" + text;

    private static string LabelIdOf(string key)
    {
        var i = key.IndexOf('\u0001');
        return i >= 0 ? key[..i] : key;
    }

    /// <summary>The hovered object outlined: its surface pushed out a touch and drawn from behind only, in the accent (an
    /// inverted hull), so a rim of the accent shows round it. Not a see-through one (a membrane, the cytoplasm): its hull's
    /// inside shows through it, and tinted all it holds — the whole cell — in the accent.</summary>
    private void Outline(string? id)
    {
        hoverLayer.Content = null;
        if (id == null || content == null) return;
        var items = content.ItemsMatching(id).Where(i => !i.Material.IsTranslucent).ToList();
        if (items.Count == 0 || items.Sum(i => i.Mesh.TriangleCount) > 150_000) return;
        var push = (float)(framing.Radius * 0.006);
        var accent = new EmissiveMaterial(new SolidColorBrush(Appearance.Accent));
        var mat = new MaterialGroup { Children = { new DiffuseMaterial(Brushes.Black), accent } };
        mat.Freeze();
        var group = new Model3DGroup();
        foreach (var it in items)
        {
            var hull = it.Mesh.Copy();
            for (var i = 0; i < hull.Positions.Count; i++) hull.Positions[i] += hull.Normals[i] * push;
            group.Children.Add(new GeometryModel3D { Geometry = BioWpf.Mesh(hull), BackMaterial = mat });
        }
        hoverLayer.Content = group;
    }

    // ---- the labels (§3.5) ----

    /// <summary>A point of the scene where the view shows it (y down), and whether it is in front of the camera.</summary>
    private (double X, double Y, bool InFront, double Distance) Project(BioVec p)
    {
        var w = turn.Transform(BioWpf.P(p));
        var v = w - camera.Position;
        var z = -v.Z;
        var f = ActualHeight / (2 * Tan20);
        if (z <= camera.NearPlaneDistance) return (0, 0, false, v.Length);
        return (ActualWidth / 2 + v.X * f / z, ActualHeight / 2 - v.Y * f / z, true, v.Length);
    }

    /// <summary>Each frame: if the camera or the model moved (or the scene is new), the anchors projected and the labels
    /// laid out; once it has been still for 150 ms, what hides each anchor is tested with a ray.</summary>
    private void TickLabels()
    {
        if (content == null || ActualWidth <= 0) return;
        if (content.Labels.Count == 0)
        {
            if (labelViews.Count > 0) ClearLabels();
            return;
        }
        var q = orientation;
        var sig = $"{camera.Position}|{q}|{ActualWidth}x{ActualHeight}|{content.GetHashCode()}";
        var moved = sig != labelSignature;
        if (moved)
        {
            labelSignature = sig;
            stillSince = DateTime.UtcNow;
            raysDone = false;
        }
        var still = (DateTime.UtcNow - stillSince).TotalMilliseconds > 150;
        if (!moved && (raysDone || !still)) return;
        var inputs = new List<BioLabelInput>();
        foreach (var l in content.Labels)
        {
            var (x, y, front, dist) = Project(l.Anchor);
            var visible = front;
            // (one always shown is not tested for what covers it)
            if (visible && !l.Always && l.Normal is { } nrm)
            {
                var wn = turn.Transform(BioWpf.V(nrm));
                visible = Vector3D.DotProduct(wn, camera.Position - turn.Transform(BioWpf.P(l.Anchor))) > 0;
            }
            else if (visible && !l.Always)
            {
                if (still)
                {
                    var hit = ObjectAt(new Point(x, y));
                    rayVisible[l.Id] = hit is not { } h || BioSceneContent.Matches(h.Id, l.Id) || BioSceneContent.Matches(l.Id, h.Id) || h.Distance >= dist * 0.98;
                }
                visible = rayVisible.GetValueOrDefault(l.Id, true);
            }
            inputs.Add(new BioLabelInput(l.Id, l.Text, x, y, visible, l.Area, LabelWidth(l.Text)));
        }
        if (still) raysDone = true;
        // (the model's circle on the view: its framing ball's centre and edge, projected)
        var c = Project(framing.Centre);
        var r = ActualHeight / (2 * Tan20) * framing.Radius / Math.Max(distance, 1e-6);
        var clear = labelKeepClear.Width > 0 && labelKeepClear.Height > 0
            ? new BioLabelBox(0, ActualHeight - labelKeepClear.Height, labelKeepClear.Width, ActualHeight) : (BioLabelBox?)null;
        var (placed, more) = BioLabelLayout.Layout(inputs, c.X, c.Y, Math.Min(r, ActualWidth * 0.32), ActualWidth, ActualHeight, ModelBox(), clear);
        DrawLabels(placed, more);
    }

    /// <summary>The content as boxes: its items in runs (one item a run for up to 256 of them), each run's least and greatest
    /// x, y and z.</summary>
    private static List<(BioVec Min, BioVec Max)> Extents(BioSceneContent c)
    {
        var boxes = new List<(BioVec Min, BioVec Max)>();
        var items = c.Items.Where(i => i.Mesh.VertexCount > 0).ToList();
        var per = Math.Max(1, (items.Count + 255) / 256);
        for (var start = 0; start < items.Count; start += per)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
            for (var k = start; k < Math.Min(start + per, items.Count); k++)
                foreach (var q in items[k].Mesh.Positions)
                {
                    if (q.X < x0) x0 = q.X;
                    if (q.Y < y0) y0 = q.Y;
                    if (q.Z < z0) z0 = q.Z;
                    if (q.X > x1) x1 = q.X;
                    if (q.Y > y1) y1 = q.Y;
                    if (q.Z > z1) z1 = q.Z;
                }
            boxes.Add((new BioVec(x0, y0, z0), new BioVec(x1, y1, z1)));
        }
        return boxes;
    }

    /// <summary>The model's box on the view: the corners of its boxes where the turn and the camera put them (as Project
    /// does), their least and greatest x and y; null for an empty scene, or while a corner is behind the camera.</summary>
    private BioLabelBox? ModelBox()
    {
        if (extents.Count == 0) return null;
        var m = turn.Value;
        var eye = camera.Position;
        var f = ActualHeight / (2 * Tan20);
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var (lo, hi) in extents)
            for (var i = 0; i < 8; i++)
            {
                var w = m.Transform(new Point3D((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y, (i & 4) == 0 ? lo.Z : hi.Z));
                var v = w - eye;
                var z = -v.Z;
                if (z <= camera.NearPlaneDistance) return null;
                double x = ActualWidth / 2 + v.X * f / z, y = ActualHeight / 2 - v.Y * f / z;
                x0 = Math.Min(x0, x);
                y0 = Math.Min(y0, y);
                x1 = Math.Max(x1, x);
                y1 = Math.Max(y1, y);
            }
        return new BioLabelBox(Math.Max(x0, 0), Math.Max(y0, 0), Math.Min(x1, ActualWidth), Math.Min(y1, ActualHeight));
    }

    private void ClearLabels()
    {
        labelViews.Clear();
        labelLayer.Children.Clear();
        leaders.Data = null;
        moreText = null;
    }

    private void DrawLabels(List<BioLabelPlacement> placed, int more)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            foreach (var l in placed)
            {
                g.BeginFigure(new Point(l.AnchorX, l.AnchorY), false, false);
                foreach (var (x, y) in l.Leader) g.LineTo(new Point(x, y), true, true);
                // (the dot at the anchor)
                g.BeginFigure(new Point(l.AnchorX - 1.5, l.AnchorY), true, true);
                g.ArcTo(new Point(l.AnchorX + 1.5, l.AnchorY), new Size(1.5, 1.5), 0, false, SweepDirection.Clockwise, true, true);
                g.ArcTo(new Point(l.AnchorX - 1.5, l.AnchorY), new Size(1.5, 1.5), 0, false, SweepDirection.Clockwise, true, true);
            }
        }
        geo.Freeze();
        leaders.Data = geo;
        leaders.SetResourceReference(Shape.FillProperty, B.Text);
        var now = placed.Select(p => LabelKey(p.Id, p.Text)).ToHashSet();
        foreach (var gone in labelViews.Keys.Where(k => !now.Contains(k)).ToList())
        {
            var v = labelViews[gone];
            labelViews.Remove(gone);
            Motion.FadeOut(v, () => labelLayer.Children.Remove(v), 120);
        }
        foreach (var l in placed)
        {
            if (!labelViews.TryGetValue(LabelKey(l.Id, l.Text), out var v))
            {
                v = LabelView(l.Id, l.Text);
                labelViews[LabelKey(l.Id, l.Text)] = v;
                labelLayer.Children.Add(v);
                Motion.FadeIn(v, 120);
            }
            var f = l.Frame;
            v.Width = f.Width;
            v.Height = f.Height;
            Canvas.SetLeft(v, f.X);
            Canvas.SetTop(v, f.Y);
        }
        if (more > 0 && placed.Count > 0)
        {
            var last = placed.OrderBy(p => p.Y).Last();
            var words = $"+{more} more";
            if (moreText != null && moreWords != words)
            {
                labelLayer.Children.Remove(moreText);
                moreText = null;
            }
            if (moreText == null)
            {
                // (a link to the parts list where the tool has one, else words)
                if (MorePressed != null)
                {
                    var link = K.Link(words, () => MorePressed?.Invoke(), Ty.Caption);
                    link.ToolTip = "The parts without a label, in the parts list";
                    link.Padding = new Thickness(4, 1, 4, 2);
                    moreText = link;
                }
                else
                {
                    var text = K.Text(words, Ty.Caption, B.Text2);
                    text.IsHitTestVisible = false;
                    moreText = text;
                }
                moreWords = words;
                labelLayer.Children.Add(moreText);
            }
            Canvas.SetLeft(moreText, last.Frame.X + 6);
            Canvas.SetTop(moreText, last.Y + 11);
        }
        else if (moreText != null)
        {
            labelLayer.Children.Remove(moreText);
            moreText = null;
            moreWords = null;
        }
    }

    /// <summary>A label: its words in a capsule of the card's colour, edged in the accent while its object is under the
    /// pointer; a press picks its object.</summary>
    private FrameworkElement LabelView(string id, string text)
    {
        var words = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = K.TextFace, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        words.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
        var wash = new Border { CornerRadius = new CornerRadius(9), Opacity = 0.85 };
        wash.SetResourceReference(Border.BackgroundProperty, B.Solid);
        var edge = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Child = words, Padding = new Thickness(10, 0, 10, 0) };
        var face = new Grid { Children = { wash, edge } };
        var b = new Button { Content = face, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true, Tag = edge };
        b.SetResourceReference(FrameworkElement.StyleProperty, "SubtleButton");
        K.SetCorner(b, new CornerRadius(9));
        b.Click += (_, e) => { e.Handled = true; Pick(id); };
        // (a label laid out under a still pointer is not pointed at: only the pointer moving onto it)
        b.MouseEnter += (_, _) => { if (ScreenPointer.Moved(ref pointerAt)) SetHover(id); };
        b.MouseLeave += (_, _) => { if (hovered == id) SetHover(null); };
        b.ToolTip = text;
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        Ring(b, Lit(id));
        return b;
    }

    private static void Ring(FrameworkElement label, bool lit)
    {
        if (label is not Button { Tag: Border edge }) return;
        if (lit) edge.BorderBrush = new SolidColorBrush(Appearance.Accent);
        else edge.SetResourceReference(Border.BorderBrushProperty, B.CardStroke);
    }
}

/// <summary>The 3D view's card (spec §4.1 UI layout), the chemistry builder's: a top bar (the tool's title and badge at the
/// left; Turn and Reset view at the right, or what the tool puts there), the scene, and the hint under it.</summary>
public static class BioSceneCard
{
    public static Border Make(UIElement bar, BioSceneView scene, string hint = "Drag to turn · scroll to zoom · click a part to learn about it")
    {
        var foot = K.Text(hint, Ty.Caption, B.Text2);
        foot.HorizontalAlignment = HorizontalAlignment.Center;
        foot.Margin = new Thickness(12);
        if (bar is FrameworkElement fb) fb.Margin = new Thickness(16);
        return K.Card(K.Rows("auto, *, auto", bar.Row(0), scene.Row(1), foot.Row(2)), new Thickness(0));
    }

    /// <summary>The scene's "Turn" button (off by default; not there with Windows' animations off) and "Reset view".</summary>
    public static FrameworkElement Buttons(BioSceneView scene)
    {
        var row = K.H(8);
        if (Motion.On)
        {
            Wpf.Ui.Controls.Button? turnButton = null;
            void Dress()
            {
                if (turnButton == null) return;
                turnButton.Content = scene.Turning ? "Turning" : "Turn";
                turnButton.Appearance = scene.Turning ? ControlAppearance.Primary : ControlAppearance.Secondary;
                turnButton.ToolTip = scene.Turning ? "Stop the model turning by itself" : "Let the model turn by itself";
            }
            turnButton = K.Button("Turn", () => { scene.Turning = !scene.Turning; Dress(); }, icon: SymbolRegular.CubeRotate20);
            turnButton.MinWidth = 0;
            Dress();
            row.Children.Add(turnButton);
        }
        var reset = K.Button("Reset view", scene.ResetView, icon: SymbolRegular.ArrowCounterclockwise20);
        reset.MinWidth = 0;
        reset.ToolTip = "Put the camera back (or double-click empty space)";
        row.Children.Add(reset);
        return row;
    }
}
