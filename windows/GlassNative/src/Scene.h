#pragma once
// What the glass sits over, three ways (Tune → Backdrop):
//   Grid  — a sharp test card drawn at the display's full resolution: hairline grid, a Siemens star, rainbow stripes,
//           rings, a checkerboard and small text — straight lines that show plainly where and how far the glass bends;
//   Photo — a picture at its full resolution, panning slowly: the desktop's wallpaper, then the photos Windows carries
//           (C:\Windows\Web), or any picture dropped on the window;
//   Simpl — a Simpl Dashboard as it looks behind a popover (greeting, counters, the course skyline, a list of work).
// Each drifts slowly so the frost and the refraction can be seen moving. Drawn by Direct2D, in DIPs.
#include "Gfx.h"
#include <wincodec.h>
#include <cmath>
#include <vector>
#include <string>
#include <algorithm>

struct Course { const wchar_t* code; D2D1_COLOR_F color; float score; };

inline D2D1_COLOR_F Rgb(int r, int g, int b, float a = 1) { return D2D1::ColorF(r / 255.0f, g / 255.0f, b / 255.0f, a); }

static const Course kCourses[] = {
    { L"MATH 021", Rgb(52, 199, 89), 0.92f }, { L"PHYS 008", Rgb(48, 176, 199), 0.81f }, { L"PHYS 008L", Rgb(255, 45, 85), 0.74f },
    { L"SPRK 010", Rgb(255, 149, 0), 0.88f }, { L"WRI 010", Rgb(200, 144, 28), 0.95f }, { L"CHEM 002", Rgb(175, 82, 222), 0.69f },
    { L"BIO 001", Rgb(0, 122, 255), 0.84f },
};

enum Backdrop { BackdropGrid = 0, BackdropPhoto = 1, BackdropSimpl = 2 };

struct Scene {
    bool dark = true;
    bool moving = true;
    float clock = 0;
    int backdrop = BackdropSimpl;

    // the pictures (Photo): found on first use; the one shown, loaded when it changes
    std::vector<std::wstring> photos;
    bool searched = false;
    int photoIndex = 0, loadedIndex = -1;
    std::wstring loadedPath;
    ComPtr<ID2D1Bitmap> photo;
    UINT photoW = 0, photoH = 0;
    ComPtr<ID2D1PathGeometry> star;

    void Draw(Gfx& g, float w, float h, float dt) {
        if (moving) clock += dt;
        if (backdrop == BackdropPhoto && Photo(g)) DrawPhoto(g, w, h);
        else if (backdrop == BackdropSimpl) DrawSimpl(g, w, h);
        else DrawGrid(g, w, h);
    }

    // ---- Photo ----

