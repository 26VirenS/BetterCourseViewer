using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Simpl.Kit;
using Simpl.Support;

namespace Simpl.Tools;

/// <summary>
/// The Biomolecule Builder's 3D view (spec §4.4, §3.4): the shared <see cref="BioSceneView"/> with what the builder adds
/// to it — a scene's marks (charges drawn as text facing the viewer; ring carbons' numbers drawn over the whole model as
/// the view's marks, so an atom in front never hides one), a scene asked for only when
/// its key is new (so a mark set made with one build is never lost to another), and the short motions the pages play
/// (a residue sliding in as water lifts off, a sugar split by water, ubiquitin unfolding). A motion's moving parts are
/// drawn over the scene, turning with it, frame by frame for as long as it runs; the scene under it is made at its end
/// state meanwhile, and the moving parts stay until that scene is shown, so nothing blinks. With Windows' animations
/// off nothing moves: the end state is shown at once.
/// </summary>
internal sealed class BiomolStage
{
    public BioSceneView View { get; }

    private string? asked;
    private int serial;
    private readonly ConditionalWeakTable<BioSceneContent, string> made = new();

    // (the motion layer: drawn after the scene, turning as it turns)
    private ModelVisual3D? layer;
    private readonly Model3DGroup layerGroup = new();
    private readonly List<(BiomolMotion.Group Group, Model3DGroup Holder, TranslateTransform3D Move, List<(GeometryModel3D Model, BioMaterial Material)> Parts)> playing = new();
    private BiomolMotion? motion;
    private double started;
    private string? holdFor;
    private double holdUntil;
    private Action? done;
    private bool ticking;
    // (a morph: matching meshes whose vertices move from one place to another)
    private List<(MeshGeometry3D Mesh, BioMesh From, BioMesh To)>? morph;
    private double morphSeconds;

    public BiomolStage(string accessibility)
    {
        View = new BioSceneView(accessibility) { PickSelects = true };
        View.Unloaded += (_, _) => Stop();
    }

    /// <summary>The scene for <paramref name="key"/>, made off the UI thread by <paramref name="build"/> (only when the key is
    /// new), with its marks; the camera frames it when <paramref name="frameKey"/> is new.</summary>
    public void Show(string key, Func<BiomolScene> build, string? frameKey)
    {
        if (key == asked) return;
        // (the scene shown already, but another asked for since and still being made: asked again under a new name, so
        // the view drops the other when it is done rather than showing it)
        var viewKey = Showing(key) ? $"{key}#{++serial}" : key;
        asked = key;
        var marks = new List<BiomolMark>();
        View.Show(viewKey, () =>
        {
            var s = build();
            lock (marks) marks.AddRange(s.Marks);
            lock (made) made.AddOrUpdate(s.Content, key);
            return s.Content;
        }, frameKey, () =>
        {
            lock (marks) return marks.Where(m => !m.OnTop).Select(Board).ToList();
        }, () =>
        {
            // (a carbon's number, or an amino acid's charge, over the whole model, so no atom in front of it hides it)
            lock (marks) return marks.Where(m => m.OnTop).Select(m => new BioMark(m.Text, m.Pos, m.Size, m.Atom, m.AtomRadius,
                m.Hex is { } h ? K.Hex(h) : (Color?)null, m.Charge)).ToList();
        });
    }

    /// <summary>A plain scene (no marks).</summary>
    public void Show(string key, Func<BioSceneContent> build, string? frameKey) => Show(key, () => new BiomolScene { Content = build() }, frameKey);

    /// <summary>Whether the scene shown is the one made for <paramref name="key"/>.</summary>
    public bool Showing(string key)
    {
        if (View.Content is not { } c) return false;
        lock (made) return made.TryGetValue(c, out var k) && k == key;
    }

    /// <summary>A mark as text facing the viewer, on its atom's near side; a charge's sign set to its atom's upper right on
    /// the view, just over the atom's edge, as a charge is written (N⁺, O⁻).</summary>
    private static BioBillboard Board(BiomolMark m)
    {
        var colour = m.Hex is { } h ? K.Hex(h) : Colors.White;
        var shift = m.Charge ? (m.AtomRadius + 0.3 * m.Size) * Math.Sqrt(0.5) : 0;
        return new BioBillboard(BioBillboards.Text(m.Text, m.Size, colour, glow: true), m.Pos, m.Lift, ShiftX: shift, ShiftY: shift);
    }

    // ---- motions ----

    /// <summary>Whether a motion can play here (Windows' animations on, the view up).</summary>
    public bool CanPlay => Motion.On && View.IsLoaded;

    /// <summary>
    /// A motion played over the scene: each group moved and faded as it says, frame by frame. When it has run,
    /// <paramref name="ended"/> is called (the page then asks for its end scene) and its last frame is held until the scene
    /// for <paramref name="endKey"/> is shown (two seconds at most), then cleared. A motion already playing is stopped first
    /// (its own <c>ended</c> called).
    /// </summary>
    public void Play(BiomolMotion m, string endKey, Action? ended = null)
    {
        Stop();
        if (!CanPlay || m.Groups.Count == 0 || !Attach()) { ended?.Invoke(); return; }
        motion = m;
        holdFor = endKey;
        done = ended;
        started = Now;
        holdUntil = double.MaxValue;
        foreach (var g in m.Groups)
        {
            var holder = new Model3DGroup();
            var move = new TranslateTransform3D();
            holder.Transform = move;
            var parts = new List<(GeometryModel3D, BioMaterial)>();
            foreach (var (material, _, batch) in BioWpf.Batches(g.Items))
                parts.Add((new GeometryModel3D(BioWpf.Mesh(batch.Mesh), BioWpf.Material(material)), material));
            playing.Add((g, holder, move, parts));
            layerGroup.Children.Add(holder);
        }
        Frame(0);
        Run(true);
    }

