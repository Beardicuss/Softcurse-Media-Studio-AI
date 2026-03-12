using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;

namespace SoftcurseMediaLabAI
{
    /// <summary>
    /// Sprite sheet assembly, GIF encoding, and background removal utilities.
    /// All methods are pure static and operate entirely on disk paths / Mat objects — no UI thread needed.
    /// </summary>
    public static class SpriteSheetService
    {
        // ══════════════════════════════════════════════════════════════════
        //  SPRITE SHEET BUILDER
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Assembles a list of frame PNG paths into a single sprite-sheet PNG.
        /// All frames are resized to the dimensions of the first frame.
        /// Returns the path to the assembled temp PNG.
        /// </summary>
        public static string BuildSheet(IReadOnlyList<string> framePaths, int columns)
        {
            if (framePaths.Count == 0)
                throw new ArgumentException("No frames to assemble.");

            using Mat first = Cv2.ImRead(framePaths[0], ImreadModes.Unchanged);
            if (first.Empty()) throw new InvalidOperationException($"Cannot read frame: {framePaths[0]}");

            int fw = first.Cols;
            int fh = first.Rows;
            bool hasAlpha = first.Channels() == 4;
            MatType matType = hasAlpha ? MatType.CV_8UC4 : MatType.CV_8UC3;

            int rows      = (int)Math.Ceiling(framePaths.Count / (double)columns);
            int sheetW    = fw * columns;
            int sheetH    = fh * rows;

            using Mat sheet = new Mat(sheetH, sheetW, matType, Scalar.All(0));

            for (int i = 0; i < framePaths.Count; i++)
            {
                int col = i % columns;
                int row = i / columns;

                using Mat frame = Cv2.ImRead(framePaths[i], hasAlpha ? ImreadModes.Unchanged : ImreadModes.Color);
                if (frame.Empty()) continue;

                // Normalise to sheet frame size if needed
                using Mat sized = new Mat();
                if (frame.Cols != fw || frame.Rows != fh)
                    Cv2.Resize(frame, sized, new Size(fw, fh));
                else
                    frame.CopyTo(sized);

                // Ensure channel count matches
                using Mat converted = new Mat();
                if (sized.Channels() != sheet.Channels())
                {
                    if (hasAlpha && sized.Channels() == 3)
                        Cv2.CvtColor(sized, converted, ColorConversionCodes.BGR2BGRA);
                    else if (!hasAlpha && sized.Channels() == 4)
                        Cv2.CvtColor(sized, converted, ColorConversionCodes.BGRA2BGR);
                    else
                        sized.CopyTo(converted);
                }
                else
                {
                    sized.CopyTo(converted);
                }

                Rect roi = new Rect(col * fw, row * fh, fw, fh);
                using Mat dest = new Mat(sheet, roi);
                converted.CopyTo(dest);
            }

            string outPath = Path.Combine(Path.GetTempPath(), $"sheet_{Guid.NewGuid()}.png");
            Cv2.ImWrite(outPath, sheet);
            return outPath;
        }

        // ══════════════════════════════════════════════════════════════════
        //  GIF ENCODER  (pure .NET — no ffmpeg dependency)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Encodes a list of frame PNGs into an animated GIF using .NET's built-in
        /// BitmapEncoder pipeline. Each frame is quantized to 256 colours (GIF limit).
        /// </summary>
        public static void BuildGif(IReadOnlyList<string> framePaths, string outputPath, int fps = 8)
        {
            if (framePaths.Count == 0) throw new ArgumentException("No frames for GIF.");

            int delayHundredths = Math.Max(1, 100 / fps); // GIF delay in 1/100ths of a second

            // We use a raw GIF byte stream writer because WPF's GifBitmapEncoder
            // does not support animated GIFs. This writes a minimal valid GIF89a.
            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var bw = new System.IO.BinaryWriter(fs);

            // Read all frames as byte arrays (convert to indexed 256-colour internally)
            var gifFrames = new List<(int w, int h, byte[] pixels, byte[] palette)>();

            int targetW = 0, targetH = 0;

            foreach (string fp in framePaths)
            {
                using Mat src  = Cv2.ImRead(fp, ImreadModes.Color);
                if (src.Empty()) continue;

                if (targetW == 0) { targetW = src.Cols; targetH = src.Rows; }

                using Mat sized = new Mat();
                if (src.Cols != targetW || src.Rows != targetH)
                    Cv2.Resize(src, sized, new Size(targetW, targetH));
                else
                    src.CopyTo(sized);

                // Quantize to 256 colours using OpenCV
                using Mat samples = sized.Reshape(1, sized.Rows * sized.Cols);
                using Mat fSamples = new Mat();
                samples.ConvertTo(fSamples, MatType.CV_32F);

                int K = 256;
                using Mat labels  = new Mat();
                using Mat centers = new Mat();
                var criteria = new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 10, 1.0);
                Cv2.Kmeans(fSamples, K, labels, criteria, 1, KMeansFlags.PpCenters, centers);

                byte[] palette = new byte[K * 3];
                byte[] pixels  = new byte[targetW * targetH];

                for (int k = 0; k < K; k++)
                {
                    palette[k * 3]     = (byte)Math.Clamp(centers.At<float>(k, 2), 0, 255); // R
                    palette[k * 3 + 1] = (byte)Math.Clamp(centers.At<float>(k, 1), 0, 255); // G
                    palette[k * 3 + 2] = (byte)Math.Clamp(centers.At<float>(k, 0), 0, 255); // B
                }

                for (int i = 0; i < targetW * targetH; i++)
                    pixels[i] = (byte)labels.At<int>(i);

                gifFrames.Add((targetW, targetH, pixels, palette));
            }

