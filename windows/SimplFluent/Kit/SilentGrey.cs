using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Support;

namespace Simpl.Kit;

/// <summary>
/// (0.3.86) Silent mode across the app, as the web's (2.99.127) and the Mac's (1.3.192): while it is on, every window of
/// Simpl's — the main one, a place in a window of its own, a popup, a tool's, the file preview — is laid out as it was, in
/// black and white. WPF draws a window's content through <see cref="Grey"/>, a small pixel shader (ps_2_0, compiled once
/// by Windows' own compiler, as <see cref="SolidEdge"/>); a web view draws in a window of its own, out of WPF's reach, so
/// its page is greyed by the page itself (a filter on its root). A window or a web view that comes up later is greyed as
/// it shows. The engine's own web view, never on show, is left as it is.
/// </summary>
public static class SilentGrey
{
    private static bool on;
    private static bool watching;
    private static readonly List<WeakReference<WebView2>> webs = new();

    /// <summary>The engine's own web view (left as it is).</summary>
    public static WebView2? Engine { get; set; }

    /// <summary>Every window and web view as Silent mode is now, and each one that shows after.</summary>
    public static void Follow(bool now)
    {
        on = now;
        if (!watching)
        {
            watching = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => { if (s is Window w) Paint(w); }));
            EventManager.RegisterClassHandler(typeof(WebView2), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => { if (s is WebView2 v) Watch(v); }));
        }
        foreach (var w in Application.Current?.Windows.OfType<Window>() ?? Enumerable.Empty<Window>()) Paint(w);
        webs.RemoveAll(r => !r.TryGetTarget(out _));
        foreach (var r in webs.ToList()) if (r.TryGetTarget(out var v)) _ = Page(v);
        Log.Write($"silent mode: the app in black and white: {(on ? "yes" : "no")}");
    }

    /// <summary>A window's content through the grey (or its own again); a content with an effect of its own is left.</summary>
    private static void Paint(Window w)
    {
        if (w.Content is not UIElement root || !Grey.Available) return;
        if (on && root.Effect == null) root.Effect = new Grey();
        else if (!on && root.Effect is Grey) root.Effect = null;
    }

    /// <summary>A web view watched: its page greyed now and on each page it goes to.</summary>
    private static void Watch(WebView2 v)
    {
        if (ReferenceEquals(v, Engine) || webs.Any(r => r.TryGetTarget(out var x) && ReferenceEquals(x, v))) return;
        webs.Add(new WeakReference<WebView2>(v));
        void Ready()
        {
            if (v.CoreWebView2 is { } core) core.NavigationCompleted += (_, _) => _ = Page(v);
            _ = Page(v);
        }
        if (v.CoreWebView2 != null) Ready();
        else v.CoreWebView2InitializationCompleted += (_, e) => { if (e.IsSuccess) Ready(); };
    }

    /// <summary>The page in the web view greyed, or its own again.</summary>
    private static async Task Page(WebView2 v)
    {
        try
        {
            if (v.CoreWebView2 == null) return;
            await v.CoreWebView2.ExecuteScriptAsync(on
                ? "document.documentElement && document.documentElement.style.setProperty('filter', 'grayscale(1)')"
                : "document.documentElement && document.documentElement.style.removeProperty('filter')");
        }
        catch { /* gone, or a page that takes no script (a PDF in Edge's viewer) */ }
    }

    /// <summary>The colours taken out: each pixel its own lightness (premultiplied, so its alpha is kept).</summary>
    public sealed class Grey : ShaderEffect
    {
        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(Grey), 0);

        private static readonly Lazy<PixelShader?> Shader = new(Compile);

        /// <summary>Whether it can be drawn here (its shader compiled).</summary>
        public static bool Available => Shader.Value != null;

        public Grey()
        {
            PixelShader = Shader.Value;
            UpdateShaderValue(InputProperty);
        }

        public Brush Input
        {
            get => (Brush)GetValue(InputProperty);
            set => SetValue(InputProperty, value);
        }

        private const string Source = @"
sampler2D input : register(s0);
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 c = tex2D(input, uv);
    float l = dot(c.rgb, float3(0.2126, 0.7152, 0.0722));
    return float4(l, l, l, c.a);
}";

        private static PixelShader? Compile()
        {
            try
            {
                var src = System.Text.Encoding.ASCII.GetBytes(Source);
                var hr = D3DCompile(src, (IntPtr)src.Length, "SilentGrey", IntPtr.Zero, IntPtr.Zero, "main", "ps_2_0", 0, 0, out var code, out var errors);
                if (hr < 0 || code == null)
                {
                    var why = errors == null ? $"0x{hr:x8}" : Marshal.PtrToStringAnsi(errors.GetBufferPointer(), (int)errors.GetBufferSize());
                    Log.Write("silent grey: the shader did not compile: " + why);
                    return null;
                }
                var bytes = new byte[(int)code.GetBufferSize()];
                Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length);
                var shader = new PixelShader();
                shader.SetStreamSource(new MemoryStream(bytes));
                shader.Freeze();
                return shader;
            }
            catch (Exception e)
            {
                Log.Write("silent grey: " + e.Message);
                return null;
            }
        }

        [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ID3DBlob
        {
            [PreserveSig] IntPtr GetBufferPointer();
            [PreserveSig] IntPtr GetBufferSize();
        }

        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern int D3DCompile(byte[] srcData, IntPtr srcDataSize, string sourceName, IntPtr defines, IntPtr include,
            string entryPoint, string target, uint flags1, uint flags2, out ID3DBlob? code, out ID3DBlob? errorMsgs);
    }
}
