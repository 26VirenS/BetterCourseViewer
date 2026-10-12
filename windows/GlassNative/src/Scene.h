#pragma once
// What the glass sits over: a Simpl Dashboard as it looks behind a popover — a greeting, counters, the course skyline
// of coloured towers, a list of work — over soft fields of colour, drifting slowly so the frost and the refraction can be
// seen moving. Drawn by Direct2D, in DIPs.
#include "Gfx.h"
#include <cmath>

struct Course { const wchar_t* code; D2D1_COLOR_F color; float score; };

inline D2D1_COLOR_F Rgb(int r, int g, int b, float a = 1) { return D2D1::ColorF(r / 255.0f, g / 255.0f, b / 255.0f, a); }

static const Course kCourses[] = {
    { L"MATH 021", Rgb(52, 199, 89), 0.92f }, { L"PHYS 008", Rgb(48, 176, 199), 0.81f }, { L"PHYS 008L", Rgb(255, 45, 85), 0.74f },
    { L"SPRK 010", Rgb(255, 149, 0), 0.88f }, { L"WRI 010", Rgb(200, 144, 28), 0.95f }, { L"CHEM 002", Rgb(175, 82, 222), 0.69f },
    { L"BIO 001", Rgb(0, 122, 255), 0.84f },
};

struct Scene {
    bool dark = true;
    bool moving = true;
    float clock = 0;

