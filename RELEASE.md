# Release process

Softcurse Media Lab AI uses `Directory.Build.props` as the version source. Release versions contain exactly three numeric components, such as `1.0.0`.

## Local validation

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify.ps1
powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -SkipInstaller
```

The publish-only command creates a clean versioned directory under `artifacts/release/`, validates required runtime files and version metadata, rejects debug/user files, and writes `SHA256SUMS.txt`.

## Full installer

A complete installer requires:

- `gui/models/lama_fp32.onnx` supplied through the private release environment or approved model-distribution process;
- the complete `third_party/ffmpeg` runtime and licensing payload (`ffmpeg.exe`, `ffprobe.exe`, `LICENSE.txt`, `SOURCE.txt`, and `BUILD-README.txt`);
- the self-contained .NET runtime produced automatically by `scripts/release.ps1`;
- Inno Setup 6 or 7 with `iscc.exe` available;
- a Windows code-signing certificate before public distribution.

Build the unsigned installer for internal testing:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -RequireModel
```

The release script refuses to create any installer when the LaMa model or bundled FFmpeg payload is missing. `-SkipInstaller` is reserved for source/CI validation and must never be published as an application release.

Exercise a clean install, hidden launch, and uninstall inside the workspace artifacts directory:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/test-installer.ps1 `
  -InstallerPath artifacts/release/1.0.0/installer/SoftcurseMediaLabAI_Setup_v1.0.0.exe `
  -ExpectedVersion 1.0.0 -RequireModel
```

Pass `-PreviousInstallerPath` to the same command to validate an in-place upgrade before launch and uninstall.

Do not label a CI shell artifact as a full release when its package validator warns that the LaMa model is absent.

## Manual release checklist

1. Move completed entries from `Unreleased` into the target section in `CHANGELOG.md`.
2. Update the version once in `Directory.Build.props`.
3. Run verification and both performance benchmarks.
4. Build with `-RequireModel` and verify `SHA256SUMS.txt`.
5. Sign the application and installer with the Softcurse certificate and a trusted RFC 3161 timestamp service.
6. Test clean install, launch, image processing, upgrade over the previous version, uninstall, and preservation of user settings on supported Windows versions.
7. Publish the installer, checksums, changelog excerpt, and known limitations.

The release script supports Authenticode signing from the Windows certificate store without accepting a PFX or password:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -RequireModel `
  -SigningCertificateThumbprint $env:SOFTCURSE_SIGNING_THUMBPRINT `
  -TimestampUrl $env:SOFTCURSE_TIMESTAMP_URL
```

Signing remains disabled until certificate storage and CI secret policy are configured. Never commit a `.pfx`, password, API key, signing token, certificate thumbprint, or timestamp-service configuration.
