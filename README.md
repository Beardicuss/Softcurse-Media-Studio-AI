# Softcurse Media Studio AI

![Softcurse Media Studio AI](assets/media.png)

A powerful, hardware-accelerated Windows WPF application for advanced image manipulation, AI-powered object removal, video processing, sprite generation, and generative AI expansion.

> **Latest Release: v2.9** — Animated logo with glitch effect, configurable model directory, SD WebUI download link in Sprite Generator.

## Features

### Image Editor
- **Auto Mode:** Automatically detect and remove watermarks using LaMa inpainting
- **Cyber Brush:** Paint custom masks for precise object removal
- **Eraser:** Refine masks and protect specific areas
- **Poly Lasso:** Draw point-to-point geometric masks
- **Magic Wand (SAM):** Click any object to auto-generate a mask using Segment Anything Model

### AI-Powered Tools
- **Background Removal:** Instantly strip image backgrounds
- **Retouch (LaMa Inpainting):** Seamlessly remove objects/watermarks using ONNX models (DirectML GPU + automatic CPU fallback)
- **Expand:** Outpaint and extend image boundaries via Stable Diffusion
- **Upscale:** Enhance resolution using AI ESRGAN
- **Generative Fill:** Inpaint masked regions with text prompts via Stable Diffusion

### Additional Modules
- **Mask Processor:** Batch mask processing and refinement
- **Video Lab:** Frame-by-frame watermark removal from video files with audio remuxing
- **AI Generation Hub:** Text-to-image generation with positive/negative prompt control
- **Sprite Generator:** Generate sprite sheets from text descriptions (requires SD WebUI — download link built in)

### Settings & Configuration
- **Execution Provider:** Switch between CPU, CUDA, and DirectML GPU acceleration
- **Model Directory:** Custom path for ONNX models (auto-detected by default)
- **API Endpoint:** Configurable SD WebUI connection for generative features
- **Default Output Folder:** Set your preferred save location

## Requirements

### Core Application
- Windows 10/11 (64-bit)
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

### AI Generative Features (Optional)
For Generative Fill, Expand, Upscale, and Sprite Generator features, run a [Stable Diffusion WebUI](https://github.com/AUTOMATIC1111/stable-diffusion-webui) instance with the `--api` flag.
- Configure API endpoint via **Settings** tab (default: `http://127.0.0.1:7860/`)
- A **DOWNLOAD SD WEBUI** button is available directly in the Sprite Generator page

### GPU Acceleration
- **DirectML** (recommended) — works with any GPU, built into Windows 10+. No extra installation.
- **CUDA** — requires NVIDIA GPU with drivers installed
- **CPU** — always available as fallback

## Installation

### From Installer
1. Download `SoftcurseMediaStudioAI_Setup_v2.9.exe` from Releases
2. Run the installer and follow the prompts
3. Launch from desktop shortcut or Start Menu

### From Source
```powershell
git clone https://github.com/Beardicuss/Softcurse-Media-Studio-AI.git
cd Softcurse-Media-Studio-AI
dotnet run --project gui/GeminiWatermarkRemover.csproj
```

### Portable (Publish Folder)
Copy the `publish/` folder contents to any directory and run `SoftcurseMediaStudioAI.exe`.

## Usage
1. Launch the application — sidebar icons animate to indicate the app is ready
2. **Settings:** Configure Default Output Folder, Model Directory, Execution Provider, and API endpoints
3. **Image Editor:** Drag/drop an image or click LOAD IMAGE. Select your tool from the bottom panel (highlighted tool glows cyan when active)
4. Draw your mask and click **APPLY MASK (RETOUCH)** to remove objects
5. Use **BACKGROUND**, **EXPAND**, **UPSCALE**, or switch to other modules
6. Click **SAVE** to export results

## Technology Stack
- **UI Framework:** WPF with ModernWpfUI, custom cyberpunk HUD theme
- **Image Processing:** OpenCvSharp4
- **AI Inference:** Microsoft.ML.OnnxRuntime (DirectML GPU + CPU fallback)
- **Video Processing:** OpenCV VideoCapture/VideoWriter + FFmpeg audio remux
- **Network API:** HttpClient REST for Stable Diffusion API integration

## Building the Installer
Requires [Inno Setup](https://jrsoftware.org/isinfo.php):
```powershell
dotnet publish gui/GeminiWatermarkRemover.csproj -c Release -r win-x64 --self-contained false -o publish
iscc media.iss
```
The installer will be output to the `installer/` directory.
