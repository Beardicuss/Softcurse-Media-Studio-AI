#!/usr/bin/env python3
"""
Universal Audio & Video Converter
Supports all common audio and video formats with quality/resolution controls.
Requires: Python 3.7+  +  ffmpeg in PATH
DnD:      pip install tkinterdnd2  (optional)
"""

import os
import shutil
import subprocess
import threading
from concurrent.futures import ThreadPoolExecutor
from typing import Optional, Dict, List, Tuple
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

try:
    from tkinterdnd2 import TkinterDnD, DND_FILES
    _DND_AVAILABLE = True
except ImportError:
    _DND_AVAILABLE = False

# ── Format definitions ────────────────────────────────────────────────────────

AUDIO_FORMATS = ["mp3", "wav", "flac", "aac", "ogg", "opus", "m4a", "wma", "aiff", "ac3"]
VIDEO_FORMATS = ["mp4", "mkv", "avi", "mov", "webm", "ogv", "wmv", "flv", "m4v", "ts", "3gp"]

ALL_INPUT_EXTS = set("." + f for f in AUDIO_FORMATS + VIDEO_FORMATS)
AUDIO_EXTS     = set("." + f for f in AUDIO_FORMATS)
VIDEO_EXTS     = set("." + f for f in VIDEO_FORMATS)

# ffmpeg codec map: output_format -> (audio_codec, video_codec_or_None)
CODEC_MAP: Dict[str, Tuple[str, Optional[str]]] = {
    # audio-only output formats
    "mp3":  ("libmp3lame", None),
    "wav":  ("pcm_s16le",  None),
    "flac": ("flac",       None),
    "aac":  ("aac",        None),
    "ogg":  ("libvorbis",  None),
    "opus": ("libopus",    None),
    "m4a":  ("aac",        None),
    "wma":  ("wmav2",      None),
    "aiff": ("pcm_s16be",  None),
    "ac3":  ("ac3",        None),
    # video output formats  (audio_codec, video_codec)
    "mp4":  ("aac",        "libx264"),
    "mkv":  ("aac",        "libx264"),
    "avi":  ("libmp3lame", "mpeg4"),
    "mov":  ("aac",        "libx264"),
    "webm": ("libvorbis",  "libvpx-vp9"),
    "ogv":  ("libvorbis",  "libtheora"),
    "wmv":  ("wmav2",      "wmv2"),
    "flv":  ("aac",        "libx264"),   # H.264+AAC — modern FLV compat
    "m4v":  ("aac",        "libx264"),
    "ts":   ("aac",        "libx264"),
    "3gp":  ("aac",        "libx264"),
}

VIDEO_RESOLUTIONS = [
    "Original",
    "3840:2160 (4K)",
    "1920:1080 (1080p)",
    "1280:720 (720p)",
    "854:480 (480p)",
    "640:360 (360p)",
]
VIDEO_QUALITIES = ["High (CRF 18)", "Medium (CRF 23)", "Low (CRF 28)", "Very Low (CRF 35)"]
CRF_MAP         = {"High (CRF 18)": "18", "Medium (CRF 23)": "23",
                   "Low (CRF 28)": "28",  "Very Low (CRF 35)": "35"}
# Theora uses quality 0-10 (higher = better); map from CRF values
THEORA_Q_MAP    = {"18": "10", "23": "7", "28": "4", "35": "1"}

AUDIO_BITRATES    = ["320k", "256k", "192k", "128k", "96k", "64k"]
# Note: opus only supports 8000/12000/16000/24000/48000 — enforced in build_ffmpeg_cmd
AUDIO_SAMPLERATES       = ["44100", "48000", "22050", "16000", "8000"]
OPUS_VALID_SAMPLERATES  = {"8000", "12000", "16000", "24000", "48000"}
AUDIO_CHANNELS    = {"Stereo": "2", "Mono": "1"}

# Lossless/PCM codecs where -b:a is meaningless — skip it
NO_BITRATE_CODECS = {"pcm_s16le", "pcm_s16be", "flac"}

MAX_WORKERS = min(4, os.cpu_count() or 4)

# ── Process registry for clean shutdown ──────────────────────────────────────

_procs_lock = threading.Lock()
_active_procs: List[subprocess.Popen] = []

def _reg(p):
    with _procs_lock: _active_procs.append(p)

def _unreg(p):
    with _procs_lock:
        try: _active_procs.remove(p)
        except ValueError: pass

def kill_all():
    with _procs_lock:
        for p in list(_active_procs):
            try: p.terminate()
            except Exception: pass
        _active_procs.clear()

# ── Helpers ───────────────────────────────────────────────────────────────────

def normalise_path(p: str) -> str:
    return os.path.normpath(os.path.expanduser(p.strip()))

def scan_folder(folder: str) -> List[str]:
    found = []
    for root, dirs, files in os.walk(folder):
        dirs.sort()
        for f in sorted(files):
            if os.path.splitext(f)[1].lower() in ALL_INPUT_EXTS:
                found.append(os.path.join(root, f))
    return found

