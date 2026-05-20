#![windows_subsystem = "windows"]
#![allow(unsafe_op_in_unsafe_fn, unused_must_use)]

use std::sync::mpsc::{self, Sender};

use windows::{
    core::{w, BOOL as CoreBOOL},
    Win32::{
        Devices::Display::{
            DestroyPhysicalMonitors, GetMonitorBrightness,
            GetNumberOfPhysicalMonitorsFromHMONITOR, GetPhysicalMonitorsFromHMONITOR,
            SetMonitorBrightness, PHYSICAL_MONITOR,
        },
        Foundation::{COLORREF, HANDLE, HINSTANCE, HWND, LPARAM, LRESULT, RECT, WIN32_ERROR, WPARAM},
        System::Registry::{
            RegCloseKey, RegDeleteValueW, RegOpenKeyExW, RegSetValueExW,
            HKEY, HKEY_CURRENT_USER, KEY_SET_VALUE, REG_SZ,
        },
        Graphics::Gdi::{
            BeginPaint, CreateFontW, CreateSolidBrush, DeleteObject, Ellipse, EndPaint,
            EnumDisplayMonitors, FillRect, GetStockObject, HDC, HFONT, HMONITOR,
            InvalidateRect, NULL_PEN, RoundRect, SelectObject, SetBkMode, SetTextColor,
            TextOutW, BACKGROUND_MODE, CLIP_DEFAULT_PRECIS, DEFAULT_CHARSET, DEFAULT_PITCH,
            DEFAULT_QUALITY, FF_DONTCARE, OUT_DEFAULT_PRECIS, PAINTSTRUCT,
        },
        System::LibraryLoader::GetModuleHandleW,
        UI::{
            Input::KeyboardAndMouse::{
                MOD_ALT, MOD_NOREPEAT, RegisterHotKey, ReleaseCapture, SetCapture,
                UnregisterHotKey,
            },
            WindowsAndMessaging::{
                CreateWindowExW, DefWindowProcW, DispatchMessageW, MessageBoxW,
                MB_ICONERROR, MB_ICONINFORMATION, MB_OK,
                GetClientRect, GetMessageW, GetSystemMetrics, GetWindowLongPtrW,
                GetWindowRect, PostQuitMessage, RegisterClassW, SetLayeredWindowAttributes,
                SetWindowLongPtrW, SetWindowPos, ShowWindow, TranslateMessage,
                CS_HREDRAW, CS_VREDRAW, GWLP_USERDATA, HTCAPTION, HTCLIENT,
                IsWindowVisible, LWA_ALPHA, MSG, SetForegroundWindow, SM_CXSCREEN,
                SM_CYSCREEN, SW_HIDE, SW_SHOW, SWP_NOMOVE,
                SWP_NOSIZE, SWP_NOZORDER, WNDCLASSW, WS_EX_LAYERED, WS_POPUP,
                WM_DESTROY, WM_HOTKEY, WM_LBUTTONDOWN, WM_LBUTTONUP, WM_MOUSEMOVE,
                WM_NCHITTEST, WM_PAINT,
            },
        },
    },
};

// Colors — stored as BGR (Windows GDI format)
const BG: u32       = 0x1e0f0f;
const TITLEBAR: u32 = 0x3e1a1a;
const ACCENT: u32   = 0xf16363;
const TRACK: u32    = 0x4e2d2d;
const TEXT: u32     = 0xf8f0f0;
const DIMTXT: u32   = 0xb8a394;
const WHITE: u32    = 0xffffff;

// Layout constants (px)
const HOTKEY_ID: i32 = 1;

const WIN_W: i32   = 420;
const TITLE_H: i32 = 50;
const SECT_H: i32  = 110;
const PAD: i32     = 20;
const SLX0: i32    = PAD + 10;
const THUMB_R: i32 = 10;
const TRK_H: i32   = 8;

fn slx1(w: i32) -> i32 { w - PAD - 10 }
fn thumb_x(bri: u32, x0: i32, x1: i32) -> i32 { x0 + (bri as i32 * (x1 - x0)) / 100 }
fn bri_from_x(cx: i32, x0: i32, x1: i32) -> u32 {
    ((cx - x0).max(0).min(x1 - x0) * 100 / (x1 - x0).max(1)).max(1) as u32
}

