#pragma once
// The showcase: Simpl's Dashboard as the backdrop, and over it glass as Simpl for Windows will draw it — the Mac's
// "Unread announcements" popover, a toolbar with the Dashboard's Cards · List · Activity, a tab bar, switches and a
// slider whose knobs turn into lenses while held, buttons, a glass bubble to drag and fling, and Tune (the glass,
// live). Built afresh each frame (immediate mode), then drawn in layers:
//   1. the backdrop (Direct2D) → the frame, and the frost's small copies;
//   2. the panels' frosted glass (AuroraGlass: one frost a frame, each shape composited on the GPU);
//   3. what is on the panels: their tint and rim of light, words, icons, tracks (Direct2D);
//   4. the lenses — knobs, thumbs, the bubble — refracting the frame so far (AuroraGlass, no frost);
//   5. what is on the lenses (Direct2D).
#include "Gfx.h"
#include "Scene.h"
#include "Spring.h"
#include "core/glass_surface.h"
#include "core/glass_material.h"
#include <functional>
#include <vector>
#include <string>
#include <unordered_map>

using namespace AuroraGlass;

struct Showcase {
    Gfx& g;
    Scene scene;
    GlassSurface frost, lens;
    bool ready = false;

    // the glass, as Tune sets it
    float frostLevel = 0.62f;   // 0..1 → how small a copy of the backdrop the frost reads (0: full size … 1: 1/32)
    float refraction = 0.42f, dispersion = 0.25f, thickness = 0.55f, saturation = 1.45f, tintAmount = 0.42f, rim = 0.45f;

    // input (layout DIPs)
    float mx = -1, my = -1;
    bool down = false, pressed = false, released = false, consumed = false;
    std::string active;        // what holds the pointer
    float uiScale = 1;         // the whole showcase scaled down to fit a small window

    // where things are
    float popX = 34, popY = 104, ctlX = -1, ctlY = 96, tuneX = -1, tuneY = 414;
    float grabX = 0, grabY = 0;
    Spring bubbleX{ 640, 0.45f, 0.78f }, bubbleY{ 560, 0.45f, 0.78f };
    std::vector<std::pair<double, std::pair<float, float>>> samples;
    std::unordered_map<std::string, Spring> springs;
    bool toggles[4] = { true, false, true, true };
    float textSize = 0.42f;
    int view = 1, tab = 0;
    double now = 0;

    struct Shape { D2D1_RECT_F r; float radius; D2D1_COLOR_F tint; float tintAmount; bool interactive; float power = 1; };
    std::vector<Shape> frosts, lenses;
    std::vector<std::function<void()>> content, overlays;

    explicit Showcase(Gfx& gfx) : g(gfx) {}

    bool Init() {
        SurfaceDesc d{ g.width, g.height };
        ready = GlassSurface::Create(g.device.Get(), d, frost).ok() && GlassSurface::Create(g.device.Get(), d, lens).ok();
        return ready;
    }

    void Resized() {
        if (!ready) return;
        frost.Resize(g.width, g.height);
        lens.Resize(g.width, g.height);
    }

    Spring& S(const std::string& id, float v, float r = 0.34f, float d = 0.8f) {
        auto it = springs.find(id);
        if (it == springs.end()) it = springs.emplace(id, Spring(v, r, d)).first;
        return it->second;
    }

    // ---- small drawing helpers (Direct2D, layout DIPs) ----

    ID2D1SolidColorBrush* Brush(D2D1_COLOR_F c) {
        static ComPtr<ID2D1SolidColorBrush> b;
        if (!b) g.d2d->CreateSolidColorBrush(c, &b);
        b->SetColor(c);
        return b.Get();
    }

    D2D1_COLOR_F Ink(float a = 0.92f) const { return scene.dark ? D2D1::ColorF(1, 1, 1, a) : D2D1::ColorF(0, 0, 0, a * 0.95f); }
    D2D1_COLOR_F Ink2() const { return scene.dark ? D2D1::ColorF(0.92f, 0.92f, 0.96f, 0.6f) : D2D1::ColorF(0.24f, 0.24f, 0.26f, 0.62f); }