def parse_drop_data(raw: str) -> List[str]:
    paths, raw, i = [], raw.strip(), 0
    while i < len(raw):
        if raw[i] == "{":
            try: end = raw.index("}", i)
            except ValueError:
                nxt = raw.find(" ", i + 1)
                i = nxt + 1 if nxt != -1 else len(raw)
                continue
            paths.append(raw[i + 1:end]); i = end + 2
        else:
            end = raw.find(" ", i)
            if end == -1: paths.append(raw[i:]); break
            paths.append(raw[i:end]); i = end + 1
    return [p for p in paths if p]

def build_dst_map(sources: List[str], out_dir: str, out_fmt: str) -> Dict[str, str]:
    reserved: set = set()
    mapping: Dict[str, str] = {}
    for src in sources:
        raw_stem = os.path.splitext(os.path.basename(src))[0]
        # FIX: guard against empty stem (e.g. file named ".hidden")
        stem = raw_stem if raw_stem else "_unnamed"
        candidate = os.path.join(out_dir, stem + "." + out_fmt)
        idx = 1
        while os.path.exists(candidate) or candidate in reserved:
            candidate = os.path.join(out_dir, f"{stem}_{idx}.{out_fmt}")
            idx += 1
        reserved.add(candidate)
        mapping[src] = candidate
    return mapping

def best_error(stderr: str) -> str:
    if not stderr: return "Unknown error"
    lines = stderr.strip().splitlines()
    priority = [l for l in lines if any(
        kw in l for kw in ("Error", "error", "Invalid", "No such",
                           "not found", "failed", "Failed"))]
    return (priority[-1].strip() if priority
            else "  |  ".join(l.strip() for l in lines[-2:] if l.strip()))

def is_video_src(src: str) -> bool:
    return os.path.splitext(src)[1].lower() in VIDEO_EXTS   # set O(1) lookup

# ── Conversion command builder ────────────────────────────────────────────────

def build_ffmpeg_cmd(src: str, dst: str, out_fmt: str, settings: dict) -> List[str]:
    """
    Build a correct ffmpeg command for all four conversion cases:
      1. audio → audio  (transcode audio stream only)
      2. video → audio  (extract + transcode audio stream, drop video)
      3. video → video  (transcode both streams with quality/resolution settings)
      4. audio → video  (wrap audio in video container — audio-only video file)
    """
    acodec, vcodec = CODEC_MAP.get(out_fmt, ("aac", None))
    src_is_video   = is_video_src(src)
    dst_is_video   = vcodec is not None   # output format has a video codec

    cmd = ["ffmpeg", "-y", "-i", src]

    if dst_is_video and src_is_video:
        # ── Case 3: video → video ─────────────────────────────────────────────
        cmd += ["-c:v", vcodec]

        # Resolution with aspect ratio preserved + even-dimension enforcement
        res = settings.get("resolution", "Original")
        if res != "Original":
            w_h = res.split(" ")[0]   # e.g. "1920:1080"
            # scale down preserving AR, letterbox-pad to target, force even dims
            cmd += ["-vf",
                    f"scale={w_h}:force_original_aspect_ratio=decrease,"
                    f"pad={w_h}:(ow-iw)/2:(oh-ih)/2,"
                    f"scale=trunc(ow/2)*2:trunc(oh/2)*2"]

        # Quality — each codec family uses a different quality control flag
        crf = CRF_MAP.get(settings.get("quality", "Medium (CRF 23)"), "23")
        if vcodec in ("libx264", "libx265"):
            cmd += ["-crf", crf, "-preset", "medium"]
        elif vcodec == "libvpx-vp9":
            # VP9 constant quality mode requires -b:v 0
            cmd += ["-crf", crf, "-b:v", "0"]
        elif vcodec == "libtheora":
            cmd += ["-q:v", THEORA_Q_MAP.get(crf, "7")]
        elif vcodec in ("wmv2", "mpeg4"):
            # wmv2 / mpeg4 use -q:v (1-31, lower = better quality)
            # Map CRF 18→2, 23→6, 28→14, 35→24 (logarithmic-ish)
            qv_map = {"18": "2", "23": "6", "28": "14", "35": "24"}
            cmd += ["-q:v", qv_map.get(crf, "6")]

        # Audio track — use vid_audio_bitrate from settings
        cmd += ["-c:a", acodec,
                "-b:a", settings.get("vid_audio_bitrate", "192k")]

    elif dst_is_video and not src_is_video:
        # ── Case 4: audio → video container ──────────────────────────────────
        # Wrap audio in container with no video stream (valid, common for .mp4/.mkv)
        # Do NOT add -c:v copy — there is no video stream in source
        # Use vid_audio_bitrate — that's the visible setting when Video mode is active
        cmd += ["-vn",        # explicitly no video output
                "-c:a", acodec]
        if acodec not in NO_BITRATE_CODECS:
            cmd += ["-b:a", settings.get("vid_audio_bitrate", "192k")]
        cmd += ["-ar", settings.get("samplerate", "44100"),
                "-ac", settings.get("channels", "2")]

    else:
        # ── Case 1 & 2: *→ audio (audio→audio or video→audio) ────────────────
        cmd += ["-vn",         # drop any video stream
                "-c:a", acodec]
        if acodec not in NO_BITRATE_CODECS:
            cmd += ["-b:a", settings.get("audio_bitrate", "192k")]
        sr = settings.get("samplerate", "44100")
        # Opus only accepts specific sample rates — clamp to nearest valid value
        if acodec == "libopus" and sr not in OPUS_VALID_SAMPLERATES:
            sr = "48000"
        cmd += ["-ar", sr,
                "-ac", settings.get("channels", "2")]

    cmd.append(dst)
    return cmd

