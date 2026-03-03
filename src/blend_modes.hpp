#ifndef BLEND_MODES_HPP
#define BLEND_MODES_HPP

#include <algorithm>
#include <cmath>

namespace WatermarkRemover {

/**
 * @brief Reverse Alpha Blending
 * 
 * The standard alpha blending formula is:
 * C_out = C_src * alpha + C_bg * (1 - alpha)
 * 
 * To recover C_bg (the original image):
 * C_bg = (C_out - C_src * alpha) / (1 - alpha)
 * 
 * @param out The color of the pixel with the watermark (0-255)
 * @param src The color of the watermark pixel (0-255)
 * @param alpha The alpha value of the watermark (0.0-1.0)
 * @return The recovered background color (0-255)
 */
inline unsigned char ReverseAlphaBlend(unsigned char out, unsigned char src, float alpha) {
    if (alpha >= 1.0f) return 0; // Cannot recover if fully opaque
    if (alpha <= 0.0f) return out; // No watermark

    float result = (static_cast<float>(out) - static_cast<float>(src) * alpha) / (1.0f - alpha);
    return static_cast<unsigned char>(std::clamp(result, 0.0f, 255.0f));
}

} // namespace WatermarkRemover

#endif // BLEND_MODES_HPP
