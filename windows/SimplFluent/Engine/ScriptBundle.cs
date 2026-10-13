using System.IO;
using System.Text.Json.Nodes;
using Simpl.Support;

namespace Simpl.Engine;

/// <summary>
/// The bundled <c>extension/</c> folder as WebView2 scripts, mirroring the manifest's content_scripts as the Mac's
/// ScriptBundle does: the native channel, the bridge (bridge.js, which stands in for the browser-extension APIs), the
/// background scripts (they answer the page's messages from inside the same page), then each content script at its
/// run_at, and the stylesheet.
///
/// WebView2 runs a script added with AddScriptToExecuteOnDocumentCreated in every frame, in the page's own world, before
/// anything else on the page. So each script here is wrapped: the main frame only, the school's host only (single
/// sign-on pages on other hosts are left alone), and a document_end script deferred to DOMContentLoaded (WebKit's
/// atDocumentEnd). WebKit's message handler (webkit.messageHandlers.bcv, which answers with a promise) is the native
/// channel's here, handed to bridge.js and login.js as a local <c>webkit</c> — never put on the page's window.
/// </summary>
public static class ScriptBundle
{
    public static string ExtensionDir => Path.Combine(AppPaths.AppDir, "extension");
    public static string WebDir => Path.Combine(AppPaths.AppDir, "Web");

