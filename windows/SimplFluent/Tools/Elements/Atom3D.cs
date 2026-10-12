using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Simpl.Kit;
using Simpl.Support;

namespace Simpl.Tools;

// An element in 3D, for its page in the Periodic Table (the Mac's Atom3D.swift, SceneKit there; WPF's Viewport3D here):
// its atom — the nucleus's protons and neutrons, each shell a ring with its electrons going round — or the shapes of a
// subshell's orbitals, each with its electrons; turned by dragging, zoomed by the scroll wheel, and slowly turning on its
// own (the nucleus spinning, the electrons going round, the orbitals revolving).

/// <summary>
/// The 3D view of an element (the Mac's Atom3DView): its atom or a subshell's orbitals, lit from above left, on nothing (the
/// card shows through). Drag turns it, the wheel zooms; it moves on its own while Windows' animation effects are on.
/// </summary>
internal sealed class Atom3DView : Border
{
    private readonly Viewport3D view = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly PerspectiveCamera camera = new()
    {
        LookDirection = new Vector3D(0, 0, -1), UpDirection = new Vector3D(0, 1, 0), NearPlaneDistance = 0.05, FarPlaneDistance = 200,
    };
    private readonly ModelVisual3D holder = new();
    private readonly QuaternionRotation3D turnRotation = new();
    private Quaternion turn = Quaternion.Identity;
    private double distance = 6, baseDistance = 6;
    private string key = "";
    private AtomScene.Built? scene;
    private bool hooked;
    private TimeSpan? last;
    private Point? dragFrom;

    public Atom3DView()
    {
        Background = Brushes.Transparent;
        Child = view;
        Cursor = Cursors.SizeAll;
        view.Camera = camera;
        view.Children.Add(holder);
        SizeChanged += (_, _) => Lens();
        Loaded += (_, _) => Hook();
        Unloaded += (_, _) => Unhook();
        IsVisibleChanged += (_, _) => { if (IsVisible) Hook(); else Unhook(); };
        MouseLeftButtonDown += (_, e) =>
        {
            dragFrom = e.GetPosition(this);
            CaptureMouse();
            e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            var (dx, dy) = (p.X - from.X, p.Y - from.Y);
            dragFrom = p;
            if (Math.Abs(dx) + Math.Abs(dy) < 0.01) return;
            var delta = new Quaternion(new Vector3D(0, 1, 0), dx * 0.5) * new Quaternion(new Vector3D(1, 0, 0), dy * 0.5);
            turn = delta * turn;
            turn.Normalize();
            turnRotation.Quaternion = turn;
            scene?.Sort(camera.Position);
        };
        MouseLeftButtonUp += (_, _) => { dragFrom = null; ReleaseMouseCapture(); };
        LostMouseCapture += (_, _) => dragFrom = null;
        MouseWheel += (_, e) =>
        {
            distance = Math.Clamp(distance * Math.Pow(0.9985, e.Delta), baseDistance * 0.35, baseDistance * 2.5);
            camera.Position = new Point3D(0, 0, distance);
            scene?.Sort(camera.Position);
            e.Handled = true;
        };
        ToolTip = "Drag to turn · scroll to zoom";
        System.Windows.Automation.AutomationProperties.SetName(this, "The atom in 3D");
    }

    /// <summary>The element's atom, or a subshell's orbitals (built again only when one of them changes).</summary>
    public void Show(PTElement e, bool orbitals, PTSubshell? subshell)
    {
        var k = $"{e.Number}-{orbitals}-{subshell?.Id}";
        if (k == key) return;
        key = k;
        scene = orbitals ? AtomScene.Orbitals(subshell ?? new PTSubshell(1, 0, 1), turnRotation) : AtomScene.Atom(e, turnRotation);
        holder.Content = scene.Root;
        baseDistance = distance = scene.Distance;
        camera.Position = new Point3D(0, 0, distance);
        scene.Sort(camera.Position);
        System.Windows.Automation.AutomationProperties.SetName(this, orbitals && subshell is { } s ? $"The {s.Name} orbitals of {e.Name} in 3D" : $"{e.Name}’s atom in 3D");
    }