# ── Conversion worker ─────────────────────────────────────────────────────────

def convert_file(src: str, dst: str, out_fmt: str, settings: dict, callback):
    cmd  = build_ffmpeg_cmd(src, dst, out_fmt, settings)
    proc = None
    try:
        proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        _reg(proc)
        _, stderr = proc.communicate()
        _unreg(proc)
        if proc.returncode == 0:
            callback(True, f"Saved: {os.path.basename(dst)}")
        else:
            callback(False, f"Error: {best_error(stderr.decode(errors='replace'))}")
    except FileNotFoundError:
        callback(False, "ffmpeg not found — install from ffmpeg.org")
    except Exception as exc:
        callback(False, str(exc))
    finally:
        if proc: _unreg(proc)

# ── GUI ───────────────────────────────────────────────────────────────────────

_BASE = TkinterDnD.Tk if _DND_AVAILABLE else tk.Tk


class ConverterApp(_BASE):
    BG      = "#0f0f13"
    PANEL   = "#1a1a22"
    PANEL2  = "#22222e"
    ACCENT  = "#00d4aa"
    FG      = "#e8e8f0"
    MUTED   = "#6b6b88"
    WARN    = "#ff6b35"
    ERR     = "#ff4466"
    DROP_HL = "#1e2a28"
    FONT_S  = ("Consolas", 9)
    FONT_T  = ("Consolas", 13, "bold")

    def __init__(self):
        super().__init__()
        self.title("Universal Converter")
        self.resizable(True, True)
        self.minsize(700, 580)
        self.configure(bg=self.BG)

        self._files_list: List[str]                    = []
        self._files_set:  set                          = set()
        self._running:    bool                         = False
        self._ffmpeg_ok:  bool                         = bool(shutil.which("ffmpeg"))
        self._lock                                     = threading.Lock()
        self._executor:   Optional[ThreadPoolExecutor] = None
        self._scan_count: int                          = 0  # active async scans

        self._build_ui()
        self._apply_out_border()
        self._center()
        self._warn_ffmpeg_startup()
        self.protocol("WM_DELETE_WINDOW", self._on_close)

    # ── Startup ───────────────────────────────────────────────────────────────

    def _warn_ffmpeg_startup(self):
        """Show ffmpeg warning once at startup only."""
        if not self._ffmpeg_ok:
            messagebox.showwarning(
                "ffmpeg not found",
                "ffmpeg was not found in your PATH.\n\n"
                "Install: https://ffmpeg.org/download.html\nthen restart.")
            self._convert_btn.config(state="disabled")

    def _on_close(self):
        if self._running:
            if self._executor:
                self._executor.shutdown(cancel_futures=True, wait=False)
            kill_all()
        try:
            self.destroy()
        except Exception:
            pass  # guard against TclError if child dialogs are open (macOS)

    # ── UI ────────────────────────────────────────────────────────────────────

    def _build_ui(self):
        hdr = tk.Frame(self, bg=self.PANEL, height=52)
        hdr.pack(fill="x")
        hdr.pack_propagate(False)
        tk.Label(hdr, text="⬡  UNIVERSAL CONVERTER",
                 font=self.FONT_T, bg=self.PANEL,
                 fg=self.ACCENT, padx=20).pack(side="left", pady=12)
        tk.Label(hdr, text="audio · video · all formats  ",
                 font=("Consolas", 9), bg=self.PANEL,
                 fg=self.MUTED).pack(side="right", pady=12)

        body = tk.Frame(self, bg=self.BG)
        body.pack(fill="both", expand=True, padx=14, pady=10)
        body.columnconfigure(0, weight=3)
        body.columnconfigure(1, weight=2)
        body.rowconfigure(0, weight=1)

        self._build_file_panel(body)
        self._build_settings_panel(body)
        self._build_bottom_bar()

    def _build_file_panel(self, parent):
        frame = tk.Frame(parent, bg=self.PANEL, padx=12, pady=10)
        frame.grid(row=0, column=0, sticky="nsew", padx=(0, 6))
        frame.rowconfigure(2, weight=1)
        frame.columnconfigure(0, weight=1)

        tk.Label(frame, text="INPUT FILES", font=self.FONT_S,
                 bg=self.PANEL, fg=self.MUTED).grid(row=0, column=0, sticky="w")

        dnd_hint = ("Drop files or folders here"
                    if _DND_AVAILABLE else "Use buttons below to add files")
        self._drop_frame = tk.Frame(frame, bg=self.DROP_HL,
                                    highlightthickness=1,
                                    highlightbackground=self.MUTED)
        self._drop_frame.grid(row=1, column=0, sticky="ew", pady=(4, 6))
        self._drop_lbl = tk.Label(self._drop_frame, text=f"⬇  {dnd_hint}",
                                  font=("Consolas", 9, "italic"),
                                  bg=self.DROP_HL, fg=self.MUTED, pady=6)
        self._drop_lbl.pack()
        if _DND_AVAILABLE:
            for w in (self._drop_frame, self._drop_lbl):
                w.drop_target_register(DND_FILES)
                w.dnd_bind("<<Drop>>",      self._on_drop)
                w.dnd_bind("<<DragEnter>>", self._on_drag_enter)
                w.dnd_bind("<<DragLeave>>", self._on_drag_leave)

        lf = tk.Frame(frame, bg=self.PANEL2)
        lf.grid(row=2, column=0, sticky="nsew")
        sb = tk.Scrollbar(lf, bg=self.PANEL2, troughcolor=self.PANEL2, bd=0)
        sb.pack(side="right", fill="y")
        self.listbox = tk.Listbox(lf, font=self.FONT_S,
                                  bg=self.PANEL2, fg=self.FG,
                                  selectbackground=self.ACCENT,
                                  selectforeground=self.BG,
                                  activestyle="none", bd=0,
                                  highlightthickness=0, height=12,
                                  yscrollcommand=sb.set)
        self.listbox.pack(side="left", fill="both", expand=True, padx=4, pady=4)
        sb.config(command=self.listbox.yview)

        self._type_var = tk.StringVar(value="")
        tk.Label(frame, textvariable=self._type_var,
                 font=self.FONT_S, bg=self.PANEL,
                 fg=self.MUTED).grid(row=3, column=0, sticky="w", pady=(4, 0))

        btn_row = tk.Frame(frame, bg=self.PANEL)
        btn_row.grid(row=4, column=0, sticky="ew", pady=(6, 0))
        self._btn_add    = self._btn(btn_row, "+ Files",   self._add_files)
        self._btn_folder = self._btn(btn_row, "📁 Folder", self._add_folder)
        self._btn_remove = self._btn(btn_row, "✕ Remove",  self._remove_selected)
        self._btn_clear  = self._btn(btn_row, "⊘ Clear",   self._clear_files)
        self._btn_add.pack(   side="left", padx=(0, 4))
        self._btn_folder.pack(side="left", padx=(0, 4))
        self._btn_remove.pack(side="left", padx=(0, 4))
        self._btn_clear.pack( side="left")

    def _build_settings_panel(self, parent):
        frame = tk.Frame(parent, bg=self.PANEL, padx=14, pady=10)
        frame.grid(row=0, column=1, sticky="nsew")
        frame.columnconfigure(0, weight=1)

        tk.Label(frame, text="SETTINGS", font=self.FONT_S,
                 bg=self.PANEL, fg=self.MUTED).grid(row=0, column=0, sticky="w")

        self._section(frame, "OUTPUT FORMAT", row=1)

        fmt_outer = tk.Frame(frame, bg=self.PANEL)
        fmt_outer.grid(row=2, column=0, sticky="ew", pady=(2, 8))
        fmt_outer.columnconfigure(0, weight=1)
        fmt_outer.columnconfigure(1, weight=1)

        tk.Label(fmt_outer, text="Type",   font=self.FONT_S,
                 bg=self.PANEL, fg=self.MUTED).grid(row=0, column=0, sticky="w")
        tk.Label(fmt_outer, text="Format", font=self.FONT_S,
                 bg=self.PANEL, fg=self.MUTED).grid(row=0, column=1, sticky="w", padx=(6, 0))

        self._type_mode = tk.StringVar(value="Audio")
        type_cb = ttk.Combobox(fmt_outer, textvariable=self._type_mode,
                               values=["Audio", "Video"],
                               state="readonly", width=9)
        type_cb.grid(row=1, column=0, sticky="ew", pady=(2, 0))
        type_cb.bind("<<ComboboxSelected>>", self._on_type_change)

        self._out_fmt = tk.StringVar(value="ogg")
        self._out_fmt.trace_add("write", self._update_opus_hint)
        self._fmt_cb  = ttk.Combobox(fmt_outer, textvariable=self._out_fmt,
                                     values=AUDIO_FORMATS, state="readonly", width=9)
        self._fmt_cb.grid(row=1, column=1, sticky="ew", padx=(6, 0), pady=(2, 0))

        self._audio_frame = tk.Frame(frame, bg=self.PANEL)
        self._audio_frame.grid(row=3, column=0, sticky="ew")
        self._audio_frame.columnconfigure(0, weight=1)
        self._build_audio_settings(self._audio_frame)

        self._video_frame = tk.Frame(frame, bg=self.PANEL)
        self._video_frame.grid(row=4, column=0, sticky="ew")
        self._video_frame.columnconfigure(0, weight=1)
        self._build_video_settings(self._video_frame)
        self._video_frame.grid_remove()

        self._section(frame, "OUTPUT FOLDER", row=5)

        self.out_var = tk.StringVar(value="")
        self.out_var.trace_add("write", lambda *_: self._apply_out_border())

        self._out_entry = tk.Entry(frame, textvariable=self.out_var,
                                   font=self.FONT_S,
                                   bg=self.PANEL2, fg=self.FG,
                                   insertbackground=self.FG, bd=0,
                                   highlightthickness=1,
                                   highlightcolor=self.ACCENT,
                                   highlightbackground=self.MUTED)
        self._out_entry.grid(row=6, column=0, sticky="ew", pady=(4, 0))

        out_btn_row = tk.Frame(frame, bg=self.PANEL)
        out_btn_row.grid(row=7, column=0, sticky="ew", pady=(4, 0))
        self._btn(out_btn_row, "Browse…", self._pick_output_dir).pack(side="left")

        self._out_hint = tk.Label(frame, text="⚠ required before converting",
                                  font=("Consolas", 8), bg=self.PANEL, fg=self.WARN)
        self._out_hint.grid(row=8, column=0, sticky="w", pady=(2, 0))

    def _build_audio_settings(self, parent):
        self._section(parent, "AUDIO SETTINGS", row=0)
        grid = tk.Frame(parent, bg=self.PANEL)
        grid.grid(row=1, column=0, sticky="ew", pady=(4, 8))
        grid.columnconfigure(0, weight=1)
        grid.columnconfigure(1, weight=1)

        tk.Label(grid, text="Bitrate",     font=self.FONT_S, bg=self.PANEL,
                 fg=self.MUTED).grid(row=0, column=0, sticky="w")
        tk.Label(grid, text="Sample Rate", font=self.FONT_S, bg=self.PANEL,
                 fg=self.MUTED).grid(row=0, column=1, sticky="w", padx=(6, 0))

        self._audio_bitrate = tk.StringVar(value="192k")
        ttk.Combobox(grid, textvariable=self._audio_bitrate,
                     values=AUDIO_BITRATES, state="readonly",
                     width=8).grid(row=1, column=0, sticky="ew", pady=(2, 0))

        self._samplerate = tk.StringVar(value="44100")
        self._samplerate.trace_add("write", self._update_opus_hint)
        ttk.Combobox(grid, textvariable=self._samplerate,
                     values=AUDIO_SAMPLERATES, state="readonly",
                     width=8).grid(row=1, column=1, sticky="ew",
                                   padx=(6, 0), pady=(2, 0))

        tk.Label(grid, text="Channels", font=self.FONT_S, bg=self.PANEL,
                 fg=self.MUTED).grid(row=2, column=0, sticky="w", pady=(6, 0))

        self._channels = tk.StringVar(value="Stereo")
        ttk.Combobox(grid, textvariable=self._channels,
                     values=list(AUDIO_CHANNELS.keys()), state="readonly",
                     width=8).grid(row=3, column=0, sticky="ew", pady=(2, 0))

        # Note shown when opus is selected (sample rates auto-clamped)
        self._opus_hint = tk.Label(grid, text="",
                                   font=("Consolas", 7), bg=self.PANEL, fg=self.WARN)
        self._opus_hint.grid(row=4, column=0, columnspan=2, sticky="w", pady=(4, 0))
        # Wire format combobox to update the hint
        self._out_fmt_trace_id = self._out_fmt if hasattr(self, "_out_fmt") else None

    def _build_video_settings(self, parent):
        self._section(parent, "VIDEO SETTINGS", row=0)
        grid = tk.Frame(parent, bg=self.PANEL)
        grid.grid(row=1, column=0, sticky="ew", pady=(4, 0))
        grid.columnconfigure(0, weight=1)

        tk.Label(grid, text="Resolution", font=self.FONT_S, bg=self.PANEL,
                 fg=self.MUTED).grid(row=0, column=0, sticky="w")
        self._resolution = tk.StringVar(value="Original")
        ttk.Combobox(grid, textvariable=self._resolution,
                     values=VIDEO_RESOLUTIONS, state="readonly"
                     ).grid(row=1, column=0, sticky="ew", pady=(2, 6))

        tk.Label(grid, text="Quality", font=self.FONT_S, bg=self.PANEL,
                 fg=self.MUTED).grid(row=2, column=0, sticky="w")
        self._quality = tk.StringVar(value="Medium (CRF 23)")
        ttk.Combobox(grid, textvariable=self._quality,
                     values=VIDEO_QUALITIES, state="readonly"
                     ).grid(row=3, column=0, sticky="ew", pady=(2, 0))

        tk.Label(grid, text="Audio Track Bitrate", font=self.FONT_S, bg=self.PANEL,
                 fg=self.MUTED).grid(row=4, column=0, sticky="w", pady=(6, 0))
        self._vid_audio_bitrate = tk.StringVar(value="192k")
        ttk.Combobox(grid, textvariable=self._vid_audio_bitrate,
                     values=AUDIO_BITRATES, state="readonly"
                     ).grid(row=5, column=0, sticky="ew", pady=(2, 0))

    def _build_bottom_bar(self):
        bar = tk.Frame(self, bg=self.PANEL)
        bar.pack(fill="x", padx=14, pady=(0, 10))

        self._convert_btn = tk.Button(bar, text="▶  CONVERT ALL",
                                      command=self._start_conversion,
                                      font=("Consolas", 11, "bold"),
                                      bg=self.ACCENT, fg=self.BG,
                                      activebackground=self.FG,
                                      activeforeground=self.BG,
                                      bd=0, padx=24, pady=10,
                                      cursor="hand2", relief="flat")
        self._convert_btn.pack(side="right", pady=6)

        left = tk.Frame(bar, bg=self.PANEL)
        left.pack(side="left", fill="both", expand=True, pady=6, padx=(0, 10))

        self._style = ttk.Style(self)
        for name, color in (
            ("A.Horizontal.TProgressbar", self.ACCENT),
            ("D.Horizontal.TProgressbar", self.ACCENT),
            ("E.Horizontal.TProgressbar", self.ERR),
        ):
            try:
                self._style.configure(name, troughcolor=self.PANEL2,
                                      background=color, thickness=5)
            except tk.TclError:
                pass

        self.progress = ttk.Progressbar(left, orient="horizontal",
                                        mode="determinate",
                                        style="A.Horizontal.TProgressbar")
        self.progress.pack(fill="x", side="top")

        self.status_var = tk.StringVar(value="Ready — add files to begin.")
        tk.Label(left, textvariable=self.status_var,
                 font=("Consolas", 8), bg=self.PANEL, fg=self.MUTED,
                 anchor="w").pack(fill="x", side="top", pady=(2, 0))

    # ── Widget helpers ────────────────────────────────────────────────────────

    def _section(self, parent, label: str, row: int):
        f = tk.Frame(parent, bg=self.PANEL)
        f.grid(row=row, column=0, sticky="ew", pady=(8, 2))
        tk.Label(f, text=label, font=("Consolas", 8, "bold"),
                 bg=self.PANEL, fg=self.ACCENT).pack(side="left")
        tk.Frame(f, bg=self.MUTED, height=1).pack(
            side="left", fill="x", expand=True, padx=(6, 0), pady=1)

    def _btn(self, parent, text, cmd):
        return tk.Button(parent, text=text, command=cmd,
                         font=("Consolas", 9, "bold"),
                         bg=self.PANEL2, fg=self.FG,
                         activebackground=self.ACCENT, activeforeground=self.BG,
                         bd=0, padx=10, pady=4, cursor="hand2", relief="flat")

    def _center(self):
        self.update_idletasks()
        w, h = self.winfo_reqwidth(), self.winfo_reqheight()
        sw, sh = self.winfo_screenwidth(), self.winfo_screenheight()
        self.geometry(f"+{(sw - w) // 2}+{(sh - h) // 2}")

    def _set_status(self, msg: str):
        self.status_var.set(msg)

    def _apply_out_border(self):
        filled = bool(self.out_var.get().strip())
        self._out_entry.config(
            highlightbackground=self.MUTED if filled else self.WARN)
        self._out_hint.config(
            text="" if filled else "⚠ required before converting")

    def _auto_set_output(self, path: str):
        if self.out_var.get().strip():
            return
        if os.path.isfile(path):
            self.out_var.set(os.path.dirname(path))
        else:
            parent = os.path.dirname(path.rstrip(os.sep))
            self.out_var.set(parent if parent else path)

    def _set_ui_busy(self, busy: bool):
        state = "disabled" if busy else "normal"
        for btn in (self._btn_add, self._btn_folder,
                    self._btn_remove, self._btn_clear):
            btn.config(state=state)
        if not busy and not self._ffmpeg_ok:
            self._convert_btn.config(state="disabled")
        else:
            self._convert_btn.config(state=state)

    def _on_type_change(self, _=None):
        if self._type_mode.get() == "Audio":
            self._fmt_cb.config(values=AUDIO_FORMATS)
            self._out_fmt.set("ogg")
            self._audio_frame.grid()
            self._video_frame.grid_remove()
        else:
            self._fmt_cb.config(values=VIDEO_FORMATS)
            self._out_fmt.set("mp4")
            self._audio_frame.grid_remove()
            self._video_frame.grid()
        self._update_opus_hint()

    def _update_opus_hint(self, *_):
        """Show a warning hint when opus is selected and a clamped sample rate chosen."""
        if not hasattr(self, "_opus_hint"):
            return
        fmt = self._out_fmt.get()
        sr  = self._samplerate.get()
        if fmt == "opus" and sr not in OPUS_VALID_SAMPLERATES:
            self._opus_hint.config(
                text=f"⚠ {sr} Hz unsupported by opus — will use 48000 Hz")
        else:
            self._opus_hint.config(text="")

    def _get_settings(self) -> dict:
        return {
            "audio_bitrate":     self._audio_bitrate.get(),
            "samplerate":        self._samplerate.get(),
            "channels":          AUDIO_CHANNELS.get(self._channels.get(), "2"),
            "resolution":        self._resolution.get(),
            "quality":           self._quality.get(),
            "vid_audio_bitrate": self._vid_audio_bitrate.get(),
        }

    # ── Drag & drop ───────────────────────────────────────────────────────────

    def _on_drag_enter(self, event):
        self._drop_frame.config(highlightbackground=self.ACCENT, bg=self.DROP_HL)
        self._drop_lbl.config(fg=self.ACCENT)

    def _on_drag_leave(self, event):
        self._drop_frame.config(highlightbackground=self.MUTED, bg=self.DROP_HL)
        self._drop_lbl.config(fg=self.MUTED)

    def _on_drop(self, event):
        self._on_drag_leave(event)
        files_direct, folders, skipped = [], [], 0
        first_path = None
        for p in parse_drop_data(event.data):
            if os.path.isdir(p):
                folders.append(p)
                if first_path is None: first_path = p
            elif os.path.isfile(p):
                if os.path.splitext(p)[1].lower() in ALL_INPUT_EXTS:
                    files_direct.append(p)
                    if first_path is None: first_path = p
                else:
                    skipped += 1

        added = sum(self._ingest_file(p) for p in files_direct)
        self._update_type_indicator()   # once after all direct files added

        if first_path:
            self._auto_set_output(first_path)
        if added or skipped:
            parts = [f"{added} file(s) added"]
            if skipped: parts.append(f"{skipped} skipped")
            parts.append(f"{len(self._files_list)} queued.")
            self._set_status("  —  ".join(parts))
        for folder in folders:
            self._ingest_folder_async(folder)

    # ── File ingestion ────────────────────────────────────────────────────────

    def _ingest_file(self, path: str) -> int:
        """Add one file; O(1) dup check. Does NOT update type indicator — caller must."""
        if path not in self._files_set:
            ext  = os.path.splitext(path)[1].lower()
            kind = "🎵" if ext in AUDIO_EXTS else "🎬" if ext in VIDEO_EXTS else "?"
            self._files_set.add(path)
            self._files_list.append(path)
            self.listbox.insert("end", f"{kind}  {os.path.basename(path)}")
            return 1
        return 0

    def _update_type_indicator(self):
        """Call once after a batch of ingestions — not per file."""
        audio = sum(1 for p in self._files_list
                    if os.path.splitext(p)[1].lower() in AUDIO_EXTS)
        video = len(self._files_list) - audio
        parts = []
        if audio: parts.append(f"{audio} audio")
        if video: parts.append(f"{video} video")
        self._type_var.set("  ·  ".join(parts) if parts else "")

    def _ingest_folder_async(self, folder: str):
        self._scan_count += 1
        self._set_status(f"Scanning {os.path.basename(folder)}…"
                         f" ({self._scan_count} scan(s) active)")
        def worker():
            files = scan_folder(folder)
            self.after(0, lambda f=files, s=folder: self._ingest_batch(f, s))
        try:
            threading.Thread(target=worker, daemon=True).start()
        except Exception:
            self._scan_count = max(0, self._scan_count - 1)  # undo increment on failure
            self._set_status("Error: could not start folder scan thread.")

    def _ingest_batch(self, files: List[str], source: str = ""):
        self._scan_count = max(0, self._scan_count - 1)
        n_total = len(files)
        added   = sum(self._ingest_file(f) for f in files)
        dupes   = n_total - added
        self._update_type_indicator()   # once after entire batch

        if n_total == 0 and source:
            # Accumulate empty-folder warnings; show combined when all scans done
            if not hasattr(self, "_empty_folders"):
                self._empty_folders: List[str] = []
            self._empty_folders.append(source)
            if self._scan_count == 0:
                msg = "\n".join(self._empty_folders)
                self._empty_folders.clear()
                messagebox.showinfo("No files found",
                                    f"No supported files in:\n{msg}")
            return
        if source:
            self._auto_set_output(source)

        parts = [f"{added} file(s) added"]
        if dupes:   parts.append(f"{dupes} duplicate(s) skipped")
        if self._scan_count > 0:
            parts.append(f"{self._scan_count} scan(s) still running…")
        parts.append(f"{len(self._files_list)} queued.")
        self._set_status("  —  ".join(parts))

    def _add_files(self):
        ftypes = [
            ("All media", " ".join(f"*{e}" for e in sorted(ALL_INPUT_EXTS))),
            ("Audio",     " ".join(f"*{e}" for e in sorted(AUDIO_EXTS))),
            ("Video",     " ".join(f"*{e}" for e in sorted(VIDEO_EXTS))),
            ("All files", "*.*"),
        ]
        paths = filedialog.askopenfilenames(title="Select audio/video files",
                                            filetypes=ftypes)
        if not paths: return
        added = sum(self._ingest_file(p) for p in paths)
        self._update_type_indicator()
        self._auto_set_output(paths[0])
        self._set_status(f"{added} file(s) added  —  {len(self._files_list)} queued.")

    def _add_folder(self):
        folder = filedialog.askdirectory(title="Select folder")
        if not folder: return
        self._ingest_folder_async(folder)

    def _remove_selected(self):
        sel = list(self.listbox.curselection())
        if not sel:
            self._set_status("Nothing selected to remove.")
            return
        for i in reversed(sel):
            path = self._files_list.pop(i)
            self._files_set.discard(path)
            self.listbox.delete(i)
        self._update_type_indicator()
        self._set_status(f"{len(self._files_list)} file(s) queued.")

    def _clear_files(self):
        self._files_list.clear()
        self._files_set.clear()
        self._scan_count = 0   # reset counter; in-flight scans may still arrive
        if hasattr(self, "_empty_folders"):
            self._empty_folders.clear()
        self.listbox.delete(0, "end")
        self._type_var.set("")
        self.progress["value"] = 0
        self._set_status("Ready — add files to begin.")

    def _pick_output_dir(self):
        d = filedialog.askdirectory(title="Select output folder")
        if d: self.out_var.set(d)

    # ── Conversion ────────────────────────────────────────────────────────────

    def _start_conversion(self):
        if self._running: return
        if not self._ffmpeg_ok:
            # Recheck at convert time — user may have installed ffmpeg since launch
            self._ffmpeg_ok = bool(shutil.which("ffmpeg"))
            if not self._ffmpeg_ok:
                messagebox.showerror("ffmpeg not found",
                    "ffmpeg is still not in your PATH.\n"
                    "Install it and restart the app.")
                return
            self._convert_btn.config(state="normal")

        if not self._files_list:
            messagebox.showwarning("No files", "Please add files first.")
            return

        if self._scan_count > 0:
            if not messagebox.askyesno(
                "Scan in progress",
                f"{self._scan_count} folder scan(s) still running.\n\n"
                "Files not yet discovered will be excluded from this batch.\n\n"
                "Convert now anyway?"
            ):
                return

        raw     = self.out_var.get()
        out_dir = normalise_path(raw) if raw.strip() else ""
        # Guard: normalise_path("") → "." which is truthy and isdir — catch it
        if not out_dir or out_dir == ".":
            messagebox.showerror("No output folder",
                                 "Please choose an output folder.")
            return
        if not os.path.isdir(out_dir):
            messagebox.showerror("Bad folder",
                                 f"Output folder not found:\n{out_dir}")
            return
        if out_dir != raw:
            self.out_var.set(out_dir)

        out_fmt      = self._out_fmt.get()
        settings     = self._get_settings()
        dst_map      = build_dst_map(self._files_list, out_dir, out_fmt)
        out_dir_snap = out_dir   # snapshot — _finish uses this, not live out_var

        self._running = True
        self._set_ui_busy(True)
        self.progress.config(style="A.Horizontal.TProgressbar", value=0)

        total   = len(self._files_list)
        results = {"done": 0, "ok": 0}

        def on_done(success: bool, msg: str):
            with self._lock:
                results["done"] += 1
                if success: results["ok"] += 1
                d, ok = results["done"], results["ok"]
            pct = int(d / total * 100)
            self.after(0, lambda v=pct: self.progress.config(value=v))
            self.after(0, lambda m=f"[{d}/{total}]  {msg}": self._set_status(m))
            if d == total:
                self.after(0, lambda o=ok, t=total, od=out_dir_snap: self._finish(o, t, od))

        if self._executor:
            self._executor.shutdown(wait=False)
        try:
            self._executor = ThreadPoolExecutor(max_workers=MAX_WORKERS)
            for src, dst in dst_map.items():
                self._executor.submit(convert_file, src, dst, out_fmt, settings, on_done)
            self._executor.shutdown(wait=False)
        except Exception as exc:
            self._running = False
            self._set_ui_busy(False)
            messagebox.showerror("Error", str(exc))

    def _finish(self, ok: int, total: int, out_dir: str):
        self._running = False
        self._set_ui_busy(False)
        if ok == total:
            self.progress.config(style="D.Horizontal.TProgressbar")
            self._set_status(f"✔  Done! {ok}/{total} converted  →  {out_dir}")
        else:
            self.progress.config(style="E.Horizontal.TProgressbar")
            self._set_status(f"⚠  Finished with errors: {ok}/{total} succeeded.")


# ── Entry point ───────────────────────────────────────────────────────────────

if __name__ == "__main__":
    app = ConverterApp()
    app.mainloop()
