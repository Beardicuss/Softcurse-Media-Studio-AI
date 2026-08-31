# Softcurse Media Lab AI — UX Flow

## Design constraint

The established Softcurse visual identity is preserved: deep navy surfaces, cyan primary accents, gold secondary accents, geometric HUD framing, compact technical typography, and custom icon language. UX improvements clarify the product without replacing that identity.

## Global navigation

```text
Launch
  → Image Editor (default)
  ├─ Toolkit Lab
  ├─ Video Retouch
  ├─ AV Converter
  ├─ Generative Image API (optional external server)
  ├─ FAQ / User Guide (searchable help)
  └─ Settings
       ├─ Test generative API
       └─ Test FFmpeg
```

The sidebar remains available from every module. Tab moves through sidebar actions and then into the active page. Every actionable control must display a visible gold/cyan keyboard-focus indicator.

## Image Editor

```text
Empty state
  → Load or drop image
  → Choose direct action OR choose selection/editing tool
  → Processing state (primary action disabled, progress/status visible)
  → Result state
       ├─ Compare
       ├─ Undo / Redo
       ├─ Copy
       ├─ Save
       └─ Continue editing
```

The empty state explains the shortest successful path. Disabled actions explain their prerequisite through tooltips. Status text describes what happened and the next useful action.

## Optional generative workflow

```text
No image → explain how to load one in Image Editor
Image ready → enter prompt → Generate
  ├─ Local endpoint → process
  ├─ Remote endpoint → privacy confirmation → process or cancel
  ├─ Cancel → return to editable prompt
  └─ Failure → actionable API guidance
Success → open result in Image Editor
```

## Forge Lab

```text
Choose Retouch or Converter
  → Select media
  → Validate FFmpeg when required
  → Choose output
  → Process with progress/cancel
  → Success path and output location OR actionable failure
```

## Settings

Settings scroll at smaller window sizes. Test actions do not save changes. Save validates all paths and endpoints before persistence. Execution-provider changes clearly state that restart is required.

## FAQ / User Guide

The in-app guide is available from every module and explicitly separates bundled/local features from optional API functionality. Search filters complete feature cards by action, format, requirement, privacy topic, shortcut, or troubleshooting term. A direct Settings action is available from the API requirements card.