struct MonitorInfo {
    // Raw handle stored as usize so it can be sent to the brightness thread.
    handle: HANDLE,
    name: String,
    brightness: u32,
    supported: bool,
    // Sending any value here queues a SetMonitorBrightness call on a background thread.
    bri_tx: Option<Sender<u32>>,
}

struct AppState {
    monitors: Vec<MonitorInfo>,
    dragging: Option<usize>,
    // Cached GDI fonts — created once, reused every WM_PAINT.
    f_title: HFONT,
    f_name: HFONT,
    f_pct: HFONT,
}

// ── Monitor enumeration ───────────────────────────────────────────────────────

unsafe extern "system" fn enum_monitor_cb(
    hmon: HMONITOR,
    _hdc: HDC,
    _rect: *mut RECT,
    lparam: LPARAM,
) -> CoreBOOL {
    let mons = &mut *(lparam.0 as *mut Vec<HMONITOR>);
    mons.push(hmon);
    CoreBOOL(1)
}

unsafe fn enumerate_monitors() -> Vec<MonitorInfo> {
    let mut hmons: Vec<HMONITOR> = Vec::new();
    EnumDisplayMonitors(
        None, None,
        Some(enum_monitor_cb),
        LPARAM(&mut hmons as *mut _ as isize),
    );

    let mut result = Vec::new();
    for (idx, hmon) in hmons.iter().enumerate() {
        let mut count = 0u32;
        if GetNumberOfPhysicalMonitorsFromHMONITOR(*hmon, &mut count).is_err() || count == 0 {
            continue;
        }
        let mut phys: Vec<PHYSICAL_MONITOR> = vec![core::mem::zeroed(); count as usize];
        if GetPhysicalMonitorsFromHMONITOR(*hmon, &mut phys).is_err() {
            continue;
        }
        for pm in phys {
            let mut min = 0u32;
            let mut cur = 50u32;
            let mut max = 100u32;
            let ok = GetMonitorBrightness(pm.hPhysicalMonitor, &mut min, &mut cur, &mut max);
            let supported = ok != 0;

            // Spawn a background thread that serialises SetMonitorBrightness calls
            // so the UI thread is never blocked by slow DDC/CI I2C communication.
            let bri_tx = if supported {
                let (tx, rx) = mpsc::channel::<u32>();
                let handle_raw = pm.hPhysicalMonitor.0 as usize;
                std::thread::spawn(move || {
                    loop {
                        // Block until at least one value arrives.
                        let mut val = match rx.recv() {
                            Ok(v) => v,
                            Err(_) => break, // sender dropped → window closed
                        };
                        // Drain any values queued during the last DDC/CI call;
                        // only apply the most recent one.
                        while let Ok(v) = rx.try_recv() {
                            val = v;
                        }
                        unsafe {
                            SetMonitorBrightness(
                                HANDLE(handle_raw as *mut core::ffi::c_void),
                                val,
                            );
                        }
                    }
                });
                Some(tx)
            } else {
                None
            };

            result.push(MonitorInfo {
                handle: pm.hPhysicalMonitor,
                name: format!("Monitor {}", idx + 1),
                brightness: if supported { cur.clamp(1, 100) } else { 0 },
                supported,
                bri_tx,
            });
        }
    }
    result
}

// ── Drawing ───────────────────────────────────────────────────────────────────

fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().collect()
}

unsafe fn draw_text(hdc: HDC, text: &str, x: i32, y: i32, color: u32) {
    SetTextColor(hdc, COLORREF(color));
    TextOutW(hdc, x, y, &wide(text));
}

unsafe fn make_font(size: i32, bold: bool) -> HFONT {
    CreateFontW(
        size, 0, 0, 0,
        if bold { 600 } else { 400 },
        0, 0, 0,
        DEFAULT_CHARSET,
        OUT_DEFAULT_PRECIS,
        CLIP_DEFAULT_PRECIS,
        DEFAULT_QUALITY,
        (DEFAULT_PITCH.0 | FF_DONTCARE.0) as u32,
        w!("Segoe UI"),
    )
}

