# Changelog

All notable changes to Softcurse Media Lab AI are documented here. Versions follow semantic versioning.

## [Unreleased]

No unreleased changes.

## [1.0.0] - 2026-08-31

### Added

- Automatic watermark detection, click-seeded Smart Select, bounded file-backed undo/redo, startup health visibility, API/FFmpeg diagnostics, accessibility improvements, and repeatable performance benchmarks.
- Dedicated Audio / Video Converter sidebar navigation.
- Searchable in-app FAQ and user guide covering every module, optional API setup, privacy, formats, shortcuts, and troubleshooting.
- Self-contained Windows installer with .NET 8, FFmpeg, FFprobe, LaMa, DirectML, OpenCV, and native runtime dependencies.

### Changed

- Hardened external API, FFmpeg, temporary-file, settings, image-validation, and ONNX execution-provider behavior.
- Preserved and refined the Softcurse cyber visual identity across all modules.
- Clarified that the Generative Image API is an optional external-server client rather than a bundled generator.
- Made packaged FFmpeg the mandatory default and removed system PATH discovery.

### Removed

- Sprite generator and bundled Stable Diffusion WebUI integration.
