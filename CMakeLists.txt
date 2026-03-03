cmake_minimum_required(VERSION 3.10)
project(GeminiWatermarkRemover)

set(CMAKE_CXX_STANDARD 17)

include_directories(src)

add_executable(GeminiWatermarkRemover 
    src/main.cpp 
    src/watermark_engine.cpp
)
