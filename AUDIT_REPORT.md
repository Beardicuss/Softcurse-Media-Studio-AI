# Softcurse Media Lab AI — Audit Report

Date: 2026-08-30

## Executive summary

The application now restores, builds, and starts successfully on .NET 8. The abandoned sprite generator and its bundled Stable Diffusion WebUI tree were fully removed. The highest-impact reliability defects found during the audit were settings being saved into the installation directory, unsafe/manual FFmpeg argument quoting, an execution-provider option that was never honored, concurrent ONNX inference access, and an output-folder preference that was never used. These issues are fixed.

No known-vulnerable NuGet packages were reported by `dotnet list package --vulnerable --include-transitive`. The security review used general .NET desktop guidance because the installed security skill has no C#-specific reference pack.

## Fixed findings

### High

**F-01 — FFmpeg argument injection and broken path quoting (fixed).** User-selected filenames were assembled into a single command-line string. Filenames containing quotes could alter argument boundaries. Both conversion and audio-remux workflows now use `ProcessStartInfo.ArgumentList`, which passes each value as an isolated argument (`gui/Views/VideoLabPage.xaml.cs:322`, `gui/Views/VideoLabPage.xaml.cs:703`).

**F-02 — Settings failed after normal per-user installation (fixed).** The application wrote `settings.json` beside the executable, which is commonly read-only in installed locations. Settings now live under `%LOCALAPPDATA%\SoftcurseMediaLabAI`, legacy settings are migrated on read, and writes use a temporary file plus atomic replacement (`gui/AppSettings.cs:18`, `gui/AppSettings.cs:91`).

### Medium

**F-03 — Stable Diffusion/sprite removal was incomplete (fixed).** The full WebUI source tree, sprite page, sprite services, navigation, process launcher, and documentation references remained. All sprite-specific code and the local WebUI tree are removed. Generative Fill and Expand remain available through a separately managed compatible API.

**F-04 — ONNX session could be used concurrently (fixed).** Image and video workflows share one `WatermarkService`; concurrent calls could race on the same inference session, particularly during GPU-to-CPU fallback. Inference is now serialized with a service-level lock (`gui/WatermarkService.cs:18`, `gui/WatermarkService.cs:98`).

**F-05 — Execution-provider setting was misleading and ignored (fixed).** The UI advertised CUDA without shipping the CUDA runtime, while model initialization always preferred DirectML. The UI now exposes CPU and DirectML only, migrates the old DirectML index, and honors the selected value (`gui/AppSettings.cs:73`, `gui/WatermarkService.cs:30`). A restart remains required after changing providers because sessions are initialized at startup.

**F-06 — API endpoint accepted embedded credentials (fixed).** HTTP(S) endpoints containing `user:password@host` are rejected to prevent accidental credential persistence and disclosure (`gui/AppSettings.cs:133`). Existing metadata/link-local address blocking remains active.

**F-07 — Default output folder had no effect (fixed).** The setting was saved but never consumed. Primary editor, resize, crop, and video-retouch save dialogs now start in the configured directory.

### Low

**F-08 — Duplicate and stale ONNX dependencies (fixed).** The project directly referenced the CPU runtime, DirectML runtime, and managed package simultaneously. It now references only DirectML 1.24.4, which includes the managed API and CPU fallback. `System.Numerics.Tensors` was updated to 10.0.11 (`gui/GeminiWatermarkRemover.csproj:20`).

**F-09 — Invalid image inputs failed deep inside OpenCV (fixed).** Filter operations now reject unreadable/empty images with a useful error (`gui/FilterService.cs:76`). Image output failures from the watermark engine are also checked.

**F-10 — Duplicate retouch actions could run simultaneously (fixed).** The Retouch button is disabled while processing and restored afterward (`gui/Views/ImageEditorPage.xaml.cs:397`).

## Remaining limitations and weak spots

**R-01 — Generative Fill and Expand still require an external API (medium).** They cannot work when no compatible server is configured. This is now documented as an optional external dependency rather than something bundled with the app. Images sent to a remote endpoint leave the machine; users should only configure a server they trust.

**R-02 — “Smart selection” uses GrabCut, not SAM (medium).** The repository contains SAM ONNX files, but `SamModelService` explicitly leaves the ONNX pipeline unimplemented and falls back to GrabCut. The UI still uses magic-wand styling, so result quality can differ from true SAM segmentation.

**R-03 — Automated coverage is absent (medium).** There is no unit/integration test project. This audit verified restore, Release compilation, dependency vulnerability status, diff hygiene, and a six-second application startup smoke test, but pixel-level correctness and every interactive workflow still need repeatable tests.

**R-04 — FFmpeg is an external requirement (low).** Forge Lab conversion/remux features require `ffmpeg` on `PATH`. The app detects its absence and preserves a silent-video fallback where possible.

**R-05 — C++ prototype is separate from the shipping WPF implementation (low).** The root CMake target is a simple legacy/prototype remover and is not called by the WPF application. Maintaining two engines increases confusion; consider archiving it if it is not distributed.

## Verification performed

- `dotnet restore gui/GeminiWatermarkRemover.csproj --force`
- `dotnet build gui/GeminiWatermarkRemover.csproj -c Release --no-restore` — succeeded with 0 warnings and 0 errors
- `dotnet list gui/GeminiWatermarkRemover.csproj package --vulnerable --include-transitive` — no known vulnerable packages
- Release executable startup smoke test — process remained healthy for six seconds, then was stopped by the audit
- `git diff --check` — no patch whitespace errors
- Repository search — no sprite generator or local SD WebUI code references remain

