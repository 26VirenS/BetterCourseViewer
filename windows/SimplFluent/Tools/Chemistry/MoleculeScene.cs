using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Simpl.Support;

namespace Simpl.Tools;

/// <summary>
/// The molecule in 3D (the Mac's MoleculeSceneView, SceneKit there, WPF's Viewport3D here): its atoms as spheres in their
/// CPK colours, its bonds as one to three cylinders, its lone pairs as see-through yellow lobes, each atom's symbol facing
/// the viewer on the near side of its sphere (hidden, like the atom, behind nearer ones), and the selected atom ringed in
/// the accent, the ring always facing the viewer. Lit by a soft ambient light, a key light from the upper right and a
/// fill from behind. Dragged to turn, scrolled to zoom, an atom selected by a click; turning slowly by itself if asked,
/// waiting three seconds after the last touch. A new molecule is framed afresh.
/// </summary>
internal sealed class MoleculeSceneView : Border
{
    private static readonly MeshGeometry3D SphereMesh = Sphere(24, 36);
    private static readonly MeshGeometry3D CylinderMesh = Cylinder(20);
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Dictionary<(Color, double), Material> Materials = new();

    private readonly MoleculeModel model;
    private readonly Viewport3D view = new() { ClipToBounds = true };
    private readonly PerspectiveCamera camera = new()
    {
        Position = new Point3D(0, 0, 5), LookDirection = new Vector3D(0, 0, -1), UpDirection = new Vector3D(0, 1, 0),
        NearPlaneDistance = 0.05, FarPlaneDistance = 400, FieldOfView = 40,
    };
    private readonly ModelVisual3D scene = new(), marks = new();
    private readonly AxisAngleRotation3D yawTurn = new(new Vector3D(0, 1, 0), 0), pitchTurn = new(new Vector3D(1, 0, 0), 0);
    private readonly Transform3DGroup turn = new();
    /// <summary>What a press on a piece selects: the atom it is (its sphere, its label, its ring).</summary>
    private readonly Dictionary<Model3D, int> atomOf = new();
    /// <summary>What faces the viewer and follows its atom as the molecule turns: where it goes, its atom, how far it is lifted toward the viewer.</summary>
    private readonly List<(TranslateTransform3D Move, Vector3D At, double Lift)> follow = new();
    private double spin, yaw, pitch, distance = 5;
    private string drawn = "";
    private int framing = -1;
    private Point? downAt;
    private bool turning;
    private DateTime pausedUntil;
    private TimeSpan? lastFrame;
    private bool rendering;