    public static JsonObject Manifest()
    {
        try { return JsonNode.Parse(System.IO.File.ReadAllText(Path.Combine(ExtensionDir, "manifest.json"))) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    public static string File(string relativePath)
    {
        try { return System.IO.File.ReadAllText(Path.Combine(ExtensionDir, relativePath.Replace('/', Path.DirectorySeparatorChar))); }
        catch { return ""; }
    }

    private static string WebFile(string name)
    {
        try { return System.IO.File.ReadAllText(Path.Combine(WebDir, name)); } catch { return ""; }
    }

    /// <summary>
    /// The native channel, first of all: <c>__simplNative.handler(name).postMessage(msg)</c> resolves with the app's
    /// answer (an id per message, the answer back through <c>reply</c>), and <c>__simplCall</c> runs an engine call
    /// (BCVNative.call) for the app, its answer posted back as text. Main frame only.
    /// </summary>
    public const string NativeChannel = """
    (function () {
      if (window.top !== window || window.__simplNative) return;
      var wv = window.chrome && window.chrome.webview;
      var pending = new Map(), next = 0;
      function handler(name) {
        return {
          postMessage: function (msg) {
            if (!wv) return Promise.reject(new Error('Simpl: no app underneath'));
            var id = ++next;
            return new Promise(function (resolve, reject) {
              pending.set(id, { resolve: resolve, reject: reject });
              try { wv.postMessage({ kind: 'msg', id: id, name: name, msg: msg }); } catch (e) { pending.delete(id); reject(e); }
            });
          }
        };
      }
      var api = {
        handler: handler,
        reply: function (id, ok, value) {
          var p = pending.get(id); if (!p) return; pending.delete(id);
          if (ok) p.resolve(value === undefined ? null : value); else p.reject(new Error(String(value || 'Simpl: failed')));
        }
      };
      Object.defineProperty(window, '__simplNative', { value: api, configurable: false, writable: false });
      Object.defineProperty(window, '__simplCall', {
        configurable: false, writable: false,
        value: function (callId, name, args) {
          Promise.resolve().then(function () {
            if (!window.BCVNative) throw new Error('not ready');
            return window.BCVNative.call(name, args || {});
          }).then(function (r) {
            wv.postMessage({ kind: 'call', id: callId, ok: true, text: JSON.stringify(r === undefined ? null : r) });
          }, function (e) {
            wv.postMessage({ kind: 'call', id: callId, ok: false, text: String((e && e.message) || e) });
          });
        }
      });
    })();
    """;

    /// <summary>The scripts for the school at <paramref name="host"/>, in order.</summary>
    public static List<string> Scripts(string host, bool shellOn)
    {
        var manifest = Manifest();
        var hostLit = Json.Str(host.ToLowerInvariant());
        var guardJs = $"if(window.top!==window||location.hostname.toLowerCase()!=={hostLit})return;";
        var scripts = new List<string> { NativeChannel };
        var seen = new HashSet<string>();
        const string webkitLocal = "var webkit={messageHandlers:{bcv:window.__simplNative.handler('bcv'),bcvLogin:window.__simplNative.handler('bcvLogin')}};";

        string AtEnd(string body) =>
            $"(function(){{{guardJs}var go=function(){{{body}\n}};if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',go,{{once:true}});else go();}})();";

        // A script WebView2 runs as a document is created runs before the document has its root (<html>), where a
        // browser runs an extension's document_start script with the root there: one that touches the page is run as
        // soon as the root is there (at once when it already is), before anything of the page's own — or its first
        // touch of document.documentElement fails (the screenshot suite's page-errors.log said so, every start).
        string AtRoot(string body) =>
            $"(function(){{{guardJs}var go=function(){{{body}\n}};if(document.documentElement)go();else{{var o=new MutationObserver(function(){{if(document.documentElement){{o.disconnect();go();}}}});o.observe(document,{{childList:true}});}}}})();";

        void Add(string path, bool atStart, bool page = false)
        {
            if (!seen.Add(path)) return;
            var body = $"try{{\n{File(path)}\n}}catch(e){{console.error('[Simpl Courses] {path} failed',e)}}";
            var wrapped = !atStart ? AtEnd(body) : page ? AtRoot(body) : $"(function(){{{guardJs}{body}\n}})();";
            scripts.Add(wrapped + $"\n//# sourceURL=simpl-courses/{path}");
        }

        // 1. the bridge that stands in for the browser-extension APIs, with the manifest baked in
        var bridge = WebFile("bridge.js")
            .Replace("__SHELL__", shellOn ? "on" : "off")
            .Replace("__MANIFEST__", Json.Literal(manifest));
        scripts.Add($"(function(){{{guardJs}{webkitLocal}\n{bridge}\n}})();\n//# sourceURL=simpl-courses/bridge.js");

        // the page is the app's screen: the viewport fixed, no zoom (as on the Mac)
        const string viewport = "width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no, viewport-fit=cover";
        var vpLit = Json.Str(viewport);

        // 2. the background script and what it needs, in the manifest's order
        var background = (manifest["background"]?["scripts"] as JsonArray)?.Select(n => n?.ToString() ?? "").Where(s => s.Length > 0).ToList()
            ?? new List<string> { "lib/settings.js", "lib/devcode.js", "background.js" };
        foreach (var path in background) Add(path, true);

        // 3. the manifest's content scripts and stylesheet
        var css = new List<string>();
        foreach (var entry in (manifest["content_scripts"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var js = (entry["js"] as JsonArray)?.Select(n => n?.ToString() ?? "").ToList() ?? new List<string>();
            if (js.Contains("content/sniff.js")) continue; // (the browsers' finder of a school's Canvas: the school is chosen in the app)
            var atStart = entry["run_at"]?.ToString() == "document_start";
            foreach (var path in js) Add(path, atStart, page: true);
            foreach (var path in (entry["css"] as JsonArray)?.Select(n => n?.ToString() ?? "") ?? Enumerable.Empty<string>()) css.Add(File(path));
        }
        var early = new List<string>
        {
            AtRoot($"var m=document.querySelector('meta[name=viewport]');if(!m){{m=document.createElement('meta');m.name='viewport';(document.head||document.documentElement).appendChild(m);}}m.content={vpLit};"),
        };
        if (css.Count > 0)
        {
            // before first paint, then moved to the end of <body> so it wins ties with Canvas's own stylesheets
            early.Add(AtRoot($"var s=document.createElement('style');s.id='bcv-css';s.textContent={Json.Str(string.Join("\n", css))};(document.head||document.documentElement).appendChild(s);"));
            scripts.Add(AtEnd("var s=document.getElementById('bcv-css');if(s&&document.body)document.body.appendChild(s);"));
        }
        scripts.InsertRange(2, early);
        scripts.Add(AtEnd($"var m=document.querySelector('meta[name=viewport]');if(m)m.content={vpLit};"));
        // Canvas's own login form: "Stay signed in" starts checked, so the session outlives the app being closed
        scripts.Add(AtEnd("if(location.pathname.indexOf('/login')!==0)return;var r=document.getElementById('pseudonym_session_remember_me');if(r&&!r.checked){r.checked=true;}"));
        return scripts;
    }

    /// <summary>
    /// Web/login.js, the sign-in reader, for every page the engine opens (the Mac's loginScript): the main frame of any
    /// host — a school's single sign-on is on a host of its own — with the Canvas host filled in, and its messages to
    /// the app through <c>bcvLogin</c>. WebView2 has no world of its own for it (WebKit's SimplLogin world), so what the
    /// app calls is kept where the page cannot swap it: <c>window.__simplLogin</c>, neither writable nor configurable,
    /// and the reader's own name on the window taken away again.
    /// </summary>
    public static string LoginScript(string host)
    {
        var src = WebFile("login.js").Replace("__CANVAS_HOST__", Json.Str(host.ToLowerInvariant()));
        return "(function(){if(window.top!==window||!window.__simplNative)return;var webkit={messageHandlers:{bcvLogin:window.__simplNative.handler('bcvLogin')}};try{\n"
            + src
            + "\n}catch(e){console.error('[Simpl Courses] login.js failed',e)}var api=self.SimplLogin;if(api){try{delete self.SimplLogin;}catch(e){}try{Object.defineProperty(window,'__simplLogin',{value:api,writable:false,configurable:false,enumerable:false});}catch(e){}}})();\n//# sourceURL=simpl-courses/login.js";
    }

    /// <summary>What a student who has used the app has in storage (the Mac's demoSeed): the setup done, the first-run
    /// pointers seen, this version's notes read.</summary>
    public static JsonObject DemoSeed()
    {
        var flow = 3;
        var m = System.Text.RegularExpressions.Regex.Match(File("background.js"), @"const SETUP_FLOW = (\d+)");
        if (m.Success) flow = int.Parse(m.Groups[1].Value);
        return new JsonObject
        {
            ["setup:offered"] = true, ["setup:done"] = true, ["welcome:search"] = true, ["tools:welcomed"] = true,
            ["setup:flow"] = flow, ["whatsnew:seen"] = ExtensionVersion,
        };
    }

    public static string ExtensionVersion => Manifest()["version"]?.ToString() ?? "";
}