    void Text(const wchar_t* s, float x, float y, float w, float h, float size, D2D1_COLOR_F c,
              DWRITE_FONT_WEIGHT wt = DWRITE_FONT_WEIGHT_NORMAL, DWRITE_TEXT_ALIGNMENT align = DWRITE_TEXT_ALIGNMENT_LEADING,
              const wchar_t* face = L"Segoe UI Variable Text") {
        auto* f = g.Font(face, size, wt);
        f->SetTextAlignment(align);
        g.d2d->DrawText(s, (UINT32)wcslen(s), f, D2D1::RectF(x, y, x + w, y + h), Brush(c));
    }

    void Icon(const wchar_t* glyph, float x, float y, float w, float h, float size, D2D1_COLOR_F c) {
        Text(glyph, x, y, w, h, size, c, DWRITE_FONT_WEIGHT_NORMAL, DWRITE_TEXT_ALIGNMENT_CENTER, L"Segoe Fluent Icons");
    }

    bool Inside(D2D1_RECT_F r) const { return mx >= r.left && mx <= r.right && my >= r.top && my <= r.bottom; }

    // ---- the parts ----

    /// A frosted panel of glass (the Mac's popover glass): a shape of frost, its tint and rim drawn over it after.
    void Frost(D2D1_RECT_F r, float radius, bool interactive = false, D2D1_COLOR_F tint = { 0, 0, 0, -1 }, float amount = -1) {
        D2D1_COLOR_F t = tint.a < 0 ? (scene.dark ? Rgb(128, 112, 96) : Rgb(250, 248, 246)) : tint;
        frosts.push_back({ r, radius, t, amount < 0 ? tintAmount * (scene.dark ? 1.0f : 1.15f) : amount, interactive });
    }

    /// A lens of glass (a knob, a thumb, the bubble): no frost, what is under it bent through its rim.
    void Lens(D2D1_RECT_F r, float radius, float power = 1) { lenses.push_back({ r, radius, {}, 0, true, power }); }

    /// A panel that moves when dragged by anything but its controls.
    void Draggable(const std::string& id, D2D1_RECT_F r, float& x, float& y) {
        if (pressed && !consumed && Inside(r)) { active = id; grabX = mx - x; grabY = my - y; consumed = true; }
        if (active == id && down) { x = mx - grabX; y = my - grabY; }
    }

