# Softcurse Media Lab AI — Improvement and Upgrade Plan

## Goal

Turn the current working application into a reliable, testable, privacy-conscious media toolkit whose core features work without the removed sprite generator or bundled Stable Diffusion installation.

## Phase 1 — Quality foundation and regression protection

Status: Completed on 2026-08-30

Deliverables:

- Add a dedicated automated test project.
- Cover API endpoint security validation, image filters, invalid inputs, and temporary-file cleanup.
- Add one repeatable verification command for restore, build, tests, dependency vulnerability scanning, and repository hygiene.
- Keep Release builds at zero warnings.
- Document automated and manual smoke-test expectations.

Exit criteria:

- All automated tests pass on Windows.
- Release build completes with zero warnings and errors.
- No known-vulnerable NuGet dependency is reported.
- Verification can be run with `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1`.

Phase 1 result: 15 automated tests pass; the Release build has zero warnings and errors; the dependency vulnerability scan reports no known vulnerable packages.

## Phase 2 — Core image engine correctness

Priority: High

Status: Completed on 2026-08-30

Phase 2 result: Smart Select now uses correct click-seeded GrabCut behavior; corrupt or oversized image files are rejected before decoding; filters validate reads and writes; automatic watermark detection combines local contrast, brightness, location, and candidate-size filtering; undo/redo is capped at 20 states and 256 MB; 27 automated tests pass.

- Rename the current GrabCut-based magic wand so its behavior is honest, or implement and validate the real SAM ONNX pipeline.
- Add golden-image tests for masks, background removal, filters, resize, crop, and watermark inpainting.
- Centralize image decoding/encoding with size, format, and failure checks.
- Improve automatic watermark-mask detection beyond the bottom-right bright-object heuristic.
- Add cancellation and progress reporting to long-running ONNX/OpenCV operations.
- Validate undo/redo correctness and cap retained bitmap memory by bytes, not only item count.

Exit criteria: deterministic reference-image results, truthful selection UX, and no unbounded editor memory growth during repeated operations.

## Phase 3 — External integration hardening

Priority: High

Status: Completed on 2026-08-30

Phase 3 result: generative requests now use centralized URL construction, explicit timeouts, cancellation, response-size limits, structured errors, JSON validation, and result-image validation; Settings can test API health and warns about remote image disclosure; FFmpeg is configurable and testable, uses safe argument lists, drains diagnostic streams, and its active remux process tree is terminated on cancellation; 33 automated tests pass.

- Add an API connection test and capability discovery in Settings.
- Add request timeouts, cancellation, response-size limits, and structured API error parsing.
- Clearly warn when images will be sent to a non-loopback endpoint.
- Disable or label Generative Fill/Expand when the configured API is unavailable.
- Add FFmpeg path configuration, version detection, capability checks, and fully captured diagnostic logs.
- Make video cancellation terminate the child FFmpeg process safely.

Exit criteria: integrations fail quickly and clearly, never hang indefinitely, and expose actionable diagnostics.

## Phase 4 — UX, workflow, and accessibility upgrade

Priority: Medium

Status: Completed on 2026-08-31

Phase 4 result: UX flow documented; Softcurse gold focus rings and themed tooltips added; global module shortcuts implemented; Image Editor empty state and guidance upgraded; Settings made scroll-responsive; Generative Image API instructions, privacy context, validation, and keyboard flow improved; a live startup health bar reports Core, FFmpeg, and API readiness; shared status semantics distinguish ready, working, success, warning, and error states; disabled primary actions explain their prerequisites; restart-required settings are explicit.

Design direction: preserve the existing Softcurse palette, cyber styling, icon language, and brand identity used across the user's apps. Improvements should make that style more welcoming and self-explanatory rather than replacing it with a generic theme.

- Add a startup health panel for models, DirectML, FFmpeg, and optional API status.
- Standardize progress, cancel, success, and error states across every page.
- Add friendly first-use guidance, contextual empty states, concise tool descriptions, and actionable recovery suggestions without cluttering expert workflows.
- Group advanced controls behind progressive disclosure while keeping the primary action for each page visually obvious.
- Add consistent hover help, inline validation, success confirmation, and disabled-state explanations in the existing Softcurse visual language.
- Improve spacing, hierarchy, responsive behavior, and readable labels while retaining the current colors and cyber aesthetic.
- Preserve processed results consistently when navigating between modules.
- Add keyboard focus indicators, accessible names, logical tab order, scalable text, and contrast checks.
- Add recent files and consistent default output behavior to every export flow.
- Make settings that require restart explicit and offer a safe restart action.