    /// The pictures to show: the wallpaper first, then those Windows carries (the largest of each in Web\4K).
    void Search() {
        searched = true;
        auto add = [&](const std::wstring& p) {
            if (p.empty() || GetFileAttributesW(p.c_str()) == INVALID_FILE_ATTRIBUTES) return;
            std::wstring low = p;
            std::transform(low.begin(), low.end(), low.begin(), ::towlower);
            for (auto& q : photos) {
                std::wstring ql = q;
                std::transform(ql.begin(), ql.end(), ql.begin(), ::towlower);
                if (ql == low) return;
            }
            photos.push_back(p);
        };
        wchar_t wall[MAX_PATH] = {};
        if (SystemParametersInfoW(SPI_GETDESKWALLPAPER, MAX_PATH, wall, 0)) add(wall);
        wchar_t win[MAX_PATH] = {};
        GetWindowsDirectoryW(win, MAX_PATH);
        std::wstring web = std::wstring(win) + L"\\Web\\";
        const wchar_t* dirs[] = { L"4K\\Wallpaper\\Windows", L"Wallpaper\\Windows", L"Wallpaper\\ThemeA", L"Wallpaper\\ThemeB",
                                  L"Wallpaper\\ThemeC", L"Wallpaper\\ThemeD", L"Wallpaper\\Theme1", L"Wallpaper\\Theme2", L"Screen" };
        for (auto* dir : dirs) {
            std::wstring folder = web + dir + L"\\";
            bool largestOnly = wcsncmp(dir, L"4K", 2) == 0; // (one picture in many sizes)
            std::wstring best;
            ULONGLONG bestSize = 0;
            WIN32_FIND_DATAW fd;
            HANDLE f = FindFirstFileW((folder + L"*").c_str(), &fd);
            if (f == INVALID_HANDLE_VALUE) continue;
            do {
                std::wstring n = fd.cFileName;
                std::wstring ext = n.size() > 4 ? n.substr(n.find_last_of(L'.') + 1) : L"";
                std::transform(ext.begin(), ext.end(), ext.begin(), ::towlower);
                if (ext != L"jpg" && ext != L"jpeg" && ext != L"png") continue;
                ULONGLONG size = ((ULONGLONG)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
                if (largestOnly) { if (size > bestSize) { bestSize = size; best = folder + n; } }
                else add(folder + n);
            } while (FindNextFileW(f, &fd));
            FindClose(f);
            if (largestOnly) add(best);
        }
    }

    /// A picture dropped on the window: shown now, and kept first in the list.
    void Open(const std::wstring& path) {
        if (!searched) Search();
        photos.insert(photos.begin(), path);
        photoIndex = 0;
        loadedIndex = -1;
        backdrop = BackdropPhoto;
    }

    void NextPhoto() {
        if (!photos.empty()) photoIndex = (photoIndex + 1) % (int)photos.size();
    }

    std::wstring PhotoName() const {
        if (photos.empty()) return L"";
        auto& p = photos[photoIndex];
        auto slash = p.find_last_of(L"\\/");
        return slash == std::wstring::npos ? p : p.substr(slash + 1);
    }

    /// The picture to show, loaded at its full resolution (at most 4096 px on its longer side); false if none.
    bool Photo(Gfx& g) {
        if (!searched) Search();
        while (!photos.empty() && loadedIndex != photoIndex) {
            if (Load(g, photos[photoIndex])) { loadedIndex = photoIndex; break; }
            photos.erase(photos.begin() + photoIndex); // (unreadable: left out)
            if (photoIndex >= (int)photos.size()) photoIndex = 0;
        }
        return photo && !photos.empty();
    }

    bool Load(Gfx& g, const std::wstring& path) {
        ComPtr<IWICImagingFactory> wic;
        if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic)))) return false;
        ComPtr<IWICBitmapDecoder> dec;
        ComPtr<IWICBitmapFrameDecode> frame;
        if (FAILED(wic->CreateDecoderFromFilename(path.c_str(), nullptr, GENERIC_READ, WICDecodeMetadataCacheOnDemand, &dec))) return false;
        if (FAILED(dec->GetFrame(0, &frame))) return false;
        UINT iw = 0, ih = 0;
        frame->GetSize(&iw, &ih);
        if (!iw || !ih) return false;
        ComPtr<IWICBitmapSource> src = frame;
        UINT limit = std::min<UINT>(4096, g.d2d->GetMaximumBitmapSize());
        if (std::max(iw, ih) > limit) {
            float k = (float)limit / std::max(iw, ih);
            ComPtr<IWICBitmapScaler> sc;
            if (FAILED(wic->CreateBitmapScaler(&sc)) || FAILED(sc->Initialize(frame.Get(), std::max(1u, (UINT)(iw * k)), std::max(1u, (UINT)(ih * k)), WICBitmapInterpolationModeFant))) return false;
            src = sc;
        }
        ComPtr<IWICFormatConverter> conv;
        if (FAILED(wic->CreateFormatConverter(&conv))) return false;
        if (FAILED(conv->Initialize(src.Get(), GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherTypeNone, nullptr, 0, WICBitmapPaletteTypeMedianCut))) return false;
        ComPtr<ID2D1Bitmap> bmp;
        if (FAILED(g.d2d->CreateBitmapFromWicBitmap(conv.Get(), nullptr, &bmp))) return false;
        conv->GetSize(&photoW, &photoH);
        photo = bmp;
        loadedPath = path;
        return true;
    }

    /// The picture covering the window (a little larger, to pan), every pixel of it, panning slowly.
    void DrawPhoto(Gfx& g, float w, float h) {
        auto* d = g.d2d.Get();
        d->Clear(D2D1::ColorF(0, 0, 0));
        float k = std::max(w / photoW, h / photoH) * 1.12f;
        float dw = photoW * k, dh = photoH * k;
        float px = (w - dw) / 2 + std::sin(clock * 0.21f) * (dw - w) * 0.45f;
        float py = (h - dh) / 2 + std::sin(clock * 0.16f + 1) * (dh - h) * 0.45f;
        D2D1_RECT_F dest = D2D1::RectF(px, py, px + dw, py + dh);
        d->DrawBitmap(photo.Get(), &dest, 1.0f, D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC, nullptr, nullptr);
    }

    // ---- Grid ----

    /// A sharp test card: anything glass does to it — a bend at a rim, colours parting, a frost — is plain to see.
    void DrawGrid(Gfx& g, float w, float h) {
        auto* d = g.d2d.Get();
        D2D1_COLOR_F bg = dark ? Rgb(18, 18, 20) : Rgb(244, 244, 246);
        d->Clear(bg);
        auto ink = [&](float a) { return dark ? D2D1::ColorF(1, 1, 1, a) : D2D1::ColorF(0, 0, 0, a); };
        ComPtr<ID2D1SolidColorBrush> b;
        d->CreateSolidColorBrush(ink(1), &b);
        float hair = 1.0f / g.Scale(); // (one device pixel)

        D2D1_MATRIX_3X2_F old;
        d->GetTransform(&old);
        float ox = std::sin(clock * 0.31f) * 70, oy = std::sin(clock * 0.23f + 0.6f) * 55;
        d->SetTransform(D2D1::Matrix3x2F::Translation(ox, oy) * old);

        // the grid: hairlines every 20, stronger every 100
        float x0 = -120, y0 = -120, x1 = w + 120, y1 = h + 120;
        for (float x = x0; x <= x1; x += 20) {
            bool major = std::fmod(x - x0, 100.0f) < 1;
            b->SetColor(ink(major ? 0.28f : 0.11f));
            d->DrawLine(D2D1::Point2F(x, y0), D2D1::Point2F(x, y1), b.Get(), major ? hair * 2 : hair);
        }
        for (float y = y0; y <= y1; y += 20) {
            bool major = std::fmod(y - y0, 100.0f) < 1;
            b->SetColor(ink(major ? 0.28f : 0.11f));
            d->DrawLine(D2D1::Point2F(x0, y), D2D1::Point2F(x1, y), b.Get(), major ? hair * 2 : hair);
        }

        // rainbow stripes, on a slant (under the popover and the clear card: their rims bend them, and part their colours)
        static const D2D1_COLOR_F hues[] = { Rgb(255, 59, 48), Rgb(255, 149, 0), Rgb(255, 204, 0), Rgb(52, 199, 89), Rgb(48, 176, 199), Rgb(0, 122, 255), Rgb(175, 82, 222) };
        D2D1_RECT_F band = D2D1::RectF(20, 80, w * 0.7f, 340);
        d->PushAxisAlignedClip(band, D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        D2D1_MATRIX_3X2_F slant;
        d->GetTransform(&slant);
        d->SetTransform(D2D1::Matrix3x2F::Rotation(-28, D2D1::Point2F(band.left, band.top)) * slant);
        for (int i = -40; i < 120; i++) {
            b->SetColor(hues[((i % 7) + 7) % 7]);
            float sx = band.left + i * 12;
            d->FillRectangle(D2D1::RectF(sx, band.top - 400, sx + 12, band.bottom + 600), b.Get());
        }
        d->SetTransform(slant);
        d->PopAxisAlignedClip();

        // small text, many sizes (under the popover's lower half)
        const wchar_t* lines[] = {
            L"Liquid Glass bends what is behind it at its rim and keeps the middle true.",
            L"Every straight line here should curve as it passes under an edge of glass,",
            L"and the colours of the stripes above should part a little where it bends most.",
            L"The quick brown fox jumps over the lazy dog. 0123456789 — ABCDEFGHIJKLMNOPQRSTUVWXYZ",
            L"abcdefghijklmnopqrstuvwxyz  !@#$%^&*()  {}[]<>  ¼ ½ ¾  ← ↑ → ↓",
        };
        float ty = 420;
        float sizes[] = { 11, 12.5f, 14, 16, 19, 23 };
        for (int i = 0; i < 12; i++) {
            float sz = sizes[i % 6];
            b->SetColor(ink(i % 3 == 0 ? 0.9f : 0.7f));
            const wchar_t* s = lines[i % 5];
            auto* f = g.Font(L"Segoe UI Variable Text", sz);
            f->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_LEADING); // (formats are shared with the glass's words)
            d->DrawText(s, (UINT32)wcslen(s), f, D2D1::RectF(36, ty, w * 0.6f, ty + sz * 1.6f), b.Get());
            ty += sz * 1.55f;
        }

        // a Siemens star (under the bubble): its spokes show the slightest bend
        if (!star) {
            g.d2dFactory->CreatePathGeometry(&star);
            ComPtr<ID2D1GeometrySink> sink;
            star->Open(&sink);
            const int n = 36;
            for (int i = 0; i < n; i++) {
                float a0 = (i * 2) * 3.14159265f / n, a1 = (i * 2 + 1) * 3.14159265f / n;
                sink->BeginFigure(D2D1::Point2F(0, 0), D2D1_FIGURE_BEGIN_FILLED);
                sink->AddLine(D2D1::Point2F(std::cos(a0), std::sin(a0)));
                sink->AddArc(D2D1::ArcSegment(D2D1::Point2F(std::cos(a1), std::sin(a1)), D2D1::SizeF(1, 1), 0, D2D1_SWEEP_DIRECTION_CLOCKWISE, D2D1_ARC_SIZE_SMALL));
                sink->EndFigure(D2D1_FIGURE_END_CLOSED);
            }
            sink->Close();
        }
        float scx = w * 0.585f, scy = h * 0.58f, sr = 150;
        D2D1_MATRIX_3X2_F here;
        d->GetTransform(&here);
        d->SetTransform(D2D1::Matrix3x2F::Rotation(clock * 6) * D2D1::Matrix3x2F::Scale(sr, sr) * D2D1::Matrix3x2F::Translation(scx, scy) * here);
        b->SetColor(ink(0.85f));
        d->FillGeometry(star.Get(), b.Get());
        d->SetTransform(here);
        b->SetColor(ink(0.5f));
        d->DrawEllipse(D2D1::Ellipse(D2D1::Point2F(scx, scy), sr, sr), b.Get(), hair * 2);

        // rings (under Controls)
        float rcx = w * 0.84f, rcy = 250;
        for (int i = 1; i <= 22; i++) {
            b->SetColor(i % 5 == 0 ? Rgb(0, 122, 255, 0.9f) : ink(0.45f));
            d->DrawEllipse(D2D1::Ellipse(D2D1::Point2F(rcx, rcy), i * 9.0f, i * 9.0f), b.Get(), i % 5 == 0 ? hair * 3 : hair * 1.5f);
        }

        // a checkerboard (under Tune)
        float cx0 = w * 0.7f, cy0 = h * 0.56f;
        for (int r = 0; r < 18; r++)
            for (int c = 0; c < 24; c++) {
                if ((r + c) % 2) continue;
                b->SetColor((r / 6 + c / 6) % 2 ? ink(0.8f) : Rgb(255, 149, 0, 0.9f));
                d->FillRectangle(D2D1::RectF(cx0 + c * 16, cy0 + r * 16, cx0 + c * 16 + 16, cy0 + r * 16 + 16), b.Get());
            }

        // colour bars along the bottom (under the tab bar)
        static const D2D1_COLOR_F bars[] = { Rgb(255, 255, 255), Rgb(255, 204, 0), Rgb(48, 220, 230), Rgb(52, 199, 89), Rgb(255, 45, 140), Rgb(255, 59, 48), Rgb(0, 80, 255), Rgb(10, 10, 12) };
        float bw = (w * 0.5f) / 8, bx = w * 0.25f, by = h - 130;
        for (int i = 0; i < 8; i++) {
            b->SetColor(bars[i]);
            d->FillRectangle(D2D1::RectF(bx + i * bw, by, bx + (i + 1) * bw, by + 110), b.Get());
        }
        d->SetTransform(old);
    }

    // ---- Simpl ----

    void DrawSimpl(Gfx& g, float w, float h) {
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