    /// <summary>SceneKit's 38° is the view's height; WPF's lens is measured across its width.</summary>
    private void Lens()
    {
        if (ActualHeight <= 0) return;
        var aspect = ActualWidth / ActualHeight;
        camera.FieldOfView = 2 * Math.Atan(Math.Tan(19 * Math.PI / 180) * aspect) * 180 / Math.PI;
    }

    private void Hook()
    {
        if (hooked || !IsLoaded || !IsVisible || !Motion.On) return;
        hooked = true;
        last = null;
        CompositionTarget.Rendering += Frame;
    }

    private void Unhook()
    {
        if (!hooked) return;
        hooked = false;
        CompositionTarget.Rendering -= Frame;
    }

    private void Frame(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs r || scene == null) return;
        if (last is { } l)
        {
            var dt = (r.RenderingTime - l).TotalSeconds;
            if (dt <= 0) return;
            if (dt < 0.5)
            {
                foreach (var (rot, perSecond) in scene.Spins) rot.Angle = (rot.Angle + perSecond * dt) % 360;
                scene.Sort(camera.Position);
            }
        }
        last = r.RenderingTime;
    }
}

/// <summary>The atom's and the orbitals' scenes (the Mac's AtomScene): their parts, materials, lights and motion.</summary>
internal static class AtomScene
{
    public static readonly Color ProtonColor = K.Hex("#ff5a4f");
    public static readonly Color NeutronColor = K.Hex("#c7c7cc");
    public static readonly Color ElectronColor = K.Hex("#64d2ff");

    /// <summary>A scene made: its models (lights, then the solid parts, then the see-through ones), how far back the camera
    /// stands, what turns on its own (degrees a second), and the see-through parts kept drawn farthest first.</summary>
    public sealed class Built
    {
        public required Model3DGroup Root { get; init; }
        public required double Distance { get; init; }
        public List<(AxisAngleRotation3D Rotation, double PerSecond)> Spins { get; } = new();
        public Model3DGroup? Glass { get; init; }
        public Transform3D? Frame { get; init; }
        private readonly List<(GeometryModel3D Model, Point3D Centre)> centres = new();

        public void AddGlass(GeometryModel3D m)
        {
            Glass?.Children.Add(m);
            centres.Add((m, m.Transform?.Transform(new Point3D()) ?? new Point3D()));
        }

        /// <summary>WPF draws see-through parts in their order and does not sort them: the farthest from the camera first,
        /// so each nearer one blends over it.</summary>
        public void Sort(Point3D camera)
        {
            if (Glass == null || centres.Count < 2) return;
            var m = Frame?.Value ?? Matrix3D.Identity;
            var order = centres.OrderByDescending(c => (m.Transform(c.Centre) - camera).LengthSquared).Select(c => c.Model).ToList();
            var same = true;
            for (var i = 0; i < order.Count && same; i++) same = ReferenceEquals(Glass.Children[i], order[i]);
            if (same) return;
            Glass.Children.Clear();
            foreach (var g in order) Glass.Children.Add(g);
        }
    }

    // ---- materials, lights, shapes ----

    private static readonly Dictionary<(Color, int, bool), Material> Materials = new();