    /// Choices in a capsule, a lens sliding to the one chosen (Cards · List · Activity; the tab bar).
    void Segmented(const std::string& id, float x, float y, float segW, const std::vector<const wchar_t*>& items, int& selected, bool glassCapsule = true) {
        float h = 40, w = segW * items.size() + 8;
        D2D1_RECT_F r = D2D1::RectF(x, y, x + w, y + h);
        if (glassCapsule) Frost(r, h / 2, true);
        Spring& sx = S(id + ":x", (float)selected, 0.36f, 0.74f);
        Spring& st = S(id + ":s", 0, 0.3f, 0.7f);
        if (pressed && !consumed && Inside(r)) {
            int i = std::clamp((int)((mx - x - 4) / segW), 0, (int)items.size() - 1);
            if (i != selected) { selected = i; sx.To((float)i); st.To(1); }
            consumed = true;
        }
        if (st.value > 0.95f && st.target == 1) st.To(0);
        float s = st.value;
        float tw = segW - 2 + 18 * s, th = h - 8 + 4 * s;
        float tx = x + 4 + sx.value * segW - 9 * s + 1, ty = y + (h - th) / 2;
        content.push_back([=, this] {
            for (size_t i = 0; i < items.size(); i++)
                Text(items[i], x + 4 + i * segW, y, segW, h, 14.5f, Ink(i == (size_t)selected ? 0.98f : 0.82f), DWRITE_FONT_WEIGHT_SEMI_BOLD, DWRITE_TEXT_ALIGNMENT_CENTER);
        });
        // (nearly flat at rest — the words under it stay readable — bending fully while it slides, as the Mac's does)
        float moving = std::min(1.0f, std::fabs(sx.value - sx.target) * 1.6f + s);
        Lens(D2D1::RectF(tx, ty, tx + tw, ty + th), th / 2, 0.12f + 0.88f * moving);
        // a thin veil of light on the chosen one, as the Mac's thumb has
        overlays.push_back([=, this] {
            g.d2d->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(tx, ty, tx + tw, ty + th), th / 2, th / 2), Brush(scene.dark ? D2D1::ColorF(1, 1, 1, 0.1f) : D2D1::ColorF(1, 1, 1, 0.55f)));
        });
    }

    /// A switch: a track, and a knob that turns into a lens while held, then slides across on a spring.
    void Toggle(const std::string& id, float x, float y, bool& on) {
        float w = 56, h = 32;
        D2D1_RECT_F r = D2D1::RectF(x, y, x + w, y + h);
        Spring& p = S(id + ":p", on ? 1.0f : 0.0f, 0.32f, 0.72f);
        Spring& hold = S(id + ":h", 0, 0.26f, 0.82f);
        if (pressed && !consumed && Inside(r)) { active = id; hold.To(1); consumed = true; }
        if (active == id && released) { on = !on; p.To(on ? 1.0f : 0.0f); hold.To(0); }
        float hv = hold.value, t = std::clamp(p.value, 0.0f, 1.0f);
        float kw = 27 + 14 * hv, kh = 27 + 5 * hv;
        float kx = x + 2.5f + t * (w - 5 - kw), ky = y + (h - kh) / 2;
        content.push_back([=, this] {
            D2D1_COLOR_F off = scene.dark ? D2D1::ColorF(0.47f, 0.47f, 0.5f, 0.3f) : D2D1::ColorF(0.47f, 0.47f, 0.5f, 0.2f);
            D2D1_COLOR_F c = D2D1::ColorF(off.r + (52 / 255.f - off.r) * t, off.g + (199 / 255.f - off.g) * t, off.b + (89 / 255.f - off.b) * t, off.a + (1 - off.a) * t);
            g.d2d->FillRoundedRectangle(D2D1::RoundedRect(r, h / 2, h / 2), Brush(c));
        });
        Lens(D2D1::RectF(kx, ky, kx + kw, ky + kh), kh / 2);
        overlays.push_back([=, this] { Knob(kx, ky, kw, kh, 1 - 0.9f * hv); });
    }

    /// The white knob at rest (and its soft shadow), fading as it turns to glass.
    void Knob(float x, float y, float w, float h, float opacity) {
        if (opacity <= 0.01f) return;
        for (int i = 3; i >= 1; i--)
            g.d2d->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(x - i * 0.6f, y + 1.2f, x + w + i * 0.6f, y + h + i * 1.1f), h / 2 + i, h / 2 + i), Brush(D2D1::ColorF(0, 0, 0, 0.06f * opacity)));
        g.d2d->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(x, y, x + w, y + h), h / 2, h / 2), Brush(D2D1::ColorF(1, 1, 1, opacity)));
    }

    /// A slider: a thin track filled to its value, and a knob that becomes a lens while dragged.
    void Slider(const std::string& id, float x, float y, float w, float& v, D2D1_COLOR_F fill = Rgb(0, 122, 255)) {
        float h = 30;
        D2D1_RECT_F r = D2D1::RectF(x, y, x + w, y + h);
        Spring& hold = S(id + ":h", 0, 0.26f, 0.82f);
        Spring& p = S(id + ":p", v, 0.3f, 0.84f);
        if (pressed && !consumed && Inside(r)) { active = id; hold.To(1); consumed = true; v = std::clamp((mx - x - 14) / (w - 28), 0.0f, 1.0f); p.To(v); }
        if (active == id && down && !pressed) { v = std::clamp((mx - x - 14) / (w - 28), 0.0f, 1.0f); p.Snap(v); }
        if (active == id && released) hold.To(0);
        float hv = hold.value;
        float kw = 30 + 18 * hv, kh = 21 + 9 * hv;
        float cx = x + 14 + p.value * (w - 28);
        float kx = cx - kw / 2, ky = y + (h - kh) / 2;
        content.push_back([=, this] {
            float ty = y + h / 2 - 3;
            g.d2d->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(x + 4, ty, x + w - 4, ty + 6), 3, 3), Brush(scene.dark ? D2D1::ColorF(1, 1, 1, 0.16f) : D2D1::ColorF(0, 0, 0, 0.1f)));
            g.d2d->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(x + 4, ty, std::max(x + 10, cx), ty + 6), 3, 3), Brush(fill));
        });
        Lens(D2D1::RectF(kx, ky, kx + kw, ky + kh), kh / 2);
        overlays.push_back([=, this] { Knob(kx, ky, kw, kh, 1 - 0.9f * hv); });
    }

    /// A button in glass: a capsule of frost (tinted for the prominent one), its words on it; it brightens when pressed.
    bool Button(const std::string& id, float x, float y, float w, const wchar_t* label, bool prominent = false) {
        float h = 38;
        D2D1_RECT_F r = D2D1::RectF(x, y, x + w, y + h);
        Spring& pr = S(id + ":p", 0, 0.24f, 0.7f);
        bool clicked = false;
        if (pressed && !consumed && Inside(r)) { active = id; pr.To(1); consumed = true; }
        if (active == id && released) { pr.To(0); clicked = Inside(r); }
        float grow = pr.value * 3;
        D2D1_RECT_F gr = D2D1::RectF(x - grow, y - grow * 0.5f, x + w + grow, y + h + grow * 0.5f);
        if (prominent) Frost(gr, h / 2 + grow, true, Rgb(0, 122, 255), 0.7f); else Frost(gr, h / 2 + grow, true);
        content.push_back([=, this] {
            Text(label, x, y, w, h, 14.5f, prominent ? D2D1::ColorF(1, 1, 1, 1) : Ink(), DWRITE_FONT_WEIGHT_SEMI_BOLD, DWRITE_TEXT_ALIGNMENT_CENTER);
            if (pr.value > 0.01f) g.d2d->FillRoundedRectangle(D2D1::RoundedRect(gr, h / 2 + grow, h / 2 + grow), Brush(D2D1::ColorF(1, 1, 1, 0.08f * pr.value)));
        });
        return clicked;
    }

    /// Icon buttons sharing one capsule of glass (Back and Forward share one, as on the Mac).
    void IconCapsule(float x, float y, const std::vector<const wchar_t*>& glyphs) {
        float h = 40, w = 40.0f * glyphs.size() + 6;
        Frost(D2D1::RectF(x, y, x + w, y + h), h / 2, true);
        content.push_back([=, this] {
            for (size_t i = 0; i < glyphs.size(); i++) Icon(glyphs[i], x + 3 + i * 40, y, 40, h, 16, Ink());
        });
    }

    // ---- the showcase, a frame of it ----

    void Build(float W, float H) {
        frosts.clear(); lenses.clear(); content.clear(); overlays.clear();
        consumed = false;
        if (ctlX < 0) ctlX = W - 344 - 36;
        if (tuneX < 0) tuneX = W - 344 - 36;

        // the toolbar
        float tx = std::max(20.0f, (W - 940) / 2), ty = 18;
        IconCapsule(tx, ty, { L"", L"" });
        IconCapsule(tx + 98, ty, { L"" });
        content.push_back([=, this] {
            Text(L"Dashboard", tx + 160, ty, 160, 24, 16, Ink(), DWRITE_FONT_WEIGHT_SEMI_BOLD);
            Text(L"Liquid Glass", tx + 160, ty + 20, 160, 20, 12, Ink2());
        });
        Segmented("view", tx + 300, ty, 84, { L"Cards", L"List", L"Activity" }, view);
        IconCapsule(tx + 572, ty, { L"" });
        Frost(D2D1::RectF(tx + 630, ty, tx + 940, ty + 40), 20, true);
        content.push_back([=, this] {
            Icon(L"", tx + 644, ty, 24, 40, 14, Ink2());
            Text(L"Search", tx + 672, ty, 200, 40, 14.5f, Ink2());
        });

        Popover();
        Controls();
        Tune();

        // the tab bar
        float bw = 5 * 86 + 8;
        Segmented("tab", (W - bw) / 2, H - 78, 86, { L"Today", L"Courses", L"To Do", L"Grades", L"Search" }, tab);

        Bubble(W, H);
    }

    /// The Mac's popover, as screenshotted: Unread announcements.
    void Popover() {
        float x = popX, y = popY, w = 560, h = 560;
        D2D1_RECT_F r = D2D1::RectF(x, y, x + w, y + h);
        Frost(r, 34, true);
        content.push_back([=, this] {
            Icon(L"", x + 30, y + 28, 30, 36, 21, Rgb(255, 149, 0));
            Text(L"Unread announcements", x + 70, y + 28, 400, 36, 21, Ink(), DWRITE_FONT_WEIGHT_SEMI_BOLD, DWRITE_TEXT_ALIGNMENT_LEADING, L"Segoe UI Variable Display");
            Icon(L"", x + w - 58, y + 28, 30, 36, 15, Ink2());
            Text(L"0", x + 30, y + 74, 80, 70, 56, Ink(), DWRITE_FONT_WEIGHT_BOLD, DWRITE_TEXT_ALIGNMENT_LEADING, L"Segoe UI Variable Display");
            Text(L"All caught up", x + 92, y + 98, 300, 30, 18, Ink2());
            Text(L"Read recently", x + 44, y + 152, 300, 26, 15.5f, Ink2(), DWRITE_FONT_WEIGHT_SEMI_BOLD);
            const wchar_t* rows[][2] = {
                { L"This Week in WRI 010", L"Posted Oct 11 · read" },
                { L"Week 8 (Oct 12 - 16, 2026) announcement", L"Posted Oct 9 · read" },
                { L"Algebra workshop: Exponentials and Logarithms", L"Posted Oct 8 · read" },
                { L"Graders for Exam 1", L"Posted Oct 6 · read" },
                { L"Office hours moved to Thursday", L"Posted Oct 3 · read" },
            };
            float ry = y + 192;
            for (auto& row : rows) {
                g.d2d->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(x + 44, ry + 6, x + 48, ry + 58), 2, 2), Brush(Rgb(196, 134, 64, 0.85f)));
                Text(row[0], x + 66, ry + 2, w - 130, 30, 18.5f, Ink(0.64f));
                Text(row[1], x + 66, ry + 32, w - 130, 26, 15.5f, Ink2());
                Icon(L"", x + w - 54, ry + 14, 20, 30, 12, Ink2());
                ry += 72;
            }
        });
        Draggable("pop", r, popX, popY);
    }

    void Controls() {
        float x = ctlX, y = ctlY, w = 344, h = 316;
        D2D1_RECT_F r = D2D1::RectF(x, y, x + w, y + h);
        Frost(r, 30, true);
        content.push_back([=, this] { Text(L"Controls", x + 24, y + 18, 200, 32, 20, Ink(), DWRITE_FONT_WEIGHT_SEMI_BOLD, DWRITE_TEXT_ALIGNMENT_LEADING, L"Segoe UI Variable Display"); });
        const wchar_t* labels[] = { L"Hide completed", L"Reminders", L"Beta updates" };
        for (int i = 0; i < 3; i++) {
            float ry = y + 62 + i * 46;
            content.push_back([=, this] { Text(labels[i], x + 24, ry, 220, 32, 15.5f, Ink()); });
            Toggle("t" + std::to_string(i), x + w - 24 - 56, ry, toggles[i]);
        }
        content.push_back([=, this] { Text(L"Text size", x + 24, y + 200, 200, 20, 13, Ink2()); });
        Slider("size", x + 18, y + 220, w - 36, textSize);
        Button("cancel", x + 24, y + 262, 120, L"Cancel");
        Button("handin", x + 154, y + 262, 130, L"Hand In", true);
        Draggable("ctl", r, ctlX, ctlY);
    }

    void Tune() {
        float x = tuneX, y = tuneY, w = 344, h = 384;
        D2D1_RECT_F r = D2D1::RectF(x, y, x + w, y + h);
        Frost(r, 30, true);
        content.push_back([=, this] { Text(L"Tune", x + 24, y + 16, 120, 36, 20, Ink(), DWRITE_FONT_WEIGHT_SEMI_BOLD, DWRITE_TEXT_ALIGNMENT_LEADING, L"Segoe UI Variable Display"); });
        int mode = scene.dark ? 0 : 1;
        Segmented("mode", x + w - 24 - 146, y + 14, 69, { L"Dark", L"Light" }, mode, false);
        scene.dark = mode == 0;
        struct K { const wchar_t* name; float* v; float lo, hi; };
        float sat = (saturation - 0.5f) / 1.5f;
        K knobs[] = {
            { L"Frost", &frostLevel, 0, 1 }, { L"Refraction", &refraction, 0, 1 }, { L"Dispersion", &dispersion, 0, 1 },
            { L"Thickness", &thickness, 0, 1 }, { L"Colour", &sat, 0, 1 }, { L"Tint", &tintAmount, 0, 0.9f }, { L"Rim", &rim, 0, 1 },
        };
        float ry = y + 64;
        int index = 0;
        for (auto& k : knobs) {
            float shown = *k.v;
            if (k.v == &sat) shown = saturation;
            wchar_t val[16];
            swprintf(val, 16, L"%.2f", shown);
            std::wstring valS = val;
            const wchar_t* name = k.name;
            content.push_back([=, this] {
                Text(name, x + 24, ry, 90, 30, 13.5f, Ink());
                Text(valS.c_str(), x + w - 64, ry, 40, 30, 12, Ink2(), DWRITE_FONT_WEIGHT_NORMAL, DWRITE_TEXT_ALIGNMENT_TRAILING);
            });
            float f = (*k.v - k.lo) / (k.hi - k.lo);
            Slider("knob" + std::to_string(index++), x + 112, ry, w - 112 - 70, f);
            *k.v = k.lo + f * (k.hi - k.lo);
            ry += 38;
        }
        saturation = 0.5f + sat * 1.5f;
        content.push_back([=, this] { Text(L"Moving backdrop", x + 24, ry + 2, 200, 32, 13.5f, Ink()); });
        Toggle("moving", x + w - 24 - 56, ry + 2, scene.moving);
        Draggable("tune", r, tuneX, tuneY);
    }

    /// The bubble: drag it, fling it — it heads where its momentum takes it, inside the window, on a spring.
    void Bubble(float W, float H) {
        float d = 150;
        D2D1_RECT_F r = D2D1::RectF(bubbleX.value, bubbleY.value, bubbleX.value + d, bubbleY.value + d);
        if (pressed && !consumed && Inside(r)) { active = "bubble"; grabX = mx - bubbleX.value; grabY = my - bubbleY.value; samples.clear(); consumed = true; }
        if (active == "bubble" && down) {
            bubbleX.Snap(mx - grabX);
            bubbleY.Snap(my - grabY);
            samples.push_back({ now, { mx, my } });
            if (samples.size() > 6) samples.erase(samples.begin());
        }
        if (active == "bubble" && released) {
            float vx = 0, vy = 0;
            if (samples.size() >= 2) {
                double dt = std::max(samples.back().first - samples.front().first, 0.008);
                vx = (float)((samples.back().second.first - samples.front().second.first) / dt);
                vy = (float)((samples.back().second.second - samples.front().second.second) / dt);
            }
            bubbleX.To(std::clamp(bubbleX.value + Spring::Project(vx), 10.0f, W - d - 10), vx);
            bubbleY.To(std::clamp(bubbleY.value + Spring::Project(vy), 10.0f, H - d - 10), vy);
        }
        lenses.push_back({ r, d / 2, {}, -1, true }); // (tintAmount -1: the bubble's own, thicker lens)
    }

    // ---- drawing a frame ----

    GlassMaterial Material(float radiusPx, const Shape& s, bool isLens) const {
        GlassMaterial m;
        float cx = (s.r.left + s.r.right) / 2, cy = (s.r.top + s.r.bottom) / 2;
        float hw = std::max((s.r.right - s.r.left) / 2, 1.0f), hh = std::max((s.r.bottom - s.r.top) / 2, 1.0f);
        float hx = std::clamp((mx - cx) / hw * 0.6f, -1.0f, 1.0f), hy = std::clamp((my - cy) / hh * 0.6f, -1.0f, 1.0f);
        if (mx < 0) { hx = -0.5f; hy = -0.7f; }
        m.SetCornerRadius(std::min(radiusPx, 200.0f));
        m.SetHighlightPosition(hx, hy);
        m.SetTintAmount(0.0f);
        m.SetNoiseAmount(isLens ? 0.0f : 0.012f);
        m.SetOpacity(1.0f);
        if (isLens) {
            bool bubble = s.tintAmount < 0;
            m.SetBlurRadius(0.0f);
            m.SetRefractionStrength(bubble ? 1.0f : std::min(1.0f, (refraction + 0.25f) * s.power));
            m.SetThickness(bubble ? 1.0f : 0.4f + 0.35f * s.power);
            m.SetDispersionStrength(bubble ? std::min(1.0f, dispersion + 0.35f) : dispersion);
            m.SetEdgeFresnel(0.35f + 0.4f * rim);
            m.SetSpecularStrength(0.4f + 1.2f * rim);
            m.SetSaturation(std::min(2.0f, 0.85f + saturation * 0.25f));
            m.SetBrightness(1.04f);
        } else {
            m.SetBlurRadius(24.0f);
            m.SetRefractionStrength(refraction);
            m.SetThickness(thickness);
            m.SetDispersionStrength(dispersion);
            m.SetEdgeFresnel(0.2f + 0.5f * rim);
            m.SetSpecularStrength(0.3f + 1.0f * rim);
            m.SetSaturation(std::clamp(saturation, 0.0f, 2.0f));
            m.SetBrightness(1.0f);
        }
        return m;
    }

    D2D1_RECT_F Px(D2D1_RECT_F r) const {
        float k = g.Scale() * uiScale;
        return D2D1::RectF(r.left * k, r.top * k, r.right * k, r.bottom * k);
    }

    void Draw(float dt) {
        now += dt;
        float k = g.Scale();
        float W = g.width / k / uiScale, H = g.height / k / uiScale;
        for (auto& [id, s] : springs) s.Step(dt);
        bubbleX.Step(dt);
        bubbleY.Step(dt);
        Build(W, H);
        if (released) active.clear();
        pressed = released = false;

        auto* d = g.d2d.Get();
        D2D1_MATRIX_3X2_F scale = D2D1::Matrix3x2F::Scale(uiScale, uiScale);

        // 1. the backdrop
        d->SetTarget(g.sceneBitmap.Get());
        d->BeginDraw();
        d->SetTransform(scale);
        scene.Draw(g, W, H, dt);
        d->EndDraw();
        g.PublishScene();

        // the panels' soft shadows, under the glass
        d->SetTarget(g.backBitmap.Get());
        d->BeginDraw();
        d->SetTransform(scale);
        for (auto& s : frosts) {
            if (s.r.bottom - s.r.top < 100) continue;
            for (int i = 1; i <= 7; i++) {
                float e = i * 3.0f;
                d->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(s.r.left - e + 4, s.r.top - e + 12, s.r.right + e - 4, s.r.bottom + e + 6), s.radius + e, s.radius + e),
                    Brush(D2D1::ColorF(0, 0, 0, (scene.dark ? 0.05f : 0.022f) * (8 - i) / 7)));
            }
        }
        d->EndDraw();

        D3D11_VIEWPORT vp = { 0, 0, (float)g.width, (float)g.height, 0, 1 };
        // 2. the frosted glass: one frost of a small copy of the backdrop, each panel composited
        if (ready) {
            UINT level = std::min<UINT>((UINT)std::lround(frostLevel * 5), g.sceneMipCount - 1);
            FrameInfo fi;
            fi.timeSeconds = (float)now;
            g.ctx->OMSetRenderTargets(1, g.backRTV.GetAddressOf(), nullptr);
            g.ctx->RSSetViewports(1, &vp);
            if (frost.PrepareFrame(g.ctx.Get(), g.sceneSRV[level].Get(), fi, 24.0f).ok()) {
                for (auto& s : frosts) {
                    frost.SetMaterial(Material(s.radius * k * uiScale, s, false));
                    auto p = Px(s.r);
                    frost.RenderRect(g.ctx.Get(), g.backRTV.Get(), GlassRect{ p.left, p.top, p.right - p.left, p.bottom - p.top });
                }
            }
        }

        // 3. on the panels: the Mac's tint, the rim's hairline of light, then the words, icons and tracks
        d->BeginDraw();
        d->SetTransform(scale);
        for (auto& s : frosts) {
            auto rr = D2D1::RoundedRect(s.r, s.radius, s.radius);
            d->FillRoundedRectangle(rr, Brush(D2D1::ColorF(s.tint.r, s.tint.g, s.tint.b, std::clamp(s.tintAmount, 0.0f, 1.0f))));
            ComPtr<ID2D1GradientStopCollection> stops;
            float a = rim * (scene.dark ? 0.55f : 0.9f);
            D2D1_GRADIENT_STOP gs[3] = { { 0, D2D1::ColorF(1, 1, 1, a) }, { 0.5f, D2D1::ColorF(1, 1, 1, a * 0.18f) }, { 1, D2D1::ColorF(1, 1, 1, a * 0.55f) } };
            d->CreateGradientStopCollection(gs, 3, &stops);
            ComPtr<ID2D1LinearGradientBrush> lb;
            d->CreateLinearGradientBrush(D2D1::LinearGradientBrushProperties(D2D1::Point2F(s.r.left, s.r.top), D2D1::Point2F(s.r.right, s.r.bottom)), stops.Get(), &lb);
            d->DrawRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(s.r.left + 0.5f, s.r.top + 0.5f, s.r.right - 0.5f, s.r.bottom - 0.5f), s.radius - 0.5f, s.radius - 0.5f), lb.Get(), 1.0f);
        }
        for (auto& f : content) f();
        d->EndDraw();

        // 4. the lenses, refracting the frame so far
        if (ready && !lenses.empty()) {
            g.Snapshot();
            FrameInfo fi;
            fi.timeSeconds = (float)now;
            g.ctx->OMSetRenderTargets(1, g.backRTV.GetAddressOf(), nullptr);
            g.ctx->RSSetViewports(1, &vp);
            if (lens.PrepareFrame(g.ctx.Get(), g.compositeSRV.Get(), fi, 0.0f).ok()) {
                for (auto& s : lenses) {
                    lens.SetMaterial(Material(s.radius * k * uiScale, s, true));
                    auto p = Px(s.r);
                    lens.RenderRect(g.ctx.Get(), g.backRTV.Get(), GlassRect{ p.left, p.top, p.right - p.left, p.bottom - p.top });
                }
            }
        }

        // 5. on the lenses
        d->BeginDraw();
        d->SetTransform(scale);
        for (auto& f : overlays) f();
        d->EndDraw();
    }
};
