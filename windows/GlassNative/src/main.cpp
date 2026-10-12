// Liquid Glass for Windows — Simpl's showcase, native: a Win32 window, Direct3D 11 with AuroraGlass for the glass,
// Direct2D and DirectWrite for the backdrop and the words. A frame is drawn each time the display refreshes (vsync).
// --shot file.png: a picture of the frame after a few seconds (and file.png.txt: the graphics card and the frame rate),
// then it quits — for CI.
#include "Gfx.h"
#include "Showcase.h"
#include <wincodec.h>
#include <shellapi.h>
#include <dwmapi.h>
#include <shellscalingapi.h>
#include <cstdio>
#include <string>

static Gfx g;
static Showcase* show = nullptr;
static bool tracking = false;
static float forcedDpi = 0; // (--dpi 192: drawn as on a display scaled 200%, for CI's picture)

static float Unit() { return g.Scale() * (show ? show->uiScale : 1.0f); }

static void FitScale() {
    if (!show) return;
    float W = g.width / g.Scale(), H = g.height / g.Scale();
    show->uiScale = std::min(1.0f, std::min(W / 1320.0f, H / 840.0f));
}

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_SIZE:
        if (show && wp != SIZE_MINIMIZED) {
            g.Resize(LOWORD(lp), HIWORD(lp));
            show->Resized();
            FitScale();
        }
        return 0;
    case WM_DPICHANGED: {
        if (!forcedDpi) g.Rescale((float)HIWORD(wp));
        if (show) { show->Resized(); FitScale(); }
        auto* r = (RECT*)lp;
        SetWindowPos(hwnd, nullptr, r->left, r->top, r->right - r->left, r->bottom - r->top, SWP_NOZORDER | SWP_NOACTIVATE);
        return 0;
    }
    case WM_MOUSEMOVE:
        if (show) {
            show->mx = (short)LOWORD(lp) / Unit();
            show->my = (short)HIWORD(lp) / Unit();
            if (!tracking) {
                TRACKMOUSEEVENT t = { sizeof(t), TME_LEAVE, hwnd, 0 };
                TrackMouseEvent(&t);
                tracking = true;
            }
        }
        return 0;
    case WM_MOUSELEAVE:
        tracking = false;
        if (show && !show->down) show->mx = show->my = -1;
        return 0;
    case WM_LBUTTONDOWN:
        if (show) {
            show->mx = (short)LOWORD(lp) / Unit();
            show->my = (short)HIWORD(lp) / Unit();
            show->down = show->pressed = true;
            SetCapture(hwnd);
        }
        return 0;
    case WM_LBUTTONUP:
        if (show) {
            show->down = false;
            show->released = true;
            ReleaseCapture();
        }
        return 0;
    case WM_KEYDOWN:
        if (wp == VK_ESCAPE) DestroyWindow(hwnd);
        return 0;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