fn close_btn_hit(cx: i32, cy: i32, win_w: i32) -> bool {
    cy < TITLE_H && cx > win_w - 50
}

fn slider_hit(cx: i32, cy: i32, idx: usize, win_w: i32) -> bool {
    let sy = TITLE_H + idx as i32 * SECT_H + PAD + 60;
    cx >= SLX0 - THUMB_R && cx <= slx1(win_w) + THUMB_R && (cy - sy).abs() <= THUMB_R * 2
}

unsafe fn on_paint(hwnd: HWND, state: &AppState) {
    let mut ps = PAINTSTRUCT::default();
    let hdc = BeginPaint(hwnd, &mut ps);

    let mut rc = RECT::default();
    GetClientRect(hwnd, &mut rc);
    let w = rc.right;

    SetBkMode(hdc, BACKGROUND_MODE(1)); // TRANSPARENT

    let bg_br = CreateSolidBrush(COLORREF(BG));
    FillRect(hdc, &rc, bg_br);
    DeleteObject(bg_br.into());

    let tb_br = CreateSolidBrush(COLORREF(TITLEBAR));
    let title_rect = RECT { left: 0, top: 0, right: w, bottom: TITLE_H };
    FillRect(hdc, &title_rect, tb_br);
    DeleteObject(tb_br.into());

    let prev_font = SelectObject(hdc, state.f_title.into());
    draw_text(hdc, "  Monitor Brightness", 10, 17, TEXT);
    draw_text(hdc, "\u{00D7}", w - 28, 17, DIMTXT);

    let null_pen = GetStockObject(NULL_PEN);

    for (i, m) in state.monitors.iter().enumerate() {
        let yb = TITLE_H + i as i32 * SECT_H + PAD;
        let sy = yb + 60;
        let x0 = SLX0;
        let x1 = slx1(w);
        let tx = thumb_x(m.brightness, x0, x1);

        SelectObject(hdc, state.f_name.into());
        draw_text(hdc, &m.name, x0, yb + 2, DIMTXT);

        if m.supported {
            SelectObject(hdc, state.f_pct.into());
            draw_text(hdc, &format!("{}%", m.brightness), x1 - 56, yb, ACCENT);

            SelectObject(hdc, null_pen);

            let trk_br = CreateSolidBrush(COLORREF(TRACK));
            SelectObject(hdc, trk_br.into());
            RoundRect(hdc, x0, sy - TRK_H / 2, x1, sy + TRK_H / 2, TRK_H, TRK_H);
            DeleteObject(trk_br.into());

            if tx > x0 + 2 {
                let acc_br = CreateSolidBrush(COLORREF(ACCENT));
                SelectObject(hdc, acc_br.into());
                RoundRect(hdc, x0, sy - TRK_H / 2, tx, sy + TRK_H / 2, TRK_H, TRK_H);
                DeleteObject(acc_br.into());
            }

            let wh_br = CreateSolidBrush(COLORREF(WHITE));
            SelectObject(hdc, wh_br.into());
            Ellipse(hdc, tx - THUMB_R, sy - THUMB_R, tx + THUMB_R, sy + THUMB_R);
            DeleteObject(wh_br.into());

            SelectObject(hdc, state.f_name.into());
            draw_text(hdc, "0", x0, sy + THUMB_R + 4, DIMTXT);
            draw_text(hdc, "100", x1 - 22, sy + THUMB_R + 4, DIMTXT);
        } else {
            draw_text(hdc, "DDC/CI not supported", x0, yb + 28, DIMTXT);
        }
    }

    SelectObject(hdc, prev_font);
    EndPaint(hwnd, &ps);
}

// ── Window procedure ──────────────────────────────────────────────────────────

