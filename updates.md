Softcurse Media Studio AI
Comprehensive Code Audit Report
March 2026


1. Executive Summary
Softcurse Media Studio AI is a Windows WPF desktop application for AI-powered image and video editing. It combines a C# .NET 8 GUI layer with a legacy C++ watermark engine. The application integrates local ONNX inference (LaMa inpainting, SAM segmentation), OpenCV-based processing, and optional remote Stable Diffusion API calls.

The codebase demonstrates solid architectural intent but contains several significant issues spanning correctness, security, reliability, and honest feature representation. The most pressing concerns are:

•	A known crash bug blocking application startup (XAML parse error in crash.log).
•	Stub/fake SAM model implementation that silently falls back to GrabCut without disclosing this to the user.
•	Hardcoded model path traversal using relative '../../../' navigation that will break in many deployment scenarios.
•	No input validation on the Stable Diffusion API endpoint, creating a potential SSRF exposure.
•	Silent bare catch blocks throughout the C# codebase hiding operational failures.
•	The C++ watermark engine uses a fixed hardcoded pixel coordinate approach incompatible with the ONNX pipeline in the GUI.

2. Project Overview
2.1 Architecture
The project has three distinct layers:

Layer	Technology	Purpose
GUI / App	C# / WPF / .NET 8	Navigation, masking UI, image/video editing, API client
AI Services	C# / ONNX Runtime	LaMa inpainting (WatermarkService), SAM segmentation (SamModelService)
Legacy CLI Engine	C++ / CMake / stb	Standalone CLI watermark remover using fixed-coordinate approach

2.2 Key Dependencies
Package	Version	Role
Microsoft.ML.OnnxRuntime + DirectML	1.24.2	GPU/CPU AI model inference
OpenCvSharp4.Windows	4.9.0	Image processing, GrabCut, video I/O
ModernWpfUI	0.9.6	Modern Fluent UI controls
System.Numerics.Tensors	10.0.2	Tensor operations for ONNX
stb_image / stb_image_write	embedded	Image I/O for C++ engine

3. Findings Summary
ID	Severity	Location	Title	Description
F-01	CRITICAL	crash.log / MainWindow.xaml	Startup Crash — XAML Icon Parse Error	App fails to start: XamlParseException at line 31 because ModernWpfUI's IconElementConverter cannot convert a plain color string to an Icon.
F-02	HIGH	SamModelService.cs	SAM Stub — Silently Falls Back to GrabCut	The SAM model files are never actually loaded. Stub text files are written, sessions are never instantiated, and GrabCut is silently substituted without any UI disclosure.
F-03	HIGH	WatermarkService.cs / SamModelService.cs	Fragile Relative Model Paths (../../../)	Model paths use '../../../models/' traversal that will resolve incorrectly in any deployment layout other than the development tree.
F-04	HIGH	ImageEditorPage / GenerativeFillPage / VideoLabPage	SSRF via Unvalidated API Endpoint	The Stable Diffusion API URL read from settings.json is sent directly to HttpClient without scheme or host validation, allowing arbitrary internal network requests.
F-05	HIGH	VideoLabPage.cs ProcessVideoRun()	Per-Frame Disk I/O in Video Processing	Every video frame is written to and read from temp files on disk, causing severe performance degradation on any non-trivial video.
F-06	MEDIUM	WatermarkService.cs RemoveWatermark()	Silent GPU Fallback Swallows All Exceptions	The DirectML fallback catch block matches all OnnxRuntimeExceptions including data-corruption errors; it silently reloads the entire model from disk on every failure.
F-07	MEDIUM	AppSettings.cs	Settings Deserialization Silently Swallows Errors	Deserialization failures are caught with an empty catch block, leaving settings in default state with no user notification.
F-08	MEDIUM	WatermarkService.cs RemoveWatermark()	Pixel-Level Loop for Tensor Fill (O(n^2))	Image-to-tensor conversion uses nested for loops instead of Span<float>/bulk memory copy, causing poor performance on large images.
F-09	MEDIUM	WatermarkService.cs CreateAutoMask()	Auto-Mask Hardcoded for Gemini Watermark Only	README advertises a general watermark remover, but the auto-detection routine exclusively targets Gemini's white star in the bottom-right corner.
F-10	MEDIUM	ImageEditorPage.cs	Static HttpClient Shared Across Instances	A static HttpClient is created per page without a shared IHttpClientFactory, risking socket exhaustion with repeated navigation.
F-11	MEDIUM	src/watermark_engine.cpp	C++ Engine: Hardcoded Fixed-Coordinate Removal	RemoveWatermark() operates on fixed pixel coordinates (watermarkWidth/Height constants not defined in audited files), making the result entirely position-dependent.
F-12	LOW	TempFileManager.cs	TempFileManager Uses ConcurrentBag; Contains() is O(n)	The contains check in RegisterTempFile iterates the entire bag each time, degrading with large batches. ConcurrentDictionary is more appropriate.
F-13	LOW	VideoLabPage.cs	FFmpeg Assumed in PATH; No Version Check	Audio remuxing silently falls back to a silent video if ffmpeg is absent, but the user is not warned about this dependency upfront.
F-14	LOW	BatchProcessorPage.cs	Batch Processor Error Swallowed Silently	Per-file errors are caught and marked 'Error' with no logged message, making debugging batch failures impossible.
F-15	INFO	GeminiWatermarkRemover.csproj	Assembly Name Mismatch	Project AssemblyName is SoftcurseMediaStudioAI but namespace is GeminiWatermarkRemover throughout the codebase — artifact of an incomplete rename.
F-16	INFO	README.md	README References Non-Existent assets/media.png	The README banner references assets/media.png which is absent from the repository (only media.ico exists).