    /// <summary>A surface of a colour: lit, with a soft shine; see-through when its alpha is under one (both sides drawn);
    /// glowing a little (the electrons).</summary>
    public static Material Mat(Color c, double alpha = 1, bool glow = false)
    {
        var a = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 255);
        if (Materials.TryGetValue((c, a, glow), out var have)) return have;
        var g = new MaterialGroup();
        g.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb((byte)a, c.R, c.G, c.B))));
        if (glow) g.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb((byte)a, (byte)(c.R * 0.45), (byte)(c.G * 0.45), (byte)(c.B * 0.45)))));
        g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb((byte)(a * 0.45), 255, 255, 255)), 36));
        g.Freeze();
        Materials[(c, a, glow)] = g;
        return g;
    }

    /// <summary>The lights: an even ambient and a key light from above left, in front (the Mac's 420 and 900).</summary>
    private static Model3DGroup Lit(Model3DGroup content)
    {
        var all = new Model3DGroup();
        all.Children.Add(new AmbientLight(Color.FromRgb(118, 118, 118)));
        all.Children.Add(new DirectionalLight(Color.FromRgb(228, 228, 228), new Vector3D(-0.43, -0.64, -0.63)));
        all.Children.Add(content);
        return all;
    }

    private static readonly Dictionary<(double, int), MeshGeometry3D> Spheres = new();

    /// <summary>A sphere of a radius, so many segments round (SceneKit's segmentCount).</summary>
    public static MeshGeometry3D Sphere(double r, int segments)
    {
        if (Spheres.TryGetValue((r, segments), out var have)) return have;
        var m = new MeshGeometry3D();
        int slices = segments, stacks = Math.Max(6, segments / 2);
        for (var i = 0; i <= stacks; i++)
        {
            var phi = Math.PI * i / stacks;
            double y = Math.Cos(phi), ring = Math.Sin(phi);
            for (var j = 0; j <= slices; j++)
            {
                var theta = 2 * Math.PI * j / slices;
                var n = new Vector3D(ring * Math.Cos(theta), y, ring * Math.Sin(theta));
                m.Positions.Add(new Point3D(n.X * r, n.Y * r, n.Z * r));
                m.Normals.Add(n);
            }
        }
        for (var i = 0; i < stacks; i++)
            for (var j = 0; j < slices; j++)
            {
                int a = i * (slices + 1) + j, b = a + slices + 1;
                m.TriangleIndices.Add(a); m.TriangleIndices.Add(a + 1); m.TriangleIndices.Add(b);
                m.TriangleIndices.Add(a + 1); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b);
            }
        m.Freeze();
        Spheres[(r, segments)] = m;
        return m;
    }

    /// <summary>A ring round the y axis, lying flat (SceneKit's SCNTorus).</summary>
    public static MeshGeometry3D Torus(double ringRadius, double pipeRadius, int ringSegments, int pipeSegments)
    {
        var m = new MeshGeometry3D();
        for (var i = 0; i <= ringSegments; i++)
        {
            var u = 2 * Math.PI * i / ringSegments;
            var centre = new Point3D(ringRadius * Math.Cos(u), 0, ringRadius * Math.Sin(u));
            for (var j = 0; j <= pipeSegments; j++)
            {
                var v = 2 * Math.PI * j / pipeSegments;
                var n = new Vector3D(Math.Cos(v) * Math.Cos(u), Math.Sin(v), Math.Cos(v) * Math.Sin(u));
                m.Positions.Add(centre + n * pipeRadius);
                m.Normals.Add(n);
            }
        }
        for (var i = 0; i < ringSegments; i++)
            for (var j = 0; j < pipeSegments; j++)
            {
                int a = i * (pipeSegments + 1) + j, b = a + pipeSegments + 1;
                m.TriangleIndices.Add(a); m.TriangleIndices.Add(a + 1); m.TriangleIndices.Add(b);
                m.TriangleIndices.Add(a + 1); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b);
            }
        m.Freeze();
        return m;
    }

    private static GeometryModel3D Model(MeshGeometry3D mesh, Material mat, Transform3D? at = null, bool twoSided = false) =>
        new(mesh, mat) { BackMaterial = twoSided ? mat : null, Transform = at ?? Transform3D.Identity };

    private static RotateTransform3D Rotate(Vector3D axis, double radians) => new(new AxisAngleRotation3D(axis, radians * 180 / Math.PI));

    /// <summary>SceneKit's euler angles: roll (z), then yaw (y), then pitch (x).</summary>
    private static Transform3D Euler(double x, double y, double z)
    {
        var g = new Transform3DGroup();
        g.Children.Add(Rotate(new Vector3D(0, 0, 1), z));
        g.Children.Add(Rotate(new Vector3D(0, 1, 0), y));
        g.Children.Add(Rotate(new Vector3D(1, 0, 0), x));
        g.Freeze();
        return g;
    }

    /// <summary>Something turning round an axis on its own, so many seconds a turn.</summary>
    private static RotateTransform3D Spin(Built b, Vector3D axis, double degreesPerSecond)
    {
        var r = new AxisAngleRotation3D(axis, 0);
        b.Spins.Add((r, degreesPerSecond));
        return new RotateTransform3D(r);
    }

    private static Color RingColor => Appearance.Dark ? Colors.White : Color.FromRgb(0x3c, 0x3c, 0x43);

    // ---- the atom ----

    /// <summary>The atom: its nucleus (protons red, neutrons gray — at most 72 drawn, in their proportion) and each shell a
    /// tilted ring with its electrons, going round, the outer ones slower.</summary>
    public static Built Atom(PTElement e, QuaternionRotation3D turn)
    {
        var shells = e.Shells.Count == 0 ? new List<int> { e.Number } : e.Shells.ToList();
        var outer = 0.9 + (shells.Count - 1) * 0.55;
        var solid = new Model3DGroup();
        var glass = new Model3DGroup();
        var content = new Model3DGroup { Children = { solid, glass }, Transform = new RotateTransform3D(turn) };
        var b = new Built { Root = Lit(content), Distance = outer * 2.9 + 1.2, Glass = glass, Frame = content.Transform };

        var total = e.Number + e.Neutrons;
        var count = Math.Min(total, 72);
        var protonShare = (double)e.Number / Math.Max(total, 1);
        var nucleusRadius = 0.12 * Math.Cbrt(count) + 0.05;
        var ball = Sphere(0.085, 16);
        Material proton = Mat(ProtonColor), neutron = Mat(NeutronColor);
        var nucleus = new Model3DGroup();
        var protonsSoFar = 0.0;
        for (var i = 0; i < count; i++)
        {
            // (spread through a ball: a golden-angle spiral, each a little further out)
            var t = (i + 0.5) / count;
            var r = nucleusRadius * Math.Cbrt(t);
            var y = 1 - 2 * t;
            var ring = Math.Sqrt(Math.Max(0, 1 - y * y));
            var phi = i * 2.399963;
            var isProton = (i + 1) * protonShare - protonsSoFar >= 0.5;
            if (isProton) protonsSoFar += 1;
            nucleus.Children.Add(Model(ball, isProton ? proton : neutron, new TranslateTransform3D(r * ring * Math.Cos(phi), r * y, r * ring * Math.Sin(phi))));
        }
        var nt = new Transform3DGroup();
        nt.Children.Add(Spin(b, new Vector3D(1, 0, 0), 0.2 * 180 / Math.PI / 24));
        nt.Children.Add(Spin(b, new Vector3D(0, 1, 0), 360.0 / 24));
        nucleus.Transform = nt;
        solid.Children.Add(nucleus);

        var electron = Sphere(0.055, 12);
        var electronMat = Mat(ElectronColor, glow: true);
        var ringMat = Mat(RingColor, 0.28);
        for (var i = 0; i < shells.Count; i++)
        {
            var n = shells[i];
            var r = 0.9 + i * 0.55;
            var tilt = Euler(0.35 + i * 0.47, i * 0.9, 0.2 * (i % 2 == 0 ? 1 : -1));
            glass.Children.Add(Model(Torus(r, 0.006, 96, 6), ringMat, tilt, twoSided: true));
            var orbit = new Model3DGroup();
            for (var k = 0; k < n; k++)
            {
                var a = (double)k / n * 2 * Math.PI;
                orbit.Children.Add(Model(electron, electronMat, new TranslateTransform3D(r * Math.Cos(a), 0, r * Math.Sin(a))));
            }
            var ot = new Transform3DGroup();
            ot.Children.Add(Spin(b, new Vector3D(0, 1, 0), 360 / (3 + i * 1.6)));
            ot.Children.Add(tilt);
            orbit.Transform = ot;
            solid.Children.Add(orbit);
        }
        return b;
    }

    // ---- orbitals ----

    public static string[] OrbitalNames(int l) => l switch
    {
        0 => new[] { "s" },
        1 => new[] { "px", "py", "pz" },
        2 => new[] { "dxy", "dxz", "dyz", "dx²−y²", "dz²" },
        _ => new[] { "fz³", "fxz²", "fyz²", "fxyz", "fz(x²−y²)", "fx(x²−3y²)", "fy(3x²−y²)" },
    };

    /// <summary>A lobe's directions for each orbital of a subshell (an orbital as lobes pointing out from the nucleus; rings
    /// for dz²'s and fz³'s collars). The f shapes are drawn simply.</summary>
    private static (Vector3D[] Dirs, double[] Rings) Lobes(int l, int i)
    {
        const double s = 0.7071;
        Vector3D V(double x, double y, double z) => new(x, y, z);
        switch (l)
        {
            case 1:
                var axis = new[] { V(1, 0, 0), V(0, 1, 0), V(0, 0, 1) };
                return (new[] { axis[i], -axis[i] }, Array.Empty<double>());
            case 2:
                return i switch
                {
                    0 => (new[] { V(s, s, 0), V(-s, -s, 0), V(s, -s, 0), V(-s, s, 0) }, Array.Empty<double>()),
                    1 => (new[] { V(s, 0, s), V(-s, 0, -s), V(s, 0, -s), V(-s, 0, s) }, Array.Empty<double>()),
                    2 => (new[] { V(0, s, s), V(0, -s, -s), V(0, s, -s), V(0, -s, s) }, Array.Empty<double>()),
                    3 => (new[] { V(1, 0, 0), V(-1, 0, 0), V(0, 1, 0), V(0, -1, 0) }, Array.Empty<double>()),
                    _ => (new[] { V(0, 0, 1), V(0, 0, -1) }, new[] { 0.0 }),
                };
            case 3:
                const double c = 0.577;
                return i switch
                {
                    0 => (new[] { V(0, 0, 1), V(0, 0, -1) }, new[] { 0.35, -0.35 }),
                    1 => (new[] { V(0.55, 0, 0.83), V(-0.55, 0, 0.83), V(0.55, 0, -0.83), V(-0.55, 0, -0.83), V(1, 0, 0), V(-1, 0, 0) }, Array.Empty<double>()),
                    2 => (new[] { V(0, 0.55, 0.83), V(0, -0.55, 0.83), V(0, 0.55, -0.83), V(0, -0.55, -0.83), V(0, 1, 0), V(0, -1, 0) }, Array.Empty<double>()),
                    3 => (new[] { V(c, c, c), V(-c, -c, c), V(c, -c, -c), V(-c, c, -c), V(-c, c, c), V(c, -c, c), V(c, c, -c), V(-c, -c, -c) }, Array.Empty<double>()),
                    4 => (new[] { V(s, 0, s), V(-s, 0, s), V(s, 0, -s), V(-s, 0, -s), V(0, s, s), V(0, -s, s), V(0, s, -s), V(0, -s, -s) }, Array.Empty<double>()),
                    5 => (Enumerable.Range(0, 6).Select(k => { var a = k * Math.PI / 3; return V(Math.Cos(a), Math.Sin(a), 0); }).ToArray(), Array.Empty<double>()),
                    _ => (Enumerable.Range(0, 6).Select(k => { var a = k * Math.PI / 3 + Math.PI / 6; return V(Math.Cos(a), Math.Sin(a), 0); }).ToArray(), Array.Empty<double>()),
                };
            default:
                return (Array.Empty<Vector3D>(), Array.Empty<double>());
        }
    }

    /// <summary>The turn that points a lobe's long axis (its y) along a direction.</summary>
    private static Transform3D Aim(Vector3D d)
    {
        d.Normalize();
        var axis = Vector3D.CrossProduct(new Vector3D(0, 1, 0), d);
        if (axis.Length < 1e-6) return d.Y > 0 ? Transform3D.Identity : new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 180));
        return new RotateTransform3D(new AxisAngleRotation3D(axis, Math.Acos(Math.Clamp(d.Y, -1, 1)) * 180 / Math.PI));
    }

    /// <summary>A subshell's orbitals side by side, each its lobes (the two phases in two colours; an empty orbital faint),
    /// the row slowly revolving.</summary>
    public static Built Orbitals(PTSubshell sub, QuaternionRotation3D turn)
    {
        var names = OrbitalNames(sub.L);
        const double gap = 2.6;
        var width = (names.Length - 1) * gap;
        // (far enough back that an s orbital's sphere, or the row of p, d or f orbitals, sits well inside the view)
        var sRadius = 0.45 + 0.12 * sub.N;
        var solid = new Model3DGroup();
        var glass = new Model3DGroup();
        var content = new Model3DGroup { Children = { solid, glass } };
        var frame = new Transform3DGroup();
        var b = new Built { Root = Lit(content), Distance = Math.Max(5.4, Math.Max(width * 0.95 + 4.6, sub.L == 0 ? sRadius * 6.2 : 0)), Glass = glass, Frame = frame };
        frame.Children.Add(Spin(b, new Vector3D(0, 1, 0), 360.0 / 40));
        frame.Children.Add(new RotateTransform3D(turn));
        content.Transform = frame;
        var filling = sub.Filling;
        Color plus = K.Hex("#0a84ff"), minus = K.Hex("#ff9f0a");
        for (var i = 0; i < names.Length; i++)
        {
            var x = i * gap - width / 2;
            var filled = i < filling.Length ? filling[i] : 0;
            var alpha = filled == 0 ? 0.16 : filled == 1 ? 0.55 : 0.8;
            // (the nucleus, a dot at the centre)
            solid.Children.Add(Model(Sphere(0.06, 12), Mat(ProtonColor), new TranslateTransform3D(x, 0, 0)));
            if (sub.L == 0)
            {
                b.AddGlass(Model(Sphere(sRadius, 48), Mat(plus, alpha), new TranslateTransform3D(x, 0, 0), twoSided: true));
                continue;
            }
            var (dirs, rings) = Lobes(sub.L, i);
            var lobeLength = sub.L == 1 ? 0.75 : 0.62;
            for (var k = 0; k < dirs.Length; k++)
            {
                var d = dirs[k];
                var at = new Transform3DGroup();
                at.Children.Add(new ScaleTransform3D(0.5, lobeLength, 0.5));
                at.Children.Add(Aim(d));
                var p = d * (lobeLength * 0.92);
                at.Children.Add(new TranslateTransform3D(p.X + x, p.Y, p.Z));
                at.Freeze();
                // (the phases alternate round the orbital)
                b.AddGlass(Model(Sphere(0.5, 32), Mat(k % 2 == 0 ? plus : minus, alpha), at, twoSided: true));
            }
            foreach (var z in rings)
            {
                var at = new Transform3DGroup();
                at.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 90)));
                at.Children.Add(new TranslateTransform3D(x, 0, z));
                at.Freeze();
                b.AddGlass(Model(Torus(0.42, 0.12, 48, 16), Mat(minus, alpha), at, twoSided: true));
            }
        }
        return b;
    }
}
