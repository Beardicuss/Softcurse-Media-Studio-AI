# Archived native prototype

This directory contains the original experimental C++ command-line watermark remover. It is retained only as historical reference and is not compiled, packaged, or used by Softcurse Media Lab AI.

The prototype assumes a fixed bottom-right watermark region and reverses a guessed alpha blend. That behavior is less accurate and less safe than the maintained C# pipeline, which uses validated image loading, automatic/manual masks, OpenCV processing, and LaMa inference.

Do not add new features here. Production work belongs in `gui/`. The prototype can be removed in a future repository-history cleanup after confirming it has no external consumers.
