# Local OCR language assets

These are unmodified `eng.traineddata` and `ara.traineddata` from
https://github.com/tesseract-ocr/tessdata_fast (Apache 2.0; see LICENSE).
They are copied to the application's `tessdata` directory during build and publish.
No language download or network access happens when reading an invoice.

Tesseract .NET wrapper: https://github.com/charlesw/tesseract (Apache 2.0).
Native Tesseract: https://github.com/tesseract-ocr/tesseract (Apache 2.0).
Leptonica: https://github.com/DanBloomberg/leptonica (BSD 2-Clause).
The NuGet Tesseract 5.2.0 package provides the Windows x86/x64 binaries.
Windows client PCs need the matching Microsoft Visual C++ 2015-2022 runtime.

Downloaded 2026-10-08; SHA-256:

eng.traineddata: 7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2
ara.traineddata: e3206d3dc87fd50c24a0fb9f01838615911d25168f4e64415244b67d2bb3e729