Exit criteria: all primary workflows are keyboard-accessible and share consistent status/error behavior.

## Phase 5 — Performance and resource efficiency

Priority: Medium

Status: Completed on 2026-08-31

Phase 5 result: large watermark-removal models are no longer initialized during application startup and are skipped entirely when automatic detection finds no watermark; model readiness is exposed without forcing initialization; unchanged video frames pass directly to the encoder without a full-frame clone; video preview frames are disposed deterministically; generated working files are isolated inside an app-owned temporary directory and stale files from interrupted sessions are cleaned on startup. Successful CPU/DirectML inference is remembered and shown in Settings; a known DirectML runtime failure is not retried on every launch, while Settings provides an explicit retry action. Undo/redo is now file-backed, eliminating retained decoded bitmaps and fixing the processing-path mismatch after history navigation. UI-ready time and private memory are measured in the Core health tooltip. Repeatable benchmarks cover packaged startup, 1080p image operations, real 720p video decode/process/encode throughput, and LaMa CPU/DirectML inference. Current development-machine baseline: 603 ms packaged startup at approximately 45 MB private memory; 25.7 ms blur; 28.5 ms sharpen; 17.0 ms mask detection; and 145.1 fps video pass-through. LaMa measured 13.34 seconds cold and 2.14 seconds warm on CPU; DirectML failed on the model's MatMul operation and safely fell back to CPU, so that known fallback is now persisted. Performance budgets are 2 seconds to UI readiness and 24 fps for 720p pass-through. The Release build remains warning-free and all 38 tests pass.

- Lazy-load large models and expose model initialization state.
- Benchmark CPU versus DirectML and remember the last known-good provider.
- Reuse buffers and reduce full-image copies in filters, masks, and video processing.
- Stream large API/FFmpeg payloads instead of loading complete results into memory.
- Add startup, image-operation, and video-throughput benchmarks.
- Improve crash-safe temporary-file cleanup across previous sessions.

Exit criteria: measured startup/memory improvements and defined performance budgets for common image/video sizes.

## Phase 6 — Packaging, release, and maintenance

Priority: Medium

Status: In progress — installer lifecycle completed on 2026-08-31; production signing pending

Phase 6 progress: version `1.0.0` is centralized in `Directory.Build.props` with deterministic assembly/file metadata; the obsolete native C++ engine is formally archived in place and excluded from production work; a release script creates a clean versioned `win-x64` publish, strips development-only native symbols/libraries, optionally requires and includes LaMa, validates runtime contents and executable version metadata, and generates SHA-256 checksums. Certificate-store-based Authenticode and RFC 3161 timestamp support is implemented without accepting PFX/password input. The Inno definition consumes release-provided version/publish inputs and has explicit x64, logging, close/restart, and upgrade behavior. Unused satellite languages reduce the package from 142 files to 16 files. Inno Setup 7 compilation and automated clean install, launch, uninstall, and in-place upgrade mechanics passed. Windows CI builds and lifecycle-tests a model-free installer shell in addition to verification and dependency auditing. A changelog, security policy, Dependabot configuration, privacy-aware bug report form, and release checklist were added. The remaining exit-criteria dependency is a real Softcurse code-signing certificate and secure CI certificate policy; internal installers are correctly reported as unsigned.

- Decide whether to archive the unused C++ prototype or integrate it intentionally.
- Add CI for restore, Release build, tests, vulnerability scan, and packaging validation.
- Produce deterministic versioned builds and changelogs.
- Add application signing and installer/uninstaller verification.
- Validate clean installation, upgrade, settings migration, and uninstall on supported Windows versions.
- Add an issue template with logs, environment, reproduction steps, and expected/actual behavior.

Exit criteria: a reproducible, signed release that passes clean-install and upgrade testing.
