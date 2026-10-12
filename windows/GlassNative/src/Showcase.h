#pragma once
// The showcase: Simpl's Dashboard as the backdrop, and over it glass as Simpl for Windows will draw it — the Mac's
// "Unread announcements" popover, a toolbar with the Dashboard's Cards · List · Activity, a tab bar, switches and a
// slider whose knobs turn into lenses while held, buttons, a card of clear glass and a glass bubble to drag (the bubble
// to fling), and Tune (the glass, its corners and the backdrop, live). Built afresh each frame (immediate mode), then
// drawn in layers:
//   1. the backdrop (Direct2D) → the frame, and its frost (Glass.h: a quarter-size copy, a Gaussian blur);
//   2. the panels' glass — frost, bent edges, tone, rim, shadow — each shape on the GPU (Glass.h);
//   3. what is on the panels: words, icons, tracks (Direct2D);
//   4. the lenses — knobs, thumbs, the bubble, the clear card — refracting the frame so far (Glass.h, no frost);
//   5. what is on the lenses (Direct2D).
// Corners: cards are soft squircles (continuous corners: a superellipse of degree 3 over 1.5 × the radius), bars and
// every other capsule are round — the glass and every shape Direct2D draws alike. (Tune → Corners → Round: round corners
// for the cards too, to compare.)
#include "Gfx.h"
#include "Scene.h"
#include "Spring.h"
#include "Glass.h"
#include <functional>
#include <vector>
#include <string>
#include <unordered_map>

struct Showcase {
    Gfx& g;
    Scene scene;
    Glass glass;
    bool ready = false;

    // the glass, as Tune sets it (DIPs where a length) — the defaults are the Mac's popover, measured
    float frostSigma = 13;      // the frost's Gaussian (0: clear glass)
    float pull = 30;            // how far past the edge the rim reads what is behind
    float band = 13;            // how deep the rim's bending band is
    float dispersion = 0.03f, saturation = 1.8f;
    float lift = 0.38f;         // how far what is behind is lifted toward the material's colour
    float rim = 0.53f;          // the rim's light
    bool squircle = true;       // cards' corners: continuous (true) or round (false); bars are round capsules either way
    static constexpr float kSquircleDegree = 3, kSquircleScale = 1.5f;

    /// A shape's corners: a capsule (its radius reaching half its height) stays round; a card's are a soft squircle's.
    /// Returns the superellipse's degree (0: round) and sets the radius to draw with.
    float Corners(float hw, float hh, float& radius, int choice = -1) const {
        bool sq = choice < 0 ? squircle : choice == 1;
        float minHalf = std::min(hw, hh);
        if (!sq || radius >= minHalf - 0.5f) { radius = std::min(radius, minHalf); return 0; }
        radius = std::min(radius * kSquircleScale, minHalf);
        return kSquircleDegree;
    }

    // input (layout DIPs)
    float mx = -1, my = -1;
    bool down = false, pressed = false, released = false, consumed = false;
    std::string active;        // what holds the pointer
    float uiScale = 1;         // the whole showcase scaled down to fit a small window

    // where things are
    float popX = 34, popY = 104, ctlX = -1, ctlY = 96, tuneX = -1, tuneY = 414, clearX = 622, clearY = 104;
    float grabX = 0, grabY = 0;
    Spring bubbleX{ 700, 0.45f, 0.78f }, bubbleY{ 400, 0.45f, 0.78f };
    std::vector<std::pair<double, std::pair<float, float>>> samples;
    std::unordered_map<std::string, Spring> springs;
    bool toggles[4] = { true, false, true, true };
    float textSize = 0.42f;
    int view = 1, tab = 0;
    double now = 0;

    static constexpr float kBubble = 150, kClearW = 300, kClearH = 200;
    enum Kind { KindPanel = 0, KindControl = 1, KindBubble = 2, KindClear = 3 };
    /// A shape of glass: tint.a is how far the material's colour leans to the tint; keep < 0: Tune's.
    struct Shape { D2D1_RECT_F r; float radius; D2D1_COLOR_F tint; float keep = -1; int kind = KindPanel; float power = 1; int corners = -1; /* -1: Tune's, 0: round, 1: squircle */ };
    std::vector<Shape> frosts, lenses;
    std::vector<std::function<void()>> content, overlays;