    void Draw(Gfx& g, float w, float h, float dt) {
        if (moving) clock += dt;
        auto* d = g.d2d.Get();
        d->Clear(dark ? Rgb(20, 20, 22) : Rgb(242, 242, 245));

        // the fields of colour: big soft blobs, wandering
        struct Blob { float x, y, r; D2D1_COLOR_F c; float sx, sy; };
        Blob blobs[] = {
            { 140, 560, 420, Rgb(255, 149, 0), 0.11f, 0.08f }, { 780, 120, 380, Rgb(0, 122, 255), 0.09f, 0.12f },
            { 1000, 640, 460, Rgb(52, 199, 89), 0.07f, 0.1f }, { 430, 260, 300, Rgb(255, 45, 85), 0.13f, 0.06f },
            { 1200, 240, 320, Rgb(175, 82, 222), 0.08f, 0.11f },
        };
        for (auto& b : blobs) {
            float x = b.x + std::sin(clock * b.sx * 2) * 110;
            float y = b.y + std::cos(clock * b.sy * 2) * 80;
            ComPtr<ID2D1GradientStopCollection> stops;
            D2D1_GRADIENT_STOP gs[2] = { { 0, D2D1::ColorF(b.c.r, b.c.g, b.c.b, dark ? 0.45f : 0.36f) }, { 1, D2D1::ColorF(b.c.r, b.c.g, b.c.b, 0) } };
            d->CreateGradientStopCollection(gs, 2, &stops);
            ComPtr<ID2D1RadialGradientBrush> rb;
            d->CreateRadialGradientBrush(D2D1::RadialGradientBrushProperties(D2D1::Point2F(x, y), D2D1::Point2F(0, 0), b.r, b.r), stops.Get(), &rb);
            d->FillEllipse(D2D1::Ellipse(D2D1::Point2F(x, y), b.r, b.r), rb.Get());
        }

        // the page, drifting up and down
        float drift = moving || clock > 0 ? (std::sin(clock * 0.45f) * 0.5f + 0.5f) * -150 : 0;
        D2D1_MATRIX_3X2_F old;
        d->GetTransform(&old);
        d->SetTransform(D2D1::Matrix3x2F::Translation(0, drift) * old);
        ComPtr<ID2D1SolidColorBrush> ink, ink2, card, brush;
        d->CreateSolidColorBrush(dark ? D2D1::ColorF(1, 1, 1, 0.92f) : D2D1::ColorF(0, 0, 0, 0.88f), &ink);
        d->CreateSolidColorBrush(dark ? D2D1::ColorF(0.92f, 0.92f, 0.96f, 0.6f) : D2D1::ColorF(0.24f, 0.24f, 0.26f, 0.6f), &ink2);
        d->CreateSolidColorBrush(dark ? Rgb(38, 38, 41) : Rgb(255, 255, 255), &card);
        d->CreateSolidColorBrush(D2D1::ColorF(0, 0, 0), &brush);
        auto text = [&](const wchar_t* s, float x, float y, float size, ID2D1Brush* b, DWRITE_FONT_WEIGHT wt = DWRITE_FONT_WEIGHT_NORMAL, float wmax = 900) {
            d->DrawText(s, (UINT32)wcslen(s), g.Font(L"Segoe UI Variable Display", size, wt), D2D1::RectF(x, y, x + wmax, y + size * 1.5f), b);
        };
        float x0 = 64, y = 96;
        text(L"Good evening, Sam", x0, y, 44, ink.Get(), DWRITE_FONT_WEIGHT_BOLD);
        text(L"Monday, October 12", x0, y + 62, 17, ink2.Get());
        y += 104;

        // counters
        struct Counter { const wchar_t* label; const wchar_t* n; const wchar_t* note; D2D1_COLOR_F c; };
        Counter cs[] = {
            { L"Due today", L"4", L"35 points total", Rgb(255, 59, 48) }, { L"Tomorrow", L"3", L"52 points total", Rgb(255, 149, 0) },
            { L"Next 7 days", L"14", L"Across 5 courses", Rgb(52, 199, 89) }, { L"Overdue", L"1", L"1 not submitted", Rgb(255, 59, 48) },
            { L"Unread", L"3", L"From 2 courses", Rgb(255, 149, 0) }, { L"Graded", L"6", L"79 / 109 points", Rgb(88, 86, 214) },
        };
        float cw = (w - x0 * 2 - 5 * 14) / 6;
        for (int i = 0; i < 6; i++) {
            float cx = x0 + i * (cw + 14);
            d->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(cx, y, cx + cw, y + 118), 18, 18), card.Get());
            brush->SetColor(cs[i].c);
            text(cs[i].label, cx + 16, y + 12, 15, brush.Get(), DWRITE_FONT_WEIGHT_SEMI_BOLD, cw - 24);
            text(cs[i].n, cx + 16, y + 34, 36, ink.Get(), DWRITE_FONT_WEIGHT_BOLD, cw - 24);
            text(cs[i].note, cx + 16, y + 84, 13, ink2.Get(), DWRITE_FONT_WEIGHT_NORMAL, cw - 24);
        }
        y += 150;

        // the skyline: a tower per course, as tall as its score, lit windows
        text(L"Your courses", x0, y, 24, ink.Get(), DWRITE_FONT_WEIGHT_BOLD);
        y += 46;
        float base = y + 300;
        ComPtr<ID2D1SolidColorBrush> lit, white;
        d->CreateSolidColorBrush(D2D1::ColorF(1, 0.97f, 0.86f, 0.85f), &lit);
        d->CreateSolidColorBrush(D2D1::ColorF(1, 1, 1, 1), &white);
        for (int i = 0; i < 7; i++) {
            auto& c = kCourses[i];
            float tx = x0 + i * 168, tw = 140, th = 290 * c.score;
            ComPtr<ID2D1GradientStopCollection> stops;
            D2D1_GRADIENT_STOP gs[2] = { { 0, c.color }, { 1, D2D1::ColorF(c.color.r * 0.7f, c.color.g * 0.7f, c.color.b * 0.7f) } };
            d->CreateGradientStopCollection(gs, 2, &stops);
            ComPtr<ID2D1LinearGradientBrush> lb;
            d->CreateLinearGradientBrush(D2D1::LinearGradientBrushProperties(D2D1::Point2F(0, base - th), D2D1::Point2F(0, base)), stops.Get(), &lb);
            d->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(tx, base - th, tx + tw, base), 14, 14), lb.Get());
            for (float wy = base - th + 18; wy < base - 22; wy += 26)
                for (int k = 0; k < 4; k++) {
                    float wx = tx + 14 + k * 30;
                    lit->SetOpacity(((int)(wy + k * 7) % 4 == 0) ? 0.35f : 0.85f);
                    d->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(wx, wy, wx + 20, wy + 13), 2, 2), lit.Get());
                }
            text(c.code, tx + 4, base + 8, 14, white.Get(), DWRITE_FONT_WEIGHT_SEMI_BOLD, tw);
        }
        y = base + 52;

        // the list of work coming up
        text(L"Coming up", x0, y, 24, ink.Get(), DWRITE_FONT_WEIGHT_BOLD);
        y += 46;
        const wchar_t* work[] = { L"Dis01", L"Qz01", L"Week 2 Post Class Assignment: GC articles", L"Lab report 3: Projectile motion",
                                  L"Essay draft: The rhetoric of place", L"Problem set 6", L"Reading response 4" };
        for (int i = 0; i < 7; i++) {
            auto& c = kCourses[i];
            d->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(x0, y, w - x0, y + 62), 14, 14), card.Get());
            brush->SetColor(D2D1::ColorF(c.color.r, c.color.g, c.color.b, 0.3f));
            d->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(x0 + 14, y + 13, x0 + 50, y + 49), 9, 9), brush.Get());
            std::wstring sub = std::wstring(c.code) + L" · Assignment";
            text(sub.c_str(), x0 + 64, y + 8, 13, ink2.Get());
            text(work[i], x0 + 64, y + 28, 17, ink.Get());
            y += 72;
        }
        d->SetTransform(old);
    }
};
