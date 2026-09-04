# Interactive ground-control-station window for the SHIELD emulation
# preview. Replaces the bare cv2.imshow annotated-frame window with a
# Tkinter panel matching the shield_gcs_ui.html design: status header,
# live video with HUD overlay, a clickable target list, and an
# engage/abort control with a degraded-track warning banner.

import time
import tkinter as tk
from dataclasses import dataclass

import cv2
from PIL import Image, ImageTk

import config
from object_detection import Detection

# --- Palette, taken directly from shield_gcs_ui.html ---
BG_OUTER = "#1a1f1f"
BG_PANEL = "#0b0f0f"
VIDEO_BG = "#121717"
TEXT_DIM = "#8fa3a3"
TEXT_BRIGHT = "#e8ece9"
TEXT_MUTED = "#c7d1d1"
GREEN = "#5DCAA5"
GREEN_BRIGHT = "#9FE1CB"
GREEN_BTN = "#1D9E75"
GREEN_BTN_TEXT = "#04342C"
ROW_BG = "#181f1f"
ROW_BORDER = "#263030"
ROW_SEL_BG = "#085041"
ROW_SEL_BORDER = "#5DCAA5"
BTN_IDLE_BG = "#263030"
RED = "#E24B4A"
RED_TEXT = "#F09595"
AMBER_BG = "#412402"
AMBER_BORDER = "#EF9F27"
AMBER_TEXT = "#FAC775"

FONT = "Segoe UI"
DISPLAY_WIDTH = 480

# AVS.03: the drone shall maintain >=25% confidence while en route to the
# target (the higher-confidence targeting system engages, then this floor
# gates an abort on a false positive if the track degrades). Reused here
# to gate the Engage button and to drive the "degraded" warning banner.
ENGAGE_CONFIDENCE_FLOOR = 0.25

# GNC-06: the drone shall not engage a target that has escaped line of
# sight for more than 30 frames - the same ByteTrack/Kalman coasting
# window the detector itself uses (config.TRACK_MAX_AGE), so a track
# dropped from this list here means the detector has dropped it too.
TRACK_CACHE_EXPIRE_UPDATES = config.TRACK_MAX_AGE

# No real time-to-intercept model exists yet, so this mirrors the
# mockup's demo countdown rather than claiming a computed value.
PLACEHOLDER_TTI_SEC = 47

# BGR (cv2 draws BGR, not RGB) versions of the palette above.
_BOX_COLOR_BGR = (165, 202, 93)      # GREEN
_MARKER_COLOR_BGR = (232, 236, 233)  # TEXT_BRIGHT
_RETICLE_COLOR_BGR = (190, 200, 195)


@dataclass
class _CacheEntry:
    detection: Detection
    updates_since_seen: int = 0


def _draw_hud(frame, detections):
    annotated = frame.copy()
    h, w = annotated.shape[:2]

    cx, cy = w // 2, h // 2
    cv2.line(annotated, (cx, cy - 18), (cx, cy + 18), _RETICLE_COLOR_BGR, 1, cv2.LINE_AA)
    cv2.line(annotated, (cx - 18, cy), (cx + 18, cy), _RETICLE_COLOR_BGR, 1, cv2.LINE_AA)
    cv2.circle(annotated, (cx, cy), 24, _RETICLE_COLOR_BGR, 1, cv2.LINE_AA)

    for det in detections:
        x1, y1, x2, y2 = (int(v) for v in det.bbox)
        cv2.rectangle(annotated, (x1, y1), (x2, y2), _BOX_COLOR_BGR, 1, cv2.LINE_AA)

        label = f"T-{det.track_id:02d} {det.confidence:.2f}"
        cv2.putText(
            annotated, label, (x1, max(10, y1 - 6)),
            cv2.FONT_HERSHEY_SIMPLEX, 0.45, _BOX_COLOR_BGR, 1, cv2.LINE_AA,
        )

        scx, scy = det.smoothed_center
        cv2.circle(annotated, (int(scx), int(scy)), 3, _MARKER_COLOR_BGR, 1, cv2.LINE_AA)

    return annotated