    explicit Showcase(Gfx& gfx) : g(gfx) {}

    bool Init() {
        ready = glass.Init(g.device.Get()) && glass.Resize(g.device.Get(), g.width, g.height);
        return ready;
    }

    void Resized() {
        if (ready) glass.Resize(g.device.Get(), g.width, g.height);
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

    /// A frosted panel of glass (the Mac's popover glass). tint: the colour it leans to (a = how far: the Mac's
    /// .tint(c.opacity(0.10)) measures about 0.25); keep: how much of what is behind shows (< 0: Tune's).
    void Frost(D2D1_RECT_F r, float radius, bool interactive = false, D2D1_COLOR_F tint = { 0, 0, 0, 0 }, float keep = -1) {
        (void)interactive;
        frosts.push_back({ r, radius, tint, keep, KindPanel });
    }

    /// A lens of glass (a knob, a thumb, the bubble, the clear card): no frost, what is under it bent through its rim.
    void Lens(D2D1_RECT_F r, float radius, float power = 1, int kind = KindControl) { lenses.push_back({ r, radius, { 0, 0, 0, 0 }, 1, kind, power }); }

    /// The shape of a rounded rectangle as the glass draws it (Corners: round, or a soft squircle's).
    ComPtr<ID2D1Geometry> Geo(D2D1_RECT_F r, float radius) {
        ComPtr<ID2D1Geometry> out;
        float hw = (r.right - r.left) / 2, hh = (r.bottom - r.top) / 2;
        float R = radius;
        float degree = Corners(hw, hh, R);
        if (degree == 0) {
            ComPtr<ID2D1RoundedRectangleGeometry> rr;
            g.d2dFactory->CreateRoundedRectangleGeometry(D2D1::RoundedRect(r, R, R), &rr);
            out = rr;
            return out;
        }
        float e = 2 / degree;
        ComPtr<ID2D1PathGeometry> path;
        g.d2dFactory->CreatePathGeometry(&path);
        ComPtr<ID2D1GeometrySink> sink;
        path->Open(&sink);
        // corners clockwise from the top right: centre, and the quadrant's signs
        struct C { float cx, cy, sx, sy; } cs[4] = {
            { r.right - R, r.top + R, 1, -1 }, { r.right - R, r.bottom - R, 1, 1 }, { r.left + R, r.bottom - R, -1, 1 }, { r.left + R, r.top + R, -1, -1 } };
        const int steps = 18;
        bool begun = false;
        for (int c = 0; c < 4; c++) {
            for (int i = 0; i <= steps; i++) {
                // from the edge before the corner to the edge after it (clockwise)
                float t = (float)i / steps * 1.5707963f;
                float a = (c % 2 == 0) ? 1.5707963f - t : t; // (top right and bottom left run from the vertical)
                float ux = std::pow(std::max(std::cos(a), 0.0f), e), uy = std::pow(std::max(std::sin(a), 0.0f), e);
                D2D1_POINT_2F pt = D2D1::Point2F(cs[c].cx + cs[c].sx * R * ux, cs[c].cy + cs[c].sy * R * uy);
                if (!begun) { sink->BeginFigure(pt, D2D1_FIGURE_BEGIN_FILLED); begun = true; }
                else sink->AddLine(pt);
            }
        }
        sink->EndFigure(D2D1_FIGURE_END_CLOSED);
        sink->Close();
        out = path;
        return out;
    }

    void FillShape(D2D1_RECT_F r, float radius, D2D1_COLOR_F c) {
        auto geo = Geo(r, radius);
        g.d2d->FillGeometry(geo.Get(), Brush(c));
    }

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
        if (sx.target != (float)selected) sx.To((float)selected); // (chosen elsewhere: a picture dropped in)
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
            FillShape(D2D1::RectF(tx, ty, tx + tw, ty + th), th / 2, scene.dark ? D2D1::ColorF(1, 1, 1, 0.1f) : D2D1::ColorF(1, 1, 1, 0.55f));
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
            FillShape(r, h / 2, c);
        });
        Lens(D2D1::RectF(kx, ky, kx + kw, ky + kh), kh / 2);
        overlays.push_back([=, this] { Knob(kx, ky, kw, kh, 1 - 0.9f * hv); });
    }

    /// The white knob at rest (and its soft shadow), fading as it turns to glass.
    void Knob(float x, float y, float w, float h, float opacity) {
        if (opacity <= 0.01f) return;
        for (int i = 3; i >= 1; i--)
            FillShape(D2D1::RectF(x - i * 0.6f, y + 1.2f, x + w + i * 0.6f, y + h + i * 1.1f), h / 2 + i, D2D1::ColorF(0, 0, 0, 0.06f * opacity));
        FillShape(D2D1::RectF(x, y, x + w, y + h), h / 2, D2D1::ColorF(1, 1, 1, opacity));
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
        if (prominent) Frost(gr, h / 2 + grow, true, Rgb(0, 122, 255, 0.9f), 0.3f); else Frost(gr, h / 2 + grow, true);
        content.push_back([=, this] {
            Text(label, x, y, w, h, 14.5f, prominent ? D2D1::ColorF(1, 1, 1, 1) : Ink(), DWRITE_FONT_WEIGHT_SEMI_BOLD, DWRITE_TEXT_ALIGNMENT_CENTER);
            if (pr.value > 0.01f) FillShape(gr, h / 2 + grow, D2D1::ColorF(1, 1, 1, 0.08f * pr.value));
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
        // the bubble and the clear card lie over everything: they take the pointer first
        if (pressed && Inside(D2D1::RectF(bubbleX.value, bubbleY.value, bubbleX.value + kBubble, bubbleY.value + kBubble))) {
            active = "bubble"; grabX = mx - bubbleX.value; grabY = my - bubbleY.value; samples.clear(); consumed = true;
        } else if (pressed && Inside(D2D1::RectF(clearX, clearY, clearX + kClearW, clearY + kClearH))) {
            active = "clear"; grabX = mx - clearX; grabY = my - clearY; consumed = true;
        }
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

        ClearCard();
        Bubble(W, H);
    }

    /// The Mac's popover, as screenshotted: Unread announcements.
    void Popover() {
        float x = popX, y = popY, w = 560, h = 560;
        D2D1_RECT_F r = D2D1::RectF(x, y, x + w, y + h);
        Frost(r, 22, true, Rgb(255, 149, 0, 0.25f)); // (the Mac's: radius 22, continuous; .tint(orange.opacity(0.10)))
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
        Frost(r, 24, true);
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
        float x = tuneX, y = tuneY, w = 344, h = 444;
        D2D1_RECT_F r = D2D1::RectF(x, y, x + w, y + h);
        Frost(r, 24, true);
        content.push_back([=, this] { Text(L"Tune", x + 24, y + 16, 120, 36, 20, Ink(), DWRITE_FONT_WEIGHT_SEMI_BOLD, DWRITE_TEXT_ALIGNMENT_LEADING, L"Segoe UI Variable Display"); });
        int mode = scene.dark ? 0 : 1;
        Segmented("mode", x + w - 24 - 146, y + 14, 69, { L"Dark", L"Light" }, mode, false);
        scene.dark = mode == 0;

        // the backdrop: Simpl · Grid · Photo (Photo again: the next picture)
        float bx = x + w - 24 - 206, by = y + 58;
        content.push_back([=, this] { Text(L"Backdrop", x + 24, by, 90, 40, 13.5f, Ink()); });
        if (pressed && !consumed && scene.backdrop == BackdropPhoto && Inside(D2D1::RectF(bx + 4 + 132, by, bx + 4 + 198, by + 40))) scene.NextPhoto();
        int shown[] = { BackdropSimpl, BackdropGrid, BackdropPhoto };
        int pick = scene.backdrop == BackdropSimpl ? 0 : scene.backdrop == BackdropGrid ? 1 : 2;
        Segmented("backdrop", bx, by, 66, { L"Simpl", L"Grid", L"Photo" }, pick, false);
        scene.backdrop = shown[pick];

        // the cards' corners: soft squircles (the bars are round either way), or round to compare
        float cy = y + 102;
        content.push_back([=, this] { Text(L"Corners", x + 24, cy, 90, 40, 13.5f, Ink()); });
        int corners = squircle ? 1 : 0;
        Segmented("corners", x + w - 24 - 168, cy, 80, { L"Round", L"Squircle" }, corners, false);
        squircle = corners == 1;

        struct K { const wchar_t* name; float* v; float lo, hi; const wchar_t* fmt; };
        K knobs[] = {
            { L"Frost", &frostSigma, 0, 30, L"%.0f pt" }, { L"Refraction", &pull, 0, 60, L"%.0f pt" }, { L"Edge", &band, 4, 30, L"%.0f pt" },
            { L"Dispersion", &dispersion, 0, 0.15f, L"%.2f" }, { L"Colour", &saturation, 0.5f, 2, L"%.2f" }, { L"Tint", &lift, 0, 0.9f, L"%.2f" },
            { L"Rim", &rim, 0, 1.5f, L"%.2f" },
        };
        float ry = y + 150;
        int index = 0;
        for (auto& k : knobs) {
            wchar_t val[24];
            swprintf(val, 24, k.fmt, *k.v);
            std::wstring valS = val;
            const wchar_t* name = k.name;
            content.push_back([=, this] {
                Text(name, x + 24, ry, 90, 30, 13.5f, Ink());
                Text(valS.c_str(), x + w - 74, ry, 50, 30, 12, Ink2(), DWRITE_FONT_WEIGHT_NORMAL, DWRITE_TEXT_ALIGNMENT_TRAILING);
            });
            float f = (*k.v - k.lo) / (k.hi - k.lo);
            Slider("knob" + std::to_string(index++), x + 112, ry, w - 112 - 78, f);
            *k.v = k.lo + f * (k.hi - k.lo);
            ry += 32;
        }
        content.push_back([=, this] { Text(L"Moving backdrop", x + 24, ry + 2, 200, 32, 13.5f, Ink()); });
        Toggle("moving", x + w - 24 - 56, ry + 2, scene.moving);

        // what the backdrop is
        std::wstring hint;
        if (scene.backdrop == BackdropGrid) hint = L"Straight lines: see them bend at each rim (Frost 0: clear)";
        else if (scene.backdrop == BackdropSimpl) hint = L"Simpl's Dashboard, as behind the Mac's popover";
        else if (scene.photos.empty()) hint = L"No pictures found: drop one on the window";
        else hint = scene.PhotoName() + L" (" + std::to_wstring(scene.photoIndex + 1) + L" of " + std::to_wstring(scene.photos.size()) + L") · Photo: next · or drop one";
        content.push_back([=, this] { Text(hint.c_str(), x + 24, ry + 40, w - 48, 22, 12, Ink2()); });
        Draggable("tune", r, tuneX, tuneY);
    }

    /// A card of clear glass (no frost at all): what is under it shows sharp, bent at its rim — Refraction and
    /// Thickness, as Tune sets them, plain to see.
    void ClearCard() {
        float w = kClearW, h = kClearH, x = clearX, y = clearY;
        D2D1_RECT_F r = D2D1::RectF(x, y, x + w, y + h);
        Lens(r, 44, 1, KindClear);
        overlays.push_back([=, this] {
            Text(L"Clear glass", x + 22, y + 14, 200, 26, 14.5f, Ink(), DWRITE_FONT_WEIGHT_SEMI_BOLD);
        });
        Draggable("clear", r, clearX, clearY);
    }

    /// The bubble: drag it, fling it — it heads where its momentum takes it, inside the window, on a spring.
    void Bubble(float W, float H) {
        float d = kBubble;
        D2D1_RECT_F r = D2D1::RectF(bubbleX.value, bubbleY.value, bubbleX.value + d, bubbleY.value + d);
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
        lenses.push_back({ r, d / 2, { 0, 0, 0, 0 }, 1, KindBubble, 1, 0 }); // (a circle stays a circle, whatever the corners)
    }

    // ---- drawing a frame ----

    /// A shape's glass, in pixels: the panels as the Mac's popover (Tune's numbers), the lenses as its knobs.
    GlassLook Look(const Shape& s) const {
        float k = g.Scale() * uiScale;
        GlassLook l;
        l.cx = (s.r.left + s.r.right) / 2 * k;
        l.cy = (s.r.top + s.r.bottom) / 2 * k;
        l.hw = std::max((s.r.right - s.r.left) / 2, 1.0f) * k;
        l.hh = std::max((s.r.bottom - s.r.top) / 2, 1.0f) * k;
        l.radius = s.radius * k;
        l.squircle = Corners(l.hw, l.hh, l.radius, s.corners);
        l.dark = scene.dark;
        float minHalf = std::min(l.hw, l.hh);
        bool big = minHalf / k > 50;
        l.rimWidth = 1.25f * k;
        l.glow = scene.dark ? 0.03f : 0.02f;
        l.outline = scene.dark ? 0.5f : 0.1f;
        if (s.kind == KindPanel) {
            l.frost = std::clamp(frostSigma / 3, 0.0f, 1.0f);
            l.pull = std::min(pull * k, minHalf * 1.2f);
            l.band = std::min(band * k, minHalf * 0.6f);
            float keep = s.keep >= 0 ? s.keep : 1 - lift;
            l.keep = keep;
            float grey = scene.dark ? 0.55f : 0.97f; // (solved from the Mac's popover: plain glass over its dark page)
            l.r = grey + (s.tint.r - grey) * s.tint.a;
            l.g = grey + (s.tint.g - grey) * s.tint.a;
            l.b = grey + (s.tint.b - grey) * s.tint.a;
            l.saturation = saturation;
            l.dispersion = dispersion;
            l.rim = rim;
            l.shadow = big ? (scene.dark ? 0.35f : 0.16f) : (scene.dark ? 0.16f : 0.07f);
            l.shadowBlur = (big ? 30.0f : 6.0f) * k;
            l.shadowY = (big ? 8.0f : 2.0f) * k;
        } else {
            l.frost = 0;
            l.keep = 1;
            l.saturation = 1.08f;
            l.brightness = 1.03f;
            l.rim = std::max(rim, 0.6f);
            l.shadow = scene.dark ? 0.18f : 0.1f;
            l.shadowBlur = 8 * k;
            l.shadowY = 3 * k;
            if (s.kind == KindClear) {
                l.pull = std::min(pull * k, minHalf);
                l.band = std::min(band * k, minHalf * 0.8f);
                l.dispersion = dispersion;
                l.brightness = 1.02f;
                l.saturation = 1;
                l.shadowBlur = 20 * k;
                l.shadowY = 6 * k;
            } else if (s.kind == KindBubble) {
                l.pull = minHalf * 0.6f;
                l.band = minHalf * 0.9f;
                l.magnify = 0.18f;
                l.dispersion = std::max(dispersion, 0.05f);
            } else {
                l.pull = minHalf * (0.15f + 0.45f * s.power);
                l.band = minHalf * 0.8f;
                l.magnify = 0.12f * s.power;
                l.dispersion = dispersion + 0.03f * s.power;
            }
        }
        return l;
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

        // 1. the backdrop, and its frost
        d->SetTarget(g.sceneBitmap.Get());
        d->BeginDraw();
        d->SetTransform(scale);
        scene.Draw(g, W, H, dt);
        d->EndDraw();
        g.PublishScene();

        // 2. the panels' glass
        if (ready) {
            ID3D11ShaderResourceView* frosted = frostSigma > 0.2f ? glass.Frost(g.ctx.Get(), g.sceneSRV.Get(), g.width, g.height, frostSigma * k * uiScale) : nullptr;
            for (auto& s : frosts) glass.Draw(g.ctx.Get(), g.backRTV.Get(), g.width, g.height, k * uiScale, g.sceneSRV.Get(), frosted, Look(s));
        }

        // 3. on the panels
        d->SetTarget(g.backBitmap.Get());
        d->BeginDraw();
        d->SetTransform(scale);
        for (auto& f : content) f();
        d->EndDraw();

        // 4. the lenses, refracting the frame so far
        if (ready && !lenses.empty()) {
            g.Snapshot();
            for (auto& s : lenses) glass.Draw(g.ctx.Get(), g.backRTV.Get(), g.width, g.height, k * uiScale, g.compositeSRV.Get(), nullptr, Look(s));
        }

        // 5. on the lenses
        d->BeginDraw();
        d->SetTransform(scale);
        for (auto& f : overlays) f();
        d->EndDraw();
    }
};
