#include "watermark_engine.hpp"
#include "blend_modes.hpp"
#include <iostream>

// Using stb_image for simplicity in this mini-app
#define STB_IMAGE_IMPLEMENTATION
#include "stb_image.h"
#define STB_IMAGE_WRITE_IMPLEMENTATION
#include "stb_image_write.h"

namespace WatermarkRemover {

WatermarkEngine::WatermarkEngine() : width(0), height(0), channels(0) {}

WatermarkEngine::~WatermarkEngine() {}

bool WatermarkEngine::LoadImage(const std::string& filePath) {
    unsigned char* data = stbi_load(filePath.c_str(), &width, &height, &channels, 0);
    if (!data) {
        std::cerr << "Failed to load image: " << filePath << std::endl;
        return false;
    }
    imageData.assign(data, data + (width * height * channels));
    stbi_image_free(data);
    return true;
}

bool WatermarkEngine::SaveImage(const std::string& filePath) {
    if (imageData.empty()) return false;
    return stbi_write_png(filePath.c_str(), width, height, channels, imageData.data(), width * channels) != 0;
}

bool WatermarkEngine::RemoveWatermark() {
    if (imageData.empty()) return false;

    // Gemini watermark is usually in the bottom right.
    // We'll target the bottom-right corner.
    // Note: In a real app, we'd use a precise mask.
    // For now, we'll assume a standard Gemini watermark alpha and color.
    
    float watermarkAlpha = 0.3f; // Estimated alpha
    unsigned char watermarkColor = 255; // Assuming white star/text

    int startX = width - watermarkWidth - 20;
    int startY = height - watermarkHeight - 20;

    for (int y = startY; y < height; ++y) {
        for (int x = startX; x < width; ++x) {
            int idx = (y * width + x) * channels;
            for (int c = 0; c < std::min(channels, 3); ++c) {
                imageData[idx + c] = ReverseAlphaBlend(imageData[idx + c], watermarkColor, watermarkAlpha);
            }
        }
    }

    return true;
}

} // namespace WatermarkRemover
