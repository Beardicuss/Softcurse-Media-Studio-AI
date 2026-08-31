# Manual smoke-test checklist

Run this checklist before a release or after changes to UI workflows.

## Startup and settings

- Launch the Release build and confirm the Image Editor opens without an error dialog.
- Confirm the top health bar updates Core, FFmpeg, and API indicators without blocking navigation.
- Hover each health indicator and confirm its tooltip explains the current state.
- Resize the window to its minimum size and confirm Settings scrolls without clipping controls.
- Navigate with Tab and confirm every focused button, text field, combo box, and toggle has a visible gold focus ring.
- Verify Ctrl+1 through Ctrl+7 open Image Editor, Toolkit Lab, Video Retouch, AV Converter, Generative Image API, Settings, and FAQ respectively.
- Open FAQ, search for API, MP3, privacy, GPU, and a nonexistent term; confirm cards filter correctly, the empty result appears, CLEAR restores all cards, and OPEN SETTINGS navigates correctly.
- Open AV Converter directly from the sidebar, add an audio or video file, choose an output format, and verify FFmpeg produces the expected file.
- Open Settings, choose an existing output directory, select CPU, save, restart, and confirm the values persist.
- Repeat with DirectML on a supported machine and confirm Retouch either succeeds or falls back cleanly.
- Enter an invalid API URL and confirm Settings refuses to save it with an actionable message.

## Image Editor

- From the empty state, choose an image with the central CHOOSE IMAGE action.
- Load PNG and JPEG images through both the button and drag-and-drop.
- Run automatic and manual Retouch; confirm the button cannot be triggered twice while processing.
- Run Background Removal, Blur, Sharpen, Noise, and Upscale; confirm Save and Copy become available.
- Verify Undo, Redo, Reset, Compare, zoom, and keyboard shortcuts.
- Save PNG and JPEG results and confirm the dialog opens in the configured default output directory.

## Toolkit Lab

- Resize, crop, convert formats, extract a palette, inspect/strip metadata, and compare two images.
- Confirm resize and crop dialogs use the configured default output directory.
- Open every saved result in a separate image viewer to confirm it is not corrupt.

## Video Retouch and AV Converter

- Confirm the app reports a useful reinstall warning if its bundled FFmpeg file is temporarily unavailable.
- Using bundled FFmpeg, convert one video and one audio file whose paths contain spaces.
- Retouch a short video with and without audio and confirm the result plays.
- Cancel a long video operation and confirm the UI becomes usable again.

## Optional generative API

- With no API running, verify Generative Fill and Expand fail with clear connection guidance.
- With a trusted compatible API, test Generate, Cancel, Expand, and Upscale fallback behavior.
- Confirm no sprite-generator navigation or Stable Diffusion launcher remains.
- Verify Ctrl+Enter starts generation and Escape cancels while a request is active.