    public MoleculeSceneView(MoleculeModel model)
    {
        this.model = model;
        Background = Brushes.Transparent;
        ClipToBounds = true;
        Focusable = false;
        view.Camera = camera;
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(0x61, 0x61, 0x61)));
        // (SceneKit's key at Euler angles (-0.6, 0.5, 0) and fill at (0.5, -2.4, 0), as directions)
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0xe0, 0xe0, 0xe0), new Vector3D(-0.48, -0.50, -0.73)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x59, 0x59, 0x59), new Vector3D(0.675, -0.353, 0.647)));
        view.Children.Add(new ModelVisual3D { Content = lights });
        turn.Children.Add(new RotateTransform3D(yawTurn));
        turn.Children.Add(new RotateTransform3D(pitchTurn));
        scene.Transform = turn;
        view.Children.Add(scene);
        view.Children.Add(marks);
        Child = view;
        System.Windows.Automation.AutomationProperties.SetName(this, "The molecule in 3D");

        void Changed() => Update();
        Loaded += (_, _) =>
        {
            model.Changed -= Changed;
            model.Changed += Changed;
            Appearance.Changed -= Changed;
            Appearance.Changed += Changed;
            Update();
        };
        Unloaded += (_, _) =>
        {
            model.Changed -= Changed;
            Appearance.Changed -= Changed;
            Rendering(false);
        };
        SizeChanged += (_, e) =>
        {
            // (SceneKit's 40° is the height's; WPF's field of view is the width's)
            if (e.NewSize.Height > 0)
                camera.FieldOfView = 2 * Math.Atan(Math.Tan(20 * Math.PI / 180) * e.NewSize.Width / e.NewSize.Height) * 180 / Math.PI;
        };
        Update();
    }

    // ---- the model drawn ----

    private void Update()
    {
        // (the atoms, bonds and lone pairs built again only when something they are made from has changed — not when the
        // turning goes on or off, or the facts beside it change)
        var now = Signature();
        if (now != drawn)
        {
            drawn = now;
            Build();
        }
        // (a new molecule: the camera back in front of it, far enough to see it all)
        if (framing != model.Framing)
        {
            framing = model.Framing;
            var extent = model.Atoms.Count == 0 ? 1 : model.Atoms.Max(a => a.Pos.Length);
            distance = Math.Max(3.6, extent * 2.3 + 2.4);
            yaw = pitch = spin = 0;
            Apply();
        }
        Rendering(model.AutoRotate && IsLoaded);
    }

    private string Signature()
    {
        var b = new StringBuilder();
        foreach (var a in model.Atoms) b.Append(a.Id).Append(a.Symbol).Append(P(a.Pos)).Append(';');
        b.Append('|');
        foreach (var x in model.Bonds) b.Append(x.Id).Append(':').Append(x.A).Append('-').Append(x.B).Append('x').Append(x.Order).Append(';');
        b.Append('|');
        foreach (var p in model.LonePairs) b.Append(p.Atom).Append(P(p.Pos)).Append(';');
        b.Append('|').Append(model.Style).Append(model.ShowLabels).Append(model.ShowLonePairs).Append(model.Selected).Append(Appearance.Accent);
        return b.ToString();
    }

    private static string P(Vector3D v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:0.###},{v.Y:0.###},{v.Z:0.###})");

    private static double Radius(string s, MBStyle style) => style switch
    {
        MBStyle.BallStick => 0.24 + 0.36 * MBChem.Covalent(s),
        MBStyle.SpaceFill => MBChem.Vdw(s) * 0.9,
        _ => 0.13,
    };

    private void Build()
    {
        atomOf.Clear();
        follow.Clear();
        var group = new Model3DGroup();
        var front = new Model3DGroup();
        var style = model.Style;
        foreach (var a in model.Atoms)
        {
            var r = Radius(a.Symbol, style);
            var t = new Transform3DGroup();
            t.Children.Add(new ScaleTransform3D(r, r, r));
            t.Children.Add(new TranslateTransform3D(a.Pos));
            var sphere = new GeometryModel3D(SphereMesh, Mat(MBChem.ColorOf(a.Symbol))) { Transform = t };
            group.Children.Add(sphere);
            atomOf[sphere] = a.Id;
            // (the selected atom ringed, the ring always facing the viewer)
            if (model.Selected == a.Id)
            {
                var ring = Ring(r * 1.18 + 0.04, Math.Max(0.018, r * 0.05), Simpl.Support.Appearance.Accent);
                Follow(ring, a.Pos, 0);
                front.Children.Add(ring);
                atomOf[ring] = a.Id;
            }
            if (model.ShowLabels && style != MBStyle.Sticks)
            {
                var label = Label(a.Symbol, Math.Max(0.32, r * 0.75), dark: a.Symbol is "H" or "F" or "Cl");
                Follow(label, a.Pos, r * 1.03);
                front.Children.Add(label);
                atomOf[label] = a.Id;
            }
        }
        if (style != MBStyle.SpaceFill)
        {
            var bondColor = Color.FromRgb(158, 158, 158);
            foreach (var b in model.Bonds)
            {
                if (model.Atom(b.A) is not { } x || model.Atom(b.B) is not { } y) continue;
                var d = y.Pos - x.Pos;
                var len = d.Length;
                if (len <= 1e-6) continue;
                var dir = d / len;
                var reference = Vector3D.CrossProduct(dir, new Vector3D(0, 0, 1));
                if (reference.Length < 0.1) reference = Vector3D.CrossProduct(dir, new Vector3D(0, 1, 0));
                reference.Normalize();
                var thick = style == MBStyle.Sticks ? 0.13 : (b.Order == 1 ? 0.09 : 0.06);
                var offsets = style == MBStyle.Sticks || b.Order == 1 ? new[] { 0.0 } : b.Order == 2 ? new[] { -0.11, 0.11 } : new[] { -0.16, 0, 0.16 };
                foreach (var o in offsets)
                {
                    var mid = (x.Pos + y.Pos) / 2 + reference * o;
                    var t = new Transform3DGroup();
                    t.Children.Add(new ScaleTransform3D(thick, len, thick));
                    t.Children.Add(Align(dir));
                    t.Children.Add(new TranslateTransform3D(mid));
                    group.Children.Add(new GeometryModel3D(CylinderMesh, Mat(style == MBStyle.Sticks ? MBChem.ColorOf(x.Symbol) : bondColor)) { Transform = t });
                }
            }
        }
        // (see-through last, over what is behind them)
        if (model.ShowLonePairs && style != MBStyle.SpaceFill)
        {
            var lobe = Mat(Color.FromRgb(0xff, 0xcc, 0x00), 0.42);
            foreach (var p in model.LonePairs)
            {
                if (model.Atom(p.Atom) is not { } a) continue;
                var d = p.Pos - a.Pos;
                var len = d.Length;
                if (len <= 1e-6) continue;
                var dir = d / len;
                var at = a.Pos + dir * (Radius(a.Symbol, style) + 0.22);
                var t = new Transform3DGroup();
                t.Children.Add(new ScaleTransform3D(0.2 * 0.7, 0.2 * 1.25, 0.2 * 0.7));
                t.Children.Add(Align(dir));
                t.Children.Add(new TranslateTransform3D(at));
                group.Children.Add(new GeometryModel3D(SphereMesh, lobe) { BackMaterial = lobe, Transform = t });
            }
        }
        scene.Content = group;
        marks.Content = front;
        Apply();
    }

    private void Follow(GeometryModel3D m, Vector3D at, double lift)
    {
        var move = new TranslateTransform3D();
        m.Transform = move;
        follow.Add((move, at, lift));
    }

    /// <summary>The molecule turned as it is now, and what faces the viewer moved with its atoms.</summary>
    private void Apply()
    {
        yawTurn.Angle = yaw + spin;
        pitchTurn.Angle = pitch;
        camera.Position = new Point3D(0, 0, distance);
        var m = turn.Value;
        foreach (var (move, at, lift) in follow)
        {
            var p = m.Transform(new Point3D(at.X, at.Y, at.Z));
            move.OffsetX = p.X;
            move.OffsetY = p.Y;
            move.OffsetZ = p.Z + lift;
        }
    }

    // ---- turning by itself ----

    private void Rendering(bool on)
    {
        if (on == rendering) return;
        rendering = on;
        lastFrame = null;
        if (on) CompositionTarget.Rendering += Frame;
        else CompositionTarget.Rendering -= Frame;
    }

    private void Frame(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs re) return;
        var now = re.RenderingTime;
        if (lastFrame is { } before)
        {
            var dt = (now - before).TotalSeconds;
            if (dt > 0 && dt < 1 && !turning && DateTime.UtcNow >= pausedUntil)
            {
                // (once round in fourteen seconds)
                spin = (spin + dt * 360 / 14) % 360;
                Apply();
            }
        }
        lastFrame = now;
    }

    /// <summary>The model handled: its turning paused, taken up again three seconds after the last touch.</summary>
    private void Handled()
    {
        pausedUntil = DateTime.UtcNow.AddSeconds(3);
        Chemistry.Tell("turned");
    }

    // ---- the pointer ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        downAt = e.GetPosition(this);
        turning = false;
        CaptureMouse();
        Handled();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (downAt is not { } from || !IsMouseCaptured) return;
        var at = e.GetPosition(this);
        if (!turning && (at - from).Length <= 3) return;
        turning = true;
        yaw += (at.X - from.X) * 0.45;
        pitch = Math.Clamp(pitch + (at.Y - from.Y) * 0.45, -90, 90);
        downAt = at;
        Apply();
        Handled();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (downAt == null) return;
        var click = !turning;
        downAt = null;
        turning = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
        if (click) Pick(e.GetPosition(view));
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        downAt = null;
        turning = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        distance = Math.Clamp(distance * Math.Pow(0.9, e.Delta / 120.0), 1.2, 80);
        Apply();
        Handled();
        e.Handled = true;
    }

    /// <summary>The atom under a click selected (the nearest thing hit; a bond or a lone pair selects nothing).</summary>
    private void Pick(Point at)
    {
        GeometryModel3D? hit = null;
        VisualTreeHelper.HitTest(view, null, r =>
        {
            if (r is RayMeshGeometry3DHitTestResult m && m.ModelHit is GeometryModel3D g)
            {
                hit = g;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(at));
        if (hit != null && atomOf.TryGetValue(hit, out var id)) model.Select(id);
    }

    // ---- pieces ----

    private static Material Mat(Color c, double alpha = 1)
    {
        if (Materials.TryGetValue((c, alpha), out var m)) return m;
        var diffuse = new DiffuseMaterial(new SolidColorBrush(c) { Opacity = alpha });
        if (alpha < 1) m = diffuse;
        else
        {
            var g = new MaterialGroup();
            g.Children.Add(diffuse);
            g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff)), 40));
            m = g;
        }
        m.Freeze();
        Materials[(c, alpha)] = m;
        return m;
    }

    /// <summary>A turn that points the y axis (a cylinder's, a lobe's) along a direction.</summary>
    private static Transform3D Align(Vector3D dir)
    {
        var y = new Vector3D(0, 1, 0);
        var axis = Vector3D.CrossProduct(y, dir);
        var angle = Vector3D.AngleBetween(y, dir);
        if (axis.Length < 1e-9) axis = new Vector3D(1, 0, 0);
        return new RotateTransform3D(new AxisAngleRotation3D(axis, angle));
    }

    /// <summary>A ring round an atom, flat toward the viewer, in a colour lighting does not change: the selected atom's mark.</summary>
    private static GeometryModel3D Ring(double radius, double pipe, Color color)
    {
        const int seg = 72;
        var mesh = new MeshGeometry3D();
        double ro = radius + pipe, ri = radius - pipe;
        for (var j = 0; j <= seg; j++)
        {
            var a = j * 2 * Math.PI / seg;
            mesh.Positions.Add(new Point3D(ri * Math.Cos(a), ri * Math.Sin(a), 0));
            mesh.Positions.Add(new Point3D(ro * Math.Cos(a), ro * Math.Sin(a), 0));
            mesh.Normals.Add(new Vector3D(0, 0, 1));
            mesh.Normals.Add(new Vector3D(0, 0, 1));
        }
        for (var j = 0; j < seg; j++)
        {
            int i0 = 2 * j, o0 = 2 * j + 1, i1 = 2 * j + 2, o1 = 2 * j + 3;
            mesh.TriangleIndices.Add(i0); mesh.TriangleIndices.Add(o0); mesh.TriangleIndices.Add(o1);
            mesh.TriangleIndices.Add(i0); mesh.TriangleIndices.Add(o1); mesh.TriangleIndices.Add(i1);
        }
        mesh.Freeze();
        var m = new MaterialGroup();
        m.Children.Add(new DiffuseMaterial(Brushes.Black));
        m.Children.Add(new EmissiveMaterial(new SolidColorBrush(color)));
        m.Freeze();
        return new GeometryModel3D(mesh, m);
    }

    /// <summary>An atom's symbol, flat toward the viewer: white on most atoms, dark on the pale ones.</summary>
    private static GeometryModel3D Label(string s, double size, bool dark)
    {
        var colour = dark ? new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f)) : Brushes.White;
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, 100, colour, 1);
        var geo = ft.BuildGeometry(new Point(0, 0));
        var bounds = geo.Bounds.IsEmpty ? new Rect(0, 0, 60, 70) : geo.Bounds;
        var brush = new DrawingBrush(new GeometryDrawing(colour, null, geo)) { Stretch = Stretch.Fill };
        brush.Freeze();
        double w = bounds.Width / 100 * size, h = bounds.Height / 100 * size;
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(-w / 2, -h / 2, 0));
        mesh.Positions.Add(new Point3D(w / 2, -h / 2, 0));
        mesh.Positions.Add(new Point3D(w / 2, h / 2, 0));
        mesh.Positions.Add(new Point3D(-w / 2, h / 2, 0));
        foreach (var _ in Enumerable.Range(0, 4)) mesh.Normals.Add(new Vector3D(0, 0, 1));
        mesh.TextureCoordinates.Add(new Point(0, 1));
        mesh.TextureCoordinates.Add(new Point(1, 1));
        mesh.TextureCoordinates.Add(new Point(1, 0));
        mesh.TextureCoordinates.Add(new Point(0, 0));
        foreach (var i in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(i);
        mesh.Freeze();
        var m = new MaterialGroup();
        m.Children.Add(new DiffuseMaterial(brush));
        if (!dark) m.Children.Add(new EmissiveMaterial(brush));
        m.Freeze();
        return new GeometryModel3D(mesh, m);
    }

    /// <summary>A sphere of radius one, its faces toward the outside.</summary>
    private static MeshGeometry3D Sphere(int lat, int lon)
    {
        var mesh = new MeshGeometry3D();
        for (var i = 0; i <= lat; i++)
        {
            var theta = i * Math.PI / lat;
            double y = Math.Cos(theta), r = Math.Sin(theta);
            for (var j = 0; j <= lon; j++)
            {
                var phi = j * 2 * Math.PI / lon;
                var p = new Point3D(r * Math.Cos(phi), y, r * Math.Sin(phi));
                mesh.Positions.Add(p);
                mesh.Normals.Add(new Vector3D(p.X, p.Y, p.Z));
            }
        }
        for (var i = 0; i < lat; i++)
        {
            for (var j = 0; j < lon; j++)
            {
                var a = i * (lon + 1) + j;
                var b = a + lon + 1;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b);
            }
        }
        mesh.Freeze();
        return mesh;
    }

    /// <summary>A tube of radius one along y, from −½ to ½ (its ends hidden in the atoms).</summary>
    private static MeshGeometry3D Cylinder(int seg)
    {
        var mesh = new MeshGeometry3D();
        for (var j = 0; j <= seg; j++)
        {
            var phi = j * 2 * Math.PI / seg;
            double x = Math.Cos(phi), z = Math.Sin(phi);
            mesh.Positions.Add(new Point3D(x, -0.5, z));
            mesh.Positions.Add(new Point3D(x, 0.5, z));
            mesh.Normals.Add(new Vector3D(x, 0, z));
            mesh.Normals.Add(new Vector3D(x, 0, z));
        }
        for (var j = 0; j < seg; j++)
        {
            int b0 = 2 * j, t0 = 2 * j + 1, b1 = 2 * j + 2, t1 = 2 * j + 3;
            mesh.TriangleIndices.Add(b0); mesh.TriangleIndices.Add(t0); mesh.TriangleIndices.Add(b1);
            mesh.TriangleIndices.Add(t0); mesh.TriangleIndices.Add(t1); mesh.TriangleIndices.Add(b1);
        }
        mesh.Freeze();
        return mesh;
    }
}