    /// <summary>
    /// Matching meshes morphed from one shape to another over <paramref name="seconds"/> (ubiquitin unfolding): the same
    /// vertices in the same order, each moved straight from where it was to where it goes, eased. Held at the end until
    /// the scene for <paramref name="endKey"/> is shown.
    /// </summary>
    public void Morph(IReadOnlyList<(string Hex, BioMesh From, BioMesh To)> parts, double seconds, string endKey, Action? ended = null)
    {
        Stop();
        if (!CanPlay || parts.Count == 0 || !Attach()) { ended?.Invoke(); return; }
        morph = new List<(MeshGeometry3D, BioMesh, BioMesh)>();
        foreach (var (hex, from, to) in parts)
        {
            var mesh = new MeshGeometry3D();
            var indices = new Int32Collection(from.Indices.Count);
            foreach (var i in from.Indices) indices.Add(i);
            indices.Freeze();
            mesh.TriangleIndices = indices;
            morph.Add((mesh, from, to));
            layerGroup.Children.Add(new GeometryModel3D(mesh, BioWpf.Material(new BioMaterial(hex, Roughness: 0.45))));
        }
        morphSeconds = seconds;
        holdFor = endKey;
        done = ended;
        started = Now;
        holdUntil = double.MaxValue;
        Frame(0);
        Run(true);
    }

    /// <summary>Whatever is playing stopped and cleared (a new motion, another page, the view gone); its <c>ended</c> called
    /// if it had not been.</summary>
    public void Stop()
    {
        Run(false);
        motion = null;
        morph = null;
        playing.Clear();
        layerGroup.Children.Clear();
        holdFor = null;
        Ended();
    }

    private void Ended()
    {
        var d = done;
        done = null;
        d?.Invoke();
    }

    public bool Playing => motion != null || morph != null;

    private static double Now => Environment.TickCount64 / 1000.0;

    /// <summary>The layer put in the view's 3D world beside the scene, turning with it (the view's own turn, found once).</summary>
    private bool Attach()
    {
        if (layer != null) return true;
        if (View.Children.OfType<Viewport3D>().FirstOrDefault() is not { } viewport) return false;
        // (the scene's world is the first model visual turned by a rotation: the view's turn, shared so the layer follows it)
        var turn = viewport.Children.OfType<ModelVisual3D>().Select(v => v.Transform).OfType<RotateTransform3D>().FirstOrDefault();
        if (turn == null) return false;
        layer = new ModelVisual3D { Content = layerGroup, Transform = turn };
        viewport.Children.Add(layer);
        return true;
    }

    private void Run(bool on)
    {
        if (on == ticking) return;
        ticking = on;
        if (on) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var t = Now - started;
        var length = motion?.Length ?? morphSeconds;
        Frame(Math.Min(t, length));
        if (t < length) return;
        // (run: the page told, so it asks for its end scene; held until that scene is shown)
        if (holdUntil == double.MaxValue)
        {
            holdUntil = Now + 2;
            Ended();
        }
        if ((holdFor != null && Showing(holdFor)) || Now >= holdUntil) Stop();
    }

    private void Frame(double t)
    {
        if (morph != null)
        {
            var u = BiomolEase.StrongOut(morphSeconds <= 0 ? 1 : t / morphSeconds);
            foreach (var (mesh, from, to) in morph)
            {
                var positions = new Point3DCollection(from.Positions.Count);
                var normals = new Vector3DCollection(from.Normals.Count);
                for (var i = 0; i < from.Positions.Count; i++)
                {
                    System.Numerics.Vector3 a = from.Positions[i], b = to.Positions[i];
                    positions.Add(new Point3D(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u, a.Z + (b.Z - a.Z) * u));
                    var n = System.Numerics.Vector3.Lerp(from.Normals[i], to.Normals[i], (float)u);
                    normals.Add(new Vector3D(n.X, n.Y, n.Z));
                }
                positions.Freeze();
                normals.Freeze();
                mesh.Positions = positions;
                mesh.Normals = normals;
            }
            return;
        }
        foreach (var (g, holder, move, parts) in playing)
        {
            var (offset, alpha) = BiomolMotion.At(g, t);
            move.OffsetX = offset.X;
            move.OffsetY = offset.Y;
            move.OffsetZ = offset.Z;
            // (opacity in twentieths, so the frozen materials are few; fully faded parts leave the layer, which would
            // otherwise still hide what is behind them)
            var fade = Math.Round(Math.Clamp(alpha, 0, 1) * 20) / 20;
            holder.Children.Clear();
            if (fade <= 0) continue;
            foreach (var (model, material) in parts)
            {
                var mat = BioWpf.Material(material, fade);
                if (!ReferenceEquals(model.Material, mat)) model.Material = mat;
                model.BackMaterial = fade < 1 || material.IsTranslucent ? mat : null;
                holder.Children.Add(model);
            }
        }
    }
}