class SHIELDGCSWindow:
    def __init__(self, title="SHIELD - Emulation"):
        self.closed = False
        self._selected_id = None
        self._engaged = False
        self._tti_remaining = None
        self._tick_after_id = None
        self._error_after_id = None
        self._cache: dict[int, _CacheEntry] = {}
        self._photo = None  # keep a reference so Tk doesn't GC the image
        self._photo_size = None  # (w, h) of self._photo, to know when .paste() is safe
        self._last_raw_frame = None
        self._last_detections = []
        self._last_frame_time = None
        self._fps = 0.0

        self.root = tk.Tk()
        self.root.title(title)
        self.root.configure(bg=BG_OUTER)
        self.root.geometry("640x760")
        self.root.minsize(420, 480)
        self.root.resizable(True, True)
        self.root.protocol("WM_DELETE_WINDOW", self._request_close)
        self.root.bind("<Escape>", lambda _e: self._request_close())
        self.root.bind("q", lambda _e: self._request_close())
        self.root.bind("Q", lambda _e: self._request_close())

        outer = tk.Frame(self.root, bg=BG_OUTER, padx=12, pady=12)
        outer.pack(fill="both", expand=True)
        panel = tk.Frame(outer, bg=BG_PANEL, padx=8, pady=8)
        panel.pack(fill="both", expand=True)

        header = tk.Frame(panel, bg=BG_PANEL)
        header.pack(fill="x", pady=(0, 8))
        self.link_label = tk.Label(
            header, text="● Link stable", bg=BG_PANEL, fg=TEXT_DIM,
            font=(FONT, 10), anchor="w",
        )
        self.link_label.pack(side="left")
        self.fps_label = tk.Label(
            header, text="-- fps", bg=BG_PANEL, fg=TEXT_DIM, font=(FONT, 10), anchor="e",
        )
        self.fps_label.pack(side="right")
        self.mode_label = tk.Label(
            header, text="Tracking", bg=BG_PANEL, fg=GREEN, font=(FONT, 10, "bold"),
        )
        self.mode_label.pack(side="left", expand=True)

        self.video_canvas = tk.Canvas(
            panel, width=DISPLAY_WIDTH, height=360, bg=VIDEO_BG,
            highlightthickness=0,
        )
        self.video_canvas.pack(fill="both", expand=True, pady=(0, 8))
        self.video_canvas.bind("<Configure>", self._on_canvas_configure)
        self._image_id = self.video_canvas.create_image(0, 0, anchor="nw")
        self._tti_title_id = self.video_canvas.create_text(
            8, 8, anchor="nw", text="Time to intercept", fill=TEXT_MUTED,
            font=(FONT, 9),
        )
        self._tti_text_id = self.video_canvas.create_text(
            8, 22, anchor="nw", text="--:--", fill=TEXT_BRIGHT,
            font=(FONT, 18, "bold"), tags="tti_value",
        )
        self._lost_bg_id = self.video_canvas.create_rectangle(
            0, 0, 0, 0, fill=AMBER_BG, outline=AMBER_BORDER, state="hidden",
        )
        self._lost_text_id = self.video_canvas.create_text(
            0, 0, text="", fill=AMBER_TEXT, font=(FONT, 9), state="hidden",
        )

        tk.Label(
            panel, text="Targets", bg=BG_PANEL, fg=TEXT_DIM, font=(FONT, 9), anchor="w",
        ).pack(fill="x")
        self.list_frame = tk.Frame(panel, bg=BG_PANEL)
        self.list_frame.pack(fill="x", pady=(4, 0))

        self.engage_btn = tk.Button(
            panel, text="Select a target", font=(FONT, 12), bd=0,
            highlightthickness=0, relief="flat", height=2, cursor="hand2",
            command=self._on_engage_click,
        )
        self.engage_btn.pack(fill="x", pady=(12, 0))
        self.error_label = tk.Label(
            panel, text="", bg=BG_PANEL, fg=RED_TEXT, font=(FONT, 9),
        )

        self._update_button_state()
        self._render_list()

    # --- external API -----------------------------------------------
    def set_link_status(self, text, level="ok"):
        color = {"ok": GREEN, "warn": AMBER_TEXT, "bad": RED_TEXT}[level]
        self.link_label.config(text=f"● {text}", fg=color)

    def update_frame(self, frame, detections):
        self._update_fps()
        self._update_cache(detections)
        self._render_video(frame, detections)
        self._render_list()
        self._update_lost_banner()

    def pump(self):
        if self.closed:
            return
        try:
            self.root.update_idletasks()
            self.root.update()
        except tk.TclError:
            self.closed = True

    def destroy(self):
        if self._tick_after_id is not None:
            try:
                self.root.after_cancel(self._tick_after_id)
            except tk.TclError:
                pass
        try:
            self.root.destroy()
        except tk.TclError:
            pass

    # --- internals -----------------------------------------------
    def _request_close(self):
        self.closed = True

    def _update_fps(self):
        now = time.monotonic()
        if self._last_frame_time is not None:
            dt = now - self._last_frame_time
            if dt > 0:
                inst_fps = 1.0 / dt
                self._fps = inst_fps if self._fps == 0 else (0.9 * self._fps + 0.1 * inst_fps)
                self.fps_label.config(text=f"{self._fps:.0f} fps")
        self._last_frame_time = now

    def _update_cache(self, detections):
        for entry in self._cache.values():
            entry.updates_since_seen += 1
        for det in detections:
            self._cache[det.track_id] = _CacheEntry(detection=det, updates_since_seen=0)
        stale = [
            tid for tid, entry in self._cache.items()
            if entry.updates_since_seen > TRACK_CACHE_EXPIRE_UPDATES
        ]
        for tid in stale:
            del self._cache[tid]
        if self._selected_id is not None and self._selected_id not in self._cache:
            if not self._engaged:
                self._selected_id = None
                self._update_button_state()

    def _on_canvas_configure(self, _event):
        # Rescale immediately on resize/maximize even if no new frame has
        # arrived yet, so dragging the window edge feels live.
        if self._last_raw_frame is not None:
            self._render_video(self._last_raw_frame, self._last_detections)

    def _render_video(self, frame, detections):
        self._last_raw_frame = frame
        self._last_detections = detections

        annotated = _draw_hud(frame, detections)
        h, w = annotated.shape[:2]

        avail_w = self.video_canvas.winfo_width()
        avail_h = self.video_canvas.winfo_height()
        if avail_w < 2 or avail_h < 2:
            avail_w, avail_h = DISPLAY_WIDTH, round(DISPLAY_WIDTH * h / w)

        # Fit the source frame into whatever space the canvas currently has
        # (letterboxed, aspect preserved) rather than stretching to fill it.
        scale = min(avail_w / w, avail_h / h)
        disp_w = max(1, int(w * scale))
        disp_h = max(1, int(h * scale))
        off_x = (avail_w - disp_w) // 2
        off_y = (avail_h - disp_h) // 2

        # cv2.resize (SIMD-optimized) instead of PIL's LANCZOS - LANCZOS's
        # convolution cost scales with output pixel count. INTER_LINEAR is
        # effectively free by comparison and plenty for a live 30fps feed.
        resized = cv2.resize(annotated, (disp_w, disp_h), interpolation=cv2.INTER_LINEAR)
        rgb = cv2.cvtColor(resized, cv2.COLOR_BGR2RGB)
        image = Image.fromarray(rgb)

        # Reuse the PhotoImage via .paste() instead of constructing a new
        # one every frame - PhotoImage() re-registers a Tcl image object
        # each call, which is the actual bottleneck (more than the resize
        # ever was). Only rebuild it when the target size actually changes.
        if self._photo is None or self._photo_size != (disp_w, disp_h):
            self._photo = ImageTk.PhotoImage(image)
            self._photo_size = (disp_w, disp_h)
            self.video_canvas.itemconfig(self._image_id, image=self._photo)
        else:
            self._photo.paste(image)
        self.video_canvas.coords(self._image_id, off_x, off_y)

        # HUD overlays track the visible image bounds, not the raw canvas
        # corners, so they stay pinned to the video when letterboxed.
        self.video_canvas.coords(self._tti_title_id, off_x + 8, off_y + 8)
        self.video_canvas.coords(self._tti_text_id, off_x + 8, off_y + 22)
        self.video_canvas.coords(
            self._lost_bg_id, off_x + 6, off_y + disp_h - 30, off_x + disp_w - 6, off_y + disp_h - 6,
        )
        self.video_canvas.coords(self._lost_text_id, off_x + disp_w // 2, off_y + disp_h - 18)

    def _render_list(self):
        for child in self.list_frame.winfo_children():
            child.destroy()

        if not self._cache:
            tk.Label(
                self.list_frame, text="No targets", bg=BG_PANEL, fg=TEXT_DIM,
                font=(FONT, 10), anchor="w", pady=6,
            ).pack(fill="x")
            return

        for track_id in sorted(self._cache):
            entry = self._cache[track_id]
            det = entry.detection
            selected = track_id == self._selected_id
            live = entry.updates_since_seen == 0

            row = tk.Frame(
                self.list_frame,
                bg=ROW_SEL_BG if selected else ROW_BG,
                highlightbackground=ROW_SEL_BORDER if selected else ROW_BORDER,
                highlightthickness=1, cursor="hand2",
            )
            row.pack(fill="x", pady=3)

            left = tk.Label(
                row, text=f"T-{track_id:02d} · {det.class_name.title()}",
                bg=row["bg"], fg=GREEN_BRIGHT if selected else TEXT_MUTED,
                font=(FONT, 10), anchor="w", padx=10, pady=6,
            )
            left.pack(side="left")
            status = "live" if live else "coasting"
            right = tk.Label(
                row, text=f"{det.confidence * 100:.0f}% · {status}",
                bg=row["bg"], fg=GREEN_BRIGHT if selected else TEXT_MUTED,
                font=(FONT, 10), anchor="e", padx=10, pady=6,
            )
            right.pack(side="right")

            for widget in (row, left, right):
                widget.bind("<Button-1>", lambda _e, tid=track_id: self._select(tid))

    def _select(self, track_id):
        if self._engaged:
            return
        self._selected_id = track_id
        self._render_list()
        self._update_button_state()
        self._hide_error()

    def _update_button_state(self):
        if self._engaged:
            return
        if self._selected_id is not None:
            self.engage_btn.config(
                text=f"Engage T-{self._selected_id:02d}",
                bg=GREEN_BTN, fg=GREEN_BTN_TEXT, activebackground=GREEN_BTN,
                activeforeground=GREEN_BTN_TEXT, highlightthickness=0,
            )
        else:
            self.engage_btn.config(
                text="Select a target",
                bg=BTN_IDLE_BG, fg=TEXT_DIM, activebackground=BTN_IDLE_BG,
                activeforeground=TEXT_DIM, highlightthickness=0,
            )
        self._hide_error()

    def _on_engage_click(self):
        if self._engaged:
            self._do_abort()
            return
        if self._selected_id is None:
            self._show_error("Select a target before engaging.")
            return
        entry = self._cache.get(self._selected_id)
        if entry is None or entry.detection.confidence < ENGAGE_CONFIDENCE_FLOOR:
            self._show_error(
                f"Target confidence below {int(ENGAGE_CONFIDENCE_FLOOR * 100)}% "
                "— cannot engage (AVS.03)."
            )
            return
        self._start_engage()

    def _start_engage(self):
        self._engaged = True
        self._hide_error()
        self._tti_remaining = PLACEHOLDER_TTI_SEC
        self.video_canvas.itemconfig(self._tti_text_id, text=self._fmt_time(self._tti_remaining))
        self.mode_label.config(text="Engaged", fg=RED_TEXT)
        self.engage_btn.config(
            text="Abort", bg=RED, fg="#ffffff", activebackground=RED,
            activeforeground="#ffffff", highlightbackground="#ffffff",
            highlightthickness=2,
        )
        self._render_list()
        self._update_lost_banner()
        self._tick_after_id = self.root.after(1000, self._tick)
        print(f"[SHIELD] ENGAGE issued for track T-{self._selected_id:02d} (UI only; no effector wired up yet)")

    def _do_abort(self):
        self._engaged = False
        if self._tick_after_id is not None:
            self.root.after_cancel(self._tick_after_id)
            self._tick_after_id = None
        self._tti_remaining = None
        self.video_canvas.itemconfig(self._tti_text_id, text="--:--")
        self.mode_label.config(text="Tracking", fg=GREEN)
        self.engage_btn.config(highlightthickness=0)
        self._update_button_state()
        self._update_lost_banner()
        print("[SHIELD] ABORT")

    def _tick(self):
        if not self._engaged or self._tti_remaining is None:
            return
        self._tti_remaining = max(0, self._tti_remaining - 1)
        self.video_canvas.itemconfig(self._tti_text_id, text=self._fmt_time(self._tti_remaining))
        if self._tti_remaining > 0:
            self._tick_after_id = self.root.after(1000, self._tick)
        else:
            self._tick_after_id = None

    def _update_lost_banner(self):
        show = False
        text = ""
        if self._engaged and self._selected_id is not None:
            entry = self._cache.get(self._selected_id)
            if entry is None:
                show, text = True, "Target lost — verify before engaging"
            elif entry.updates_since_seen > 0 or entry.detection.confidence < ENGAGE_CONFIDENCE_FLOOR:
                show, text = True, "Track confidence degraded — verify before engaging"

        state = "normal" if show else "hidden"
        self.video_canvas.itemconfig(self._lost_bg_id, state=state)
        self.video_canvas.itemconfig(self._lost_text_id, state=state, text=text)

    def _show_error(self, text):
        self.error_label.config(text=text)
        self.error_label.pack(pady=(6, 0))
        if self._error_after_id is not None:
            self.root.after_cancel(self._error_after_id)
        self._error_after_id = self.root.after(3000, self._hide_error)

    def _hide_error(self):
        self._error_after_id = None
        self.error_label.pack_forget()

    @staticmethod
    def _fmt_time(seconds):
        m, s = divmod(int(seconds), 60)
        return f"{m:02d}:{s:02d}"
