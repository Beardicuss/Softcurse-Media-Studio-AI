#ifndef WATERMARK_ENGINE_HPP
#define WATERMARK_ENGINE_HPP

#include <string>
#include <vector>

namespace WatermarkRemover {

struct Pixel {
    unsigned char r, g, b, a;
};

class WatermarkEngine {
public:
    WatermarkEngine();
    ~WatermarkEngine();

    bool LoadImage(const std::string& filePath);
    bool SaveImage(const std::string& filePath);
    bool RemoveWatermark();

private:
    int width;
    int height;
    int channels;
    std::vector<unsigned char> imageData;

    // Watermark characteristics (Gemini bottom-right)
    const int watermarkWidth = 100; // Approximate
    const int watermarkHeight = 40; // Approximate
};

} // namespace WatermarkRemover

#endif // WATERMARK_ENGINE_HPP