            if (gifFrames.Count == 0) throw new InvalidOperationException("No valid frames for GIF.");

            WriteGif89a(bw, gifFrames, delayHundredths, targetW, targetH);
        }

        // ── Minimal GIF89a writer ────────────────────────────────────────
        private static void WriteGif89a(
            System.IO.BinaryWriter bw,
            List<(int w, int h, byte[] pixels, byte[] palette)> frames,
            int delayCs, int logW, int logH)
        {
            // GIF header
            bw.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));

            // Logical screen descriptor
            bw.Write((ushort)logW);
            bw.Write((ushort)logH);
            bw.Write((byte)0x70); // global colour table: 256 colours (7+1 = 8 bit), colour res = 8
            bw.Write((byte)0);    // background colour index
            bw.Write((byte)0);    // pixel aspect ratio

            // Global colour table (use first frame's palette)
            bw.Write(frames[0].palette);
            // Pad to 256*3 if needed
            int padBytes = 256 * 3 - frames[0].palette.Length;
            if (padBytes > 0) bw.Write(new byte[padBytes]);

            // Netscape application extension (looping)
            bw.Write((byte)0x21); // extension introducer
            bw.Write((byte)0xFF); // app extension label
            bw.Write((byte)11);   // block size
            bw.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            bw.Write((byte)3);    // sub-block size
            bw.Write((byte)1);    // sub-block ID
            bw.Write((ushort)0);  // loop count (0 = infinite)
            bw.Write((byte)0);    // block terminator

            foreach (var (w, h, pixels, palette) in frames)
            {
                // Graphic control extension (delay)
                bw.Write((byte)0x21); // extension introducer
                bw.Write((byte)0xF9); // graphic control label
                bw.Write((byte)4);    // block size
                bw.Write((byte)0);    // packed field
                bw.Write((ushort)delayCs);
                bw.Write((byte)0);    // transparent colour index (unused)
                bw.Write((byte)0);    // block terminator

                // Image descriptor
                bw.Write((byte)0x2C); // image separator
                bw.Write((ushort)0);  // left
                bw.Write((ushort)0);  // top
                bw.Write((ushort)w);
                bw.Write((ushort)h);
                // Use local colour table with this frame's palette
                bw.Write((byte)0x87); // local colour table flag + 256 colours

                // Local colour table
                bw.Write(palette);
                int lpad = 256 * 3 - palette.Length;
                if (lpad > 0) bw.Write(new byte[lpad]);

                // LZW minimum code size
                bw.Write((byte)8);

                // LZW-encode pixels
                byte[] lzw = LzwEncode(pixels, 8);

                // Write LZW data in 255-byte sub-blocks
                int offset = 0;
                while (offset < lzw.Length)
                {
                    int blockLen = Math.Min(255, lzw.Length - offset);
                    bw.Write((byte)blockLen);
                    bw.Write(lzw, offset, blockLen);
                    offset += blockLen;
                }
                bw.Write((byte)0); // block terminator
            }

            bw.Write((byte)0x3B); // GIF trailer
        }

        // ── Simple LZW encoder for GIF ───────────────────────────────────
        private static byte[] LzwEncode(byte[] pixels, int minCodeSize)
        {
            using var ms = new MemoryStream();

            int clearCode = 1 << minCodeSize;
            int eofCode   = clearCode + 1;

            var table = new Dictionary<string, int>();
            for (int i = 0; i < clearCode; i++) table[((char)i).ToString()] = i;

            int nextCode = eofCode + 1;
            int codeSize = minCodeSize + 1;

            var bits = new List<bool>();

            void emit(int code)
            {
                for (int b = 0; b < codeSize; b++)
                    bits.Add(((code >> b) & 1) == 1);
                if (nextCode > (1 << codeSize) && codeSize < 12) codeSize++;
            }

            emit(clearCode);

            string buffer = ((char)pixels[0]).ToString();
            for (int i = 1; i < pixels.Length; i++)
            {
                string next = buffer + (char)pixels[i];
                if (table.ContainsKey(next))
                {
                    buffer = next;
                }
                else
                {
                    emit(table[buffer]);
                    if (nextCode < 4096) { table[next] = nextCode++; }
                    buffer = ((char)pixels[i]).ToString();
                }
            }
            emit(table[buffer]);
            emit(eofCode);

            // Pack bits into bytes
            byte[] result = new byte[(bits.Count + 7) / 8];
            for (int i = 0; i < bits.Count; i++)
                if (bits[i]) result[i / 8] |= (byte)(1 << (i % 8));
            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        //  BACKGROUND REMOVAL  (GrabCut border-seeded)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Removes the background using a border-seeded GrabCut.
        /// Seeds a thin border strip as definite background, the inner 60% as probable foreground.
        /// Outputs a BGRA PNG with the background channel zeroed.
        /// </summary>
        public static void RemoveBackgroundGrabCut(string inputPath, string outputPath)
        {
            using Mat img    = Cv2.ImRead(inputPath, ImreadModes.Color);
            if (img.Empty()) throw new InvalidOperationException($"Cannot read: {inputPath}");

            int w = img.Cols, h = img.Rows;

            using Mat gcMask   = new Mat(h, w, MatType.CV_8UC1, Scalar.All((int)GrabCutClasses.PR_BGD));
            using Mat bgdModel = new Mat();
            using Mat fgdModel = new Mat();

            // Seed outer border as definite background
            int borderPx = Math.Max(2, Math.Min(w, h) / 20);
            // top / bottom rows
            gcMask.RowRange(0, borderPx).SetTo(Scalar.All((int)GrabCutClasses.BGD));
            gcMask.RowRange(h - borderPx, h).SetTo(Scalar.All((int)GrabCutClasses.BGD));
            // left / right cols
            gcMask.ColRange(0, borderPx).SetTo(Scalar.All((int)GrabCutClasses.BGD));
            gcMask.ColRange(w - borderPx, w).SetTo(Scalar.All((int)GrabCutClasses.BGD));

            // Seed inner rectangle as probable foreground
            int margin = borderPx + Math.Max(2, Math.Min(w, h) / 10);
            Rect fgRect = new Rect(margin, margin, w - margin * 2, h - margin * 2);
            if (fgRect.Width > 0 && fgRect.Height > 0)
                gcMask[fgRect].SetTo(Scalar.All((int)GrabCutClasses.PR_FGD));

            try
            {
                Cv2.GrabCut(img, gcMask, new Rect(), bgdModel, fgdModel,
                            5, GrabCutModes.InitWithMask);
            }
            catch { /* if GrabCut fails, output as-is with full opacity */ }

            // Combine probable + definite foreground
            using Mat fgMask  = new Mat();
            using Mat pfgMask = new Mat();
            Cv2.Compare(gcMask, new Scalar((int)GrabCutClasses.FGD),    fgMask,  CmpType.EQ);
            Cv2.Compare(gcMask, new Scalar((int)GrabCutClasses.PR_FGD), pfgMask, CmpType.EQ);
            using Mat alphaMask = new Mat();
            Cv2.BitwiseOr(fgMask, pfgMask, alphaMask);

            // Smooth the mask edges
            Cv2.GaussianBlur(alphaMask, alphaMask, new Size(3, 3), 0);
            Cv2.Threshold(alphaMask, alphaMask, 127, 255, ThresholdTypes.Binary);

            // Build BGRA output
            using Mat bgra = new Mat();
            Cv2.CvtColor(img, bgra, ColorConversionCodes.BGR2BGRA);

            // Zero alpha where background
            Mat[] channels = Cv2.Split(bgra);
            alphaMask.CopyTo(channels[3]); // replace alpha channel
            using Mat result = new Mat();
            Cv2.Merge(channels, result);
            foreach (var ch in channels) ch.Dispose();

            Cv2.ImWrite(outputPath, result);
        }
    }
}