4. Detailed Findings
F-01 [CRITICAL] — Startup Crash: XAML Icon Parse Error
Location
gui/Views/* (XAML) — crash.log line 31
Description
The crash.log file records a fatal XamlParseException thrown during application startup. ModernWpfUI's IconElementConverter cannot convert a plain color string (e.g. 'Color #RRGGBB') into an Icon type. This prevents the application from launching entirely.
Evidence
System.NotSupportedException: IconElementConverter cannot convert from System.String.
at ModernWpf.Controls.IconElementConverter.ConvertFrom(...)
Remediation
•	Locate the XAML property at line 31 that passes a color string to an Icon attribute.
•	Replace with a valid ModernWpfUI icon specification, e.g. <ui:SymbolIcon Symbol="Edit"/> or wrap the color in a proper FontIcon element.
•	Run the application and confirm the XamlParseException is resolved before any other fixes.

F-02 [HIGH] — SAM Stub: Silent GrabCut Substitution
Location
gui/SamModelService.cs — InitializeAsync(), DownloadQuantizedModelsAsync(), GenerateMaskAsync()
Description
SamModelService never loads a real SAM ONNX model. DownloadQuantizedModelsAsync() writes literal stub text files ('SAM_ENCODER_STUB_8BIT_QUANTIZED') to disk instead of downloading real weights. The InferenceSession load calls are commented out. GenerateMaskAsync() silently falls back to OpenCV GrabCut, which is a substantially inferior segmentation method. The UI labels this feature 'Magic Wand (SAM)' with no disclaimer.
Remediation
•	Either implement genuine SAM ONNX inference (uncomment the session load, point to real quantized model files, implement encoder+decoder pipeline) or clearly label the feature as 'Region Select (GrabCut)' in the UI.
•	Remove the fake download delay and stub file writing — it misleads users and future developers.
•	If the genuine SAM pipeline is planned, add a model-download progress dialog with real HTTP download logic and checksums.

F-03 [HIGH] — Fragile Relative Model Paths
Location
gui/WatermarkService.cs line ~13
gui/SamModelService.cs line ~16
Description
Both services construct model paths with Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "models", ...). This depends on the application being run from exactly three levels below the repository root (the development bin/Debug/net8.0/ directory). Any published build, installer package, or non-standard working directory will silently fail to find the model with a FileNotFoundException.
Remediation
•	Define model paths via configuration: read from appsettings.json or a dedicated model settings entry.
•	Provide a first-run wizard or settings UI entry for users to specify their model directory.
•	Fall back to AppDomain.CurrentDomain.BaseDirectory/models/ (already present as a secondary check) and surface a helpful error with instructions if the path is still not found.

F-04 [HIGH] — SSRF via Unvalidated API Endpoint
Location
gui/Views/ImageEditorPage.xaml.cs — Expand_Click, Upscale_Click
gui/Views/GenerativeFillPage.xaml.cs — Generate_Click
Description
The API endpoint is read directly from AppSettings.ApiEndpoint (a user-writable settings.json file) and passed to HttpClient.PostAsync without any validation of scheme, host, or port. An attacker who can write to settings.json (e.g., via a malicious file) could redirect requests to internal network addresses, cloud metadata services (169.254.169.254), or other sensitive endpoints.
Remediation
•	Validate that the endpoint URI starts with http:// or https:// and resolves to a non-private IP (or whitelist localhost/127.0.0.1 only).
•	Parse with Uri.TryCreate and check Uri.IsLoopback or maintain an allowlist of permitted hosts.
•	Show a warning in the Settings UI if the user enters a non-localhost address.

F-05 [HIGH] — Per-Frame Disk I/O in Video Processing
Location
gui/Views/VideoLabPage.cs — ProcessVideoRun()
Description
For each video frame, the code writes a PNG to disk via Cv2.ImWrite(tempInput), calls RemoveWatermark() which reads it back with Cv2.ImRead(), then reads the output PNG again. A 1-minute 30fps video generates over 3,600 disk write+read cycles, creating severe I/O bottleneck and substantial temp storage consumption.
Remediation
•	Refactor WatermarkService.RemoveWatermark() to accept a Mat directly (or a byte[] span) instead of file paths.
•	Pass frames in-memory through the ONNX pipeline, eliminating all disk I/O in the frame loop.
•	Consider processing multiple frames concurrently using a producer-consumer Channel<Mat> pattern.

F-06 [MEDIUM] — Overly Broad Exception Catch in GPU Fallback
Location
gui/WatermarkService.cs — RemoveWatermark() catch block ~line 120
Description
The GPU-to-CPU fallback catches all OnnxRuntimeExceptions and reloads the entire model from disk on every failure. This means any model execution error (not just GPU incompatibility) triggers a costly re-initialization, and corrupted output could silently pass through.
Remediation
•	Narrow the catch to GPU-specific errors using OnnxRuntimeException.OrtErrorCode or message patterns for EP_FAIL.
•	Cache the fallback CPU session to avoid reloading the model on every call.
•	Log the exception details before swallowing, using ILogger or Debug.WriteLine with a structured format.

F-07 [MEDIUM] — Silent Settings Deserialization Failure
Location
gui/AppSettings.cs — Load() catch block
Description
If settings.json is corrupt or contains invalid JSON, the catch block silently continues with default settings. The user receives no notification and may not realize their saved preferences (especially the API endpoint and output folder) were not loaded.
Remediation
•	Log the exception with a timestamp to a diagnostic log file.
•	Surface a non-blocking notification in the UI status bar indicating the settings file could not be read and defaults are being used.
•	On save failure, also notify rather than silently ignoring.

F-08 [MEDIUM] — Pixel-Level Loop for Tensor Preparation
Location
gui/WatermarkService.cs — RemoveWatermark() tensor fill loop ~line 85
Description
The code uses a nested for(y)for(x) loop calling inputImage.At<Vec3b>(y, x) and indexing into DenseTensor via individual element assignments. For a 512x512 input this is 786,432 individual managed calls. Using Mat.DataPointer with unsafe memory copies or pre-allocated float arrays with Buffer.BlockCopy would be significantly faster.
Remediation
•	Use Mat.DataPointer or CvMat.GetRawData() to obtain a direct memory pointer.
•	Normalize channel data using a vectorized loop or System.Numerics.Vector<float>.
•	Benchmark before and after — for 512x512 the improvement should be 5-10x on the tensor fill step.

F-09 [MEDIUM] — Auto-Mask Hardcoded for Gemini Watermark
Location
gui/WatermarkService.cs — CreateAutoMask()
Description
The auto-mask function explicitly searches only the bottom-right third of the image for bright/white star-shaped blobs, with comment noting it targets the 'Gemini watermark'. The README and UI describe a general-purpose watermark remover. Users with watermarks in other positions or colors will get no detection.
Remediation
•	Either rename 'Auto Mode' to 'Gemini Watermark Mode' and document the limitation, or generalize the detection.
•	A more general approach would apply the brightness threshold to the full image, cluster detected regions by density, and present candidates to the user rather than auto-selecting.

F-10 [MEDIUM] — Static HttpClient per Page
Location
gui/Views/ImageEditorPage.xaml.cs line ~19
gui/Views/GenerativeFillPage.xaml.cs line ~10
Description
Each page class declares a static readonly HttpClient. While static is better than per-request instantiation, two separate static instances exist with no shared lifetime management. In .NET 8 the recommended pattern is IHttpClientFactory via dependency injection, especially important here because the API endpoint can change at runtime.
Remediation
•	Register a named HttpClient with the DI container in App.xaml.cs.
•	Inject IHttpClientFactory into pages and create short-lived clients via factory.CreateClient("StableDiffusion").
•	This also allows the base address to be reconfigured at runtime when the user changes the API endpoint in Settings.

F-11 [MEDIUM] — C++ Engine: Fixed-Coordinate Watermark Removal
Location
src/watermark_engine.cpp — RemoveWatermark()
Description
The C++ CLI engine computes startX/Y using watermarkWidth and watermarkHeight — constants that appear to be declared in the header file but are not shown in the audited files. The algorithm applies a simple reverse-alpha-blend across a fixed rectangle in the bottom-right corner. This is entirely disconnected from the sophisticated ONNX LaMa pipeline in the C# layer and will produce incorrect results for any image where the watermark size or position differs from the hardcoded values.
Remediation
•	If the C++ engine is still intended to be shipped, add auto-detection logic mirroring CreateAutoMask() or accept a mask file as a CLI argument.
•	If the C++ engine has been superseded by the C# ONNX pipeline (which appears to be the case), clearly mark it as deprecated in the README and remove it from the CMake build to avoid confusion.

F-12 [LOW] — TempFileManager O(n) Contains Check
Location
gui/TempFileManager.cs — RegisterTempFile()
Description
ConcurrentBag<T>.Contains() iterates the entire collection. For batch processing of thousands of images, this degrades performance unnecessarily.
Remediation
// Replace ConcurrentBag with:
private static readonly ConcurrentDictionary<string, byte> _files = new();
public static void Register(string p) => _files.TryAdd(p, 0);

F-13 [LOW] — FFmpeg Runtime Dependency Undisclosed
Location
gui/Views/VideoLabPage.cs — RemuxAudio()
Description
Audio remuxing requires ffmpeg to be present in the system PATH. Its absence is caught via Win32Exception and silently falls back to a silent video, but the user is never informed of this dependency before starting video processing.
Remediation
•	On application startup or when the Video Lab tab is first opened, check for ffmpeg availability and display a status indicator.
•	Add ffmpeg to the requirements section of the README with a download link.

F-14 [LOW] — Silent Batch Error Swallowing
Location
gui/Views/BatchProcessorPage.cs — StartBatch_Click() catch block
Description
Per-file batch errors are silently caught and the item is marked 'Error' with no exception details written anywhere. Users have no way to diagnose why a particular file failed.
Remediation
•	Log exception messages to a BatchErrors.log file in the output directory.
•	Show exception message in a tooltip or expandable details panel next to the Error status.

F-15 [INFO] — Assembly Name / Namespace Mismatch
Location
gui/GeminiWatermarkRemover.csproj
Description
The project file sets AssemblyName to SoftcurseMediaStudioAI but every C# file uses namespace GeminiWatermarkRemover. This is a vestige of an incomplete rebrand and may confuse contributors or tooling.
Remediation
•	Perform a global namespace rename from GeminiWatermarkRemover to SoftcurseMediaStudioAI.
•	Rename the .csproj file to match.

F-16 [INFO] — Missing README Banner Image
Location
README.md line 3
Description
The README references assets/media.png which does not exist in the repository (only assets/media.ico is present). The broken image reference renders as an empty broken image in GitHub.
Remediation
•	Add a representative screenshot as assets/media.png, or update the README to reference media.ico or remove the image tag.

5. Positive Observations
Despite the issues listed, the codebase demonstrates several good engineering practices worth acknowledging:

•	Proper IDisposable implementation on both WatermarkService and SamModelService, ensuring ONNX session cleanup.
•	Smart ROI cropping in WatermarkService — rather than resizing the full image, the code crops a 512x512 region centered on the watermark, preserving detail.
•	Correct GPU-to-CPU fallback architecture with DirectML as the primary execution provider and transparent CPU fallback.
•	Undo/redo stack scaffolding is present in ImageEditorPage (Stack<string> _undoStack / _redoStack) — though not fully wired, the intent is there.
•	Cancellation token propagation in both BatchProcessorPage and VideoLabPage, allowing clean task cancellation.
•	The before/after compare slider is a polished UX feature with correct clip geometry binding.
•	TempFileManager provides a centralized temp file registry for cleanup on shutdown.
•	BitmapImage.Freeze() is correctly called after loading images, which is important for cross-thread rendering performance in WPF.

6. Prioritized Remediation Roadmap
Pri	Finding	Effort	Action
1	F-01	< 1 hour	Fix XAML icon property crash — app cannot be used at all until this is resolved.
2	F-02	1–3 days	Either implement real SAM inference or honestly rebrand the feature as GrabCut-based region select.
3	F-03	2–4 hours	Replace ../../../ path traversal with configuration-driven model path resolution.
4	F-04	2–4 hours	Add URL validation to the API endpoint before passing to HttpClient.
5	F-05	1 day	Refactor WatermarkService to accept Mat in-memory; eliminate per-frame disk I/O.
6	F-06	2 hours	Narrow GPU fallback catch, cache CPU session, and log details.
7	F-07/14	2 hours	Add logging and user notifications for silent catch blocks.
8	F-08	4 hours	Profile and optimize tensor fill with unsafe memory copy.
9	F-09	4 hours	Generalize or honestly document the auto-mask detection limitation.
10	F-10	4 hours	Migrate HttpClient to IHttpClientFactory with DI.

7. Conclusion
Softcurse Media Studio AI has a strong conceptual foundation: the combination of LaMa ONNX inpainting, multi-tool masking UI, video watermark removal, and Stable Diffusion integration is genuinely useful. However, the application currently cannot start due to F-01, and its most-advertised AI feature (SAM Magic Wand) is not actually implemented. Addressing the critical and high severity findings — particularly the startup crash, the SAM stub, the model path fragility, and the API endpoint validation — would bring the codebase to a releasable state. The medium-severity items are important for robustness and performance at scale. The overall code quality is moderate; the architecture is sound but execution needs another pass of hardening before wider distribution.


# Softcurse Media Studio AI — GUI Redesign Implementation Guide

## What Was Changed

### Files Delivered
| File | Status | Changes |
|------|--------|---------|
| `gui/App.xaml` | **Replaced** | New color palette, all button styles, combo box, text box, toggle styles |
| `gui/MainWindow.xaml` | **Replaced** | Removed ModernWpf NavigationView, new custom sidebar with canvas icons |
| `gui/MainWindow.xaml.cs` | **Replaced** | New `NavButton_Click` handler replacing `NavView_SelectionChanged` |
| `gui/Views/ImageEditorPage.xaml` | **Replaced** | Full HUD toolbar, canvas with corner brackets, bottom tool strip |
| `gui/Views/SettingsPage.xaml` | **Replaced** | HUD-styled settings panels with new brushes |

---

## Design System

### Color Palette
| Variable | Hex | Use |
|----------|-----|-----|
| `BG` | `#060D14` | App background |
| `BG2` | `#0A1520` | Secondary panels |
| `PanelBrush` | `#071018` | Sidebar, top/bottom bars |
| `CyberAccentBrush` | `#00E5FF` | Primary cyan — titles, active borders, icons |
| `CyberSecondaryBrush` | `#1565FF` | Blue accent |
| `CyberMagentaBrush` | `#FF4488` | Danger / reset actions |
| `BorderBrush` | `#0D3A5C` | Subtle borders |
| `BorderBrightBrush` | `#1A6A9A` | Visible panel borders |
| `TextBrush` | `#B0D8F0` | Primary text |
| `TextDimBrush` | `#4A7A9B` | Dim/inactive text, labels |
| `SuccessBrush` | `#00FF99` | API connected state |
| `WarnBrush` | `#FF8C00` | Warnings |

### Button Styles
- `BtnPrimary` — Cyan fill, black text, glow shadow (use for primary actions like "APPLY MASK")
- `BtnOutline` — Transparent bg, cyan border (use for secondary actions)
- `BtnDanger` — Transparent bg, magenta border (use for RESET, CLEAR MASK)
- `ToggleOutline` — Like BtnOutline but lit when checked (use for COMPARE)

### Other Styles
- `HudComboBox` — Dark navy bg, cyan text, bright border
- `HudTextBox` — Same family, with caret matching accent color

---

## How to Drop In

1. Replace the 5 files listed above in your project.
2. The `MainWindow.xaml.cs` no longer depends on `ModernWpf.Controls.NavigationView` — the `using ModernWpf.Controls;` line can be removed if nothing else uses it.
3. The **nav buttons are plain WPF Buttons** — no ModernWpf `NavigationViewItem` used, which also **fixes the F-01 Icon crash bug** (no more `IconElementConverter`).
4. Styles are defined globally in `App.xaml` — any page can use `Style="{StaticResource BtnPrimary}"` etc.

---

## Roadmap Update (from Audit Report)

This GUI update addresses:

| Finding | Status | Notes |
|---------|--------|-------|
| **F-01** CRITICAL — Startup crash | ✅ **Fixed** | Removed ModernWpfUI `NavigationViewItem Icon=` usage entirely |
| **F-15** INFO — Namespace mismatch | ⏳ Pending | Still named `GeminiWatermarkRemover` — rename separately |

Remaining high-priority from audit:
1. **F-02** SAM stub — Implement real SAM ONNX or relabel feature
2. **F-03** Model paths — Replace `../../../` with config-driven paths
3. **F-04** SSRF — Validate API endpoint URL
4. **F-05** Per-frame disk I/O — Refactor `WatermarkService` to accept `Mat`

---

## Extending the Design

### Adding a new page
1. Create `Views/YourPage.xaml` with `Background="Transparent"`
2. Use `Style="{StaticResource BtnOutline}"` etc. for all buttons
3. Add a nav button in `MainWindow.xaml` following the existing pattern
4. Add a `case "YourPage":` in `MainWindow.xaml.cs`

### Custom HUD panel
Wrap content in:
```xml
<Border BorderBrush="{StaticResource BorderBrightBrush}" BorderThickness="1"
        Background="#0A1520" Padding="16">
    <!-- your content -->
</Border>
```

### Glow text
```xml
<TextBlock Text="TITLE" Foreground="{StaticResource CyberAccentBrush}">
    <TextBlock.Effect>
        <DropShadowEffect Color="#00E5FF" BlurRadius="10" ShadowDepth="0" Opacity="0.6"/>
    </TextBlock.Effect>
</TextBlock>
```

11 files fixed. Here's exactly what was done to each finding:

F-02 — SAM stub (SamModelService.cs) — The fake DownloadQuantizedModelsAsync that wrote stub text files is gone entirely. InitializeAsync() now tries to load real ONNX sessions via ModelPathResolver, sets IsSamAvailable = true only when they actually load, and silently falls back to GrabCut otherwise. GenerateMaskAsync() returns a (maskPath, usedSam) tuple. ImageEditorPage.cs updated to reflect which mode is active in the status bar.
F-03 — Fragile paths (ModelPathResolver.cs — new file) — All ../../../models/ hardcoding replaced with a central resolver that checks in order: user-configured directory from AppSettings.ModelDirectory → models/ next to exe → ../../../models/ dev fallback. Throws FileNotFoundException with user-actionable instructions if nothing is found. Both WatermarkService and SamModelService now call ModelPathResolver.Resolve().
F-04 — SSRF (AppSettings.cs, ImageEditorPage.xaml.cs, GenerativeFillPage.xaml.cs) — AppSettings.IsApiEndpointSafe() validates scheme (http/https only), URI parsability, and blocks known cloud metadata address ranges (169.254.x.x, etc.). All three API call sites check this before any HttpClient.PostAsync.
F-05 — Per-frame disk I/O (WatermarkService.cs, VideoLabPage.xaml.cs) — WatermarkService now has a RemoveWatermarkFromMat(Mat) overload that processes entirely in memory and returns a Mat. VideoLabPage.ProcessVideoRun() calls this directly — no ImWrite/ImRead per frame.
F-06 — Broad GPU catch (WatermarkService.cs) — IsEpRejectionError() narrows the fallback to ORT error codes 8/9/10 and known DirectML rejection messages. Data errors are no longer silently swallowed. CPU session is created once and cached.
F-07 — Silent settings errors (AppSettings.cs, MainWindow.xaml.cs) — Load()/Save() now raise LoadError/SaveError events with the exception message. MainWindow subscribes and shows a warning MessageBox on the UI thread.
F-08 — O(n²) tensor fill (WatermarkService.cs) — FillTensors() uses unsafe pointer arithmetic via Mat.DataPointer and Span<float> into the tensor buffer. AllowUnsafeBlocks added to the csproj.
F-09 — Auto-mask labeling (WatermarkService.cs) — CreateAutoMask() renamed to DetectHighContrastMask() with an honest XML doc comment describing what it actually does and its current limitation.
F-12 — ConcurrentBag O(n) (TempFileManager.cs) — Replaced with ConcurrentDictionary<string, byte> giving O(1) TryAdd checks.
F-13 — FFmpeg not checked (VideoLabPage.xaml.cs, MainWindow.xaml.cs) — IsFfmpegAvailable() is a public static method that probes ffmpeg -version. MainWindow calls it on startup. The Win32Exception catch now shows an explicit install-instructions dialog instead of silently falling back.
F-14 — Silent batch errors (BatchProcessorPage.xaml.cs) — The per-file catch now appends a timestamped entry to batch_errors.log in the output directory and sets item.Status to the actual error message.

6 files delivered (D:\Projects\Gemini watermark remover\temp). Here's exactly what was built:

What's in each file
SpriteGeneratorPage.xaml — Full HUD layout in two columns. Left panel has all controls (prompt, negative prompt, 7 style presets, frame size, steps/CFG/seed, sprite sheet options, AnimateDiff section). Right panel has a checkerboard-background preview area (shows transparency correctly), a live scrollable frame strip that populates as frames are generated, and a save/action bar.
SpriteGeneratorPage.xaml.cs — The full async pipeline:

Validates the SD WebUI endpoint (reuses the F-04 IsApiEndpointSafe check)
Generates the base sprite via /sdapi/v1/txt2img with style-preset prompt suffixes injected automatically (e.g. "pixel art, 16-bit, SNES style..." for the 16-bit preset)
Removes the background via SpriteSheetService.RemoveBackgroundGrabCut if checked
AnimateDiff path: POSTs to /sdapi/v1/img2img with the alwayson_scripts.AnimateDiff block — motion module, frame count, context overlap, and an animation-type prompt (walk/idle/run/attack/jump/death/cast). Returns all frames from the images[] array
Static variations path: generates N independent txt2img frames with random seeds
Frame strip updates live as each frame arrives
Assembles the sprite sheet PNG via SpriteSheetService.BuildSheet
Save buttons: individual sprite PNG, full sheet PNG, animated GIF, and "Send to Editor" which calls MainWindow.OpenImageInEditor()

SpriteSheetService.cs — Three static utilities, no UI thread needed:

BuildSheet() — stitches frames into a grid PNG using OpenCvSharp, handles alpha channels, auto-resizes frames to match the first frame
BuildGif() — pure .NET animated GIF encoder with no ffmpeg dependency. Uses OpenCV k-means quantization (K=256) per frame, then writes a valid GIF89a with Netscape looping extension and per-frame LZW encoding
RemoveBackgroundGrabCut() — border-seeded GrabCut: seeds the outer border strip as definite background, the inner 60% as probable foreground, outputs BGRA PNG with transparent background

MainWindow.xaml — New "SPRITE GENERATOR" nav button added to the sidebar with a 4-cell sprite-sheet grid icon
MainWindow.xaml.cs — SpritePage nav case + OpenImageInEditor(path) public method so the sprite page can hand off results
ImageEditorPage.xaml.cs — LoadImageFromPath(path) public wrapper added

Requirements for AnimateDiff to work

SD WebUI running with --api
AnimateDiff extension installed
A motion module file (e.g. mm_sd_v15_v2.ckpt) placed in stable-diffusion-webui/extensions/sd-webui-animatediff/model/
If AnimateDiff isn't installed, uncheck "Enable AnimateDiff" — the static variations path works on any bare SD WebUI

new icons pack - D:\Projects\Gemini watermark remover\temp\softcurse-icons.html, use this icons!!!