/// The frame as it is, to a PNG (Windows Imaging Component).
static bool SavePng(const std::wstring& file) {
    ComPtr<ID3D11Texture2D> back;
    g.swap->GetBuffer(0, IID_PPV_ARGS(&back));
    D3D11_TEXTURE2D_DESC d;
    back->GetDesc(&d);
    d.Usage = D3D11_USAGE_STAGING;
    d.BindFlags = 0;
    d.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    d.MiscFlags = 0;
    ComPtr<ID3D11Texture2D> staging;
    if (FAILED(g.device->CreateTexture2D(&d, nullptr, &staging))) return false;
    g.ctx->CopyResource(staging.Get(), back.Get());
    D3D11_MAPPED_SUBRESOURCE m;
    if (FAILED(g.ctx->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &m))) return false;
    ComPtr<IWICImagingFactory> wic;
    CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic));
    ComPtr<IWICStream> stream;
    ComPtr<IWICBitmapEncoder> enc;
    ComPtr<IWICBitmapFrameEncode> frame;
    bool ok = wic && SUCCEEDED(wic->CreateStream(&stream)) && SUCCEEDED(stream->InitializeFromFilename(file.c_str(), GENERIC_WRITE))
        && SUCCEEDED(wic->CreateEncoder(GUID_ContainerFormatPng, nullptr, &enc)) && SUCCEEDED(enc->Initialize(stream.Get(), WICBitmapEncoderNoCache))
        && SUCCEEDED(enc->CreateNewFrame(&frame, nullptr)) && SUCCEEDED(frame->Initialize(nullptr)) && SUCCEEDED(frame->SetSize(d.Width, d.Height));
    if (ok) {
        WICPixelFormatGUID fmt = GUID_WICPixelFormat32bppBGRA;
        frame->SetPixelFormat(&fmt);
        // (the frame is opaque: its alpha set to full, row by row)
        std::vector<BYTE> rows((size_t)d.Width * 4 * d.Height);
        for (UINT y = 0; y < d.Height; y++) {
            memcpy(&rows[(size_t)y * d.Width * 4], (BYTE*)m.pData + (size_t)y * m.RowPitch, (size_t)d.Width * 4);
            for (UINT x = 0; x < d.Width; x++) rows[((size_t)y * d.Width + x) * 4 + 3] = 255;
        }
        ok = SUCCEEDED(frame->WritePixels(d.Height, d.Width * 4, (UINT)rows.size(), rows.data())) && SUCCEEDED(frame->Commit()) && SUCCEEDED(enc->Commit());
    }
    g.ctx->Unmap(staging.Get(), 0);
    return ok;
}

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE, PWSTR, int) {
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    int argc = 0;
    wchar_t** argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    std::wstring shot;
    for (int i = 1; i + 1 < argc; i++) {
        if (std::wstring(argv[i]) == L"--shot") shot = argv[i + 1];
        if (std::wstring(argv[i]) == L"--dpi") forcedDpi = (float)_wtof(argv[i + 1]);
    }

    WNDCLASSEXW wc = { sizeof(wc) };
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = WndProc;
    wc.hInstance = inst;
    wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
    wc.hIcon = LoadIconW(inst, MAKEINTRESOURCEW(1));
    wc.lpszClassName = L"SimplLiquidGlass";
    RegisterClassExW(&wc);

    // the window: 1360 × 880 (scaled for the display), never larger than the screen's work area
    UINT sysDpi = GetDpiForSystem();
    RECT work;
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
    int ww = std::min<int>(MulDiv(1360, sysDpi, 96), work.right - work.left - 20);
    int wh = std::min<int>(MulDiv(880, sysDpi, 96), work.bottom - work.top - 20);
    HWND hwnd = CreateWindowExW(0, wc.lpszClassName, L"Liquid Glass for Windows — Simpl", WS_OVERLAPPEDWINDOW,
        work.left + (work.right - work.left - ww) / 2, work.top + (work.bottom - work.top - wh) / 2, ww, wh, nullptr, nullptr, inst, nullptr);
    BOOL darkFrame = TRUE;
    DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, &darkFrame, sizeof(darkFrame));

    if (!g.Init(hwnd)) {
        MessageBoxW(nullptr, L"Direct3D 11 could not be started.", L"Liquid Glass", MB_ICONERROR);
        return 1;
    }
    if (forcedDpi > 0) g.Rescale(forcedDpi);
    static Showcase s(g);
    show = &s;
    FitScale();
    if (!s.Init()) MessageBoxW(hwnd, L"AuroraGlass could not start (its shaders folder must be beside the app).", L"Liquid Glass", MB_ICONWARNING);
    ShowWindow(hwnd, SW_SHOW);

    LARGE_INTEGER freq, last, start;
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&last);
    start = last;
    int frames = 0;
    double fpsClock = 0, fps = 0;
    MSG msg = {};
    bool running = true;
    while (running) {
        while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
            if (msg.message == WM_QUIT) running = false;
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        if (!running) break;
        LARGE_INTEGER t;
        QueryPerformanceCounter(&t);
        float dt = (float)((t.QuadPart - last.QuadPart) / (double)freq.QuadPart);
        last = t;
        dt = std::min(dt, 1.0f / 15);
        s.Draw(dt);
        frames++;
        fpsClock += dt;
        if (fpsClock >= 1.0) {
            fps = frames / fpsClock;
            frames = 0;
            fpsClock = 0;
            wchar_t title[128];
            swprintf(title, 128, L"Liquid Glass for Windows — Simpl  (%.0f fps)", fps);
            SetWindowTextW(hwnd, title);
        }
        double since = (t.QuadPart - start.QuadPart) / (double)freq.QuadPart;
        if (!shot.empty() && since > 7) {
            bool ok = SavePng(shot);
            DXGI_ADAPTER_DESC ad = {};
            ComPtr<IDXGIDevice> dd;
            g.device.As(&dd);
            ComPtr<IDXGIAdapter> a;
            if (dd) dd->GetAdapter(&a);
            if (a) a->GetDesc(&ad);
            FILE* f = _wfopen((shot + L".txt").c_str(), L"w");
            if (f) { fwprintf(f, L"saved=%d glass=%d adapter=%s fps=%.1f size=%ux%u\n", ok ? 1 : 0, s.ready ? 1 : 0, ad.Description, fps, g.width, g.height); fclose(f); }
            break;
        }
        g.swap->Present(1, 0);
    }
    return 0;
}