unsafe extern "system" fn wnd_proc(
    hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_PAINT => {
            let ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut AppState;
            if !ptr.is_null() { on_paint(hwnd, &*ptr); }
            LRESULT(0)
        }

        WM_LBUTTONDOWN => {
            let cx = (lparam.0 & 0xffff) as i16 as i32;
            let cy = ((lparam.0 >> 16) & 0xffff) as i16 as i32;
            let mut crc = RECT::default();
            GetClientRect(hwnd, &mut crc);
            let win_w = crc.right;

            if close_btn_hit(cx, cy, win_w) {
                ShowWindow(hwnd, SW_HIDE);
                return LRESULT(0);
            }

            let ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut AppState;
            if !ptr.is_null() {
                let state = &mut *ptr;
                for i in 0..state.monitors.len() {
                    if state.monitors[i].supported && slider_hit(cx, cy, i, win_w) {
                        state.dragging = Some(i);
                        SetCapture(hwnd);
                        let bri = bri_from_x(cx, SLX0, slx1(win_w));
                        state.monitors[i].brightness = bri;
                        if let Some(tx) = &state.monitors[i].bri_tx {
                            let _ = tx.send(bri);
                        }
                        InvalidateRect(Some(hwnd), None, false);
                        return LRESULT(0);
                    }
                }
            }
            DefWindowProcW(hwnd, msg, wparam, lparam)
        }

        WM_MOUSEMOVE => {
            let ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut AppState;
            if !ptr.is_null() {
                let state = &mut *ptr;
                if let Some(i) = state.dragging {
                    let cx = (lparam.0 & 0xffff) as i16 as i32;
                    let mut crc = RECT::default();
                    GetClientRect(hwnd, &mut crc);
                    let bri = bri_from_x(cx, SLX0, slx1(crc.right));
                    if bri != state.monitors[i].brightness {
                        state.monitors[i].brightness = bri;
                        if let Some(tx) = &state.monitors[i].bri_tx {
                            let _ = tx.send(bri);
                        }
                        InvalidateRect(Some(hwnd), None, false);
                    }
                }
            }
            LRESULT(0)
        }

        WM_LBUTTONUP => {
            let ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut AppState;
            if !ptr.is_null() { (*ptr).dragging = None; }
            ReleaseCapture();
            LRESULT(0)
        }

        WM_NCHITTEST => {
            let res = DefWindowProcW(hwnd, msg, wparam, lparam);
            if res == LRESULT(HTCLIENT as isize) {
                let mut wrc = RECT::default();
                GetWindowRect(hwnd, &mut wrc);
                let sx = (lparam.0 & 0xffff) as i16 as i32;
                let sy = ((lparam.0 >> 16) & 0xffff) as i16 as i32;
                let lx = sx - wrc.left;
                let ly = sy - wrc.top;
                if ly >= 0 && ly < TITLE_H && lx < (wrc.right - wrc.left) - 50 {
                    return LRESULT(HTCAPTION as isize);
                }
            }
            res
        }

        WM_HOTKEY => {
            if wparam.0 as i32 == HOTKEY_ID {
                if IsWindowVisible(hwnd).as_bool() {
                    ShowWindow(hwnd, SW_HIDE);
                } else {
                    ShowWindow(hwnd, SW_SHOW);
                    SetForegroundWindow(hwnd);
                }
            }
            LRESULT(0)
        }

        WM_DESTROY => {
            UnregisterHotKey(Some(hwnd), HOTKEY_ID).ok();
            let ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut AppState;
            if !ptr.is_null() {
                let state = Box::from_raw(ptr);
                // Drop senders first — this closes channels and lets threads exit.
                for m in &state.monitors {
                    let pm = PHYSICAL_MONITOR {
                        hPhysicalMonitor: m.handle,
                        szPhysicalMonitorDescription: [0u16; 128],
                    };
                    DestroyPhysicalMonitors(&[pm]);
                }
                DeleteObject(state.f_title.into());
                DeleteObject(state.f_name.into());
                DeleteObject(state.f_pct.into());
                drop(state);
            }
            PostQuitMessage(0);
            LRESULT(0)
        }

        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

// ── Startup registration ──────────────────────────────────────────────────────

unsafe fn open_run_key() -> Option<HKEY> {
    let mut hkey = HKEY(core::ptr::null_mut());
    let err = RegOpenKeyExW(
        HKEY_CURRENT_USER,
        w!("Software\\Microsoft\\Windows\\CurrentVersion\\Run"),
        None,
        KEY_SET_VALUE,
        &mut hkey,
    );
    (err == WIN32_ERROR(0)).then_some(hkey)
}

unsafe fn install() {
    let exe = match std::env::current_exe() {
        Ok(p) => p,
        Err(_) => {
            MessageBoxW(None, w!("Could not get executable path."), w!("BrightnessCtrl"), MB_OK | MB_ICONERROR);
            return;
        }
    };

    // REG_SZ value: UTF-16 encoded path with null terminator, reinterpreted as bytes.
    let wide: Vec<u16> = exe.to_string_lossy().encode_utf16().chain(Some(0)).collect();
    let bytes = core::slice::from_raw_parts(wide.as_ptr() as *const u8, wide.len() * 2);

    let Some(hkey) = open_run_key() else {
        MessageBoxW(None, w!("Could not open registry key."), w!("BrightnessCtrl"), MB_OK | MB_ICONERROR);
        return;
    };
    let err = RegSetValueExW(hkey, w!("BrightnessCtrl"), None, REG_SZ, Some(bytes));
    RegCloseKey(hkey);

    if err == WIN32_ERROR(0) {
        MessageBoxW(None, w!("Added to startup.\r\nPress Alt+B to show/hide the window."), w!("BrightnessCtrl"), MB_OK | MB_ICONINFORMATION);
    } else {
        MessageBoxW(None, w!("Failed to write registry value."), w!("BrightnessCtrl"), MB_OK | MB_ICONERROR);
    }
}

unsafe fn uninstall() {
    let Some(hkey) = open_run_key() else {
        MessageBoxW(None, w!("Could not open registry key."), w!("BrightnessCtrl"), MB_OK | MB_ICONERROR);
        return;
    };
    let err = RegDeleteValueW(hkey, w!("BrightnessCtrl"));
    RegCloseKey(hkey);

    if err == WIN32_ERROR(0) {
        MessageBoxW(None, w!("Removed from startup."), w!("BrightnessCtrl"), MB_OK | MB_ICONINFORMATION);
    } else {
        MessageBoxW(None, w!("Not found in startup (may not have been installed)."), w!("BrightnessCtrl"), MB_OK | MB_ICONERROR);
    }
}

// ── Entry point ───────────────────────────────────────────────────────────────

fn main() {
    match std::env::args().nth(1).as_deref() {
        Some("--install")   => { unsafe { install(); }   return; }
        Some("--uninstall") => { unsafe { uninstall(); } return; }
        _ => {}
    }

    unsafe {
        let hmod = GetModuleHandleW(None).unwrap();
        let hinstance = HINSTANCE(hmod.0);

        let wc = WNDCLASSW {
            style: CS_HREDRAW | CS_VREDRAW,
            lpfnWndProc: Some(wnd_proc),
            hInstance: hinstance,
            lpszClassName: w!("BrightnessCtrl"),
            ..Default::default()
        };
        RegisterClassW(&wc);

        let monitors = enumerate_monitors();
        let count = monitors.len().max(1);
        let win_h = TITLE_H + count as i32 * SECT_H + PAD;

        let sx = GetSystemMetrics(SM_CXSCREEN);
        let sy = GetSystemMetrics(SM_CYSCREEN);

        let state = Box::new(AppState {
            monitors,
            dragging: None,
            f_title: make_font(16, true),
            f_name: make_font(13, false),
            f_pct: make_font(22, true),
        });
        let state_ptr = Box::into_raw(state);

        let hwnd = CreateWindowExW(
            WS_EX_LAYERED,
            w!("BrightnessCtrl"),
            w!("Monitor Brightness"),
            WS_POPUP,
            (sx - WIN_W) / 2,
            (sy - win_h) / 2,
            WIN_W, win_h,
            None, None,
            Some(hinstance),
            None,
        )
        .unwrap();

        SetWindowLongPtrW(hwnd, GWLP_USERDATA, state_ptr as isize);
        SetLayeredWindowAttributes(hwnd, COLORREF(0), 245, LWA_ALPHA).ok();
        RegisterHotKey(Some(hwnd), HOTKEY_ID, MOD_ALT | MOD_NOREPEAT, b'B' as u32).ok();
        ShowWindow(hwnd, SW_SHOW);
        SetWindowPos(hwnd, None, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER).ok();

        let mut msg = MSG::default();
        while GetMessageW(&mut msg, None, 0, 0).as_bool() {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
}
