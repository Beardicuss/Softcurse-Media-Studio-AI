#include "watermark_engine.hpp"
#include <iostream>

int main(int argc, char* argv[]) {
    if (argc < 3) {
        std::cout << "Usage: GeminiWatermarkRemover <input_path> <output_path>" << std::endl;
        return 1;
    }

    std::string inputPath = argv[1];
    std::string outputPath = argv[2];

    WatermarkRemover::WatermarkEngine engine;
    if (!engine.LoadImage(inputPath)) {
        return 1;
    }

    std::cout << "Processing image..." << std::endl;
    if (engine.RemoveWatermark()) {
        if (engine.SaveImage(outputPath)) {
            std::cout << "Watermark removed successfully. Saved to: " << outputPath << std::endl;
        } else {
            std::cerr << "Failed to save image." << std::endl;
            return 1;
        }
    } else {
        std::cerr << "Failed to remove watermark." << std::endl;
        return 1;
    }

    return 0;
}
