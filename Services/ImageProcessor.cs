using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaApp.ViewModels;

namespace AvaloniaApp.Services;

public enum ImageOperation { None, Sum, Difference, Product, Average, Min, Max, Mask }
public enum ChannelMode { RGB, R, G, B, RG, GB, RB }
public enum MaskShape { None, Circle, Square, Rectangle }
public enum BinarizationMethod { None, Gavrilov, Otsu, Niblack, Sauvola, Wolf, BradleyRoth }

public class ProcessResult
{
    public WriteableBitmap? Image { get; set; }
    public int[]? Histogram { get; set; }
}

public class ImageProcessor
{
    private static void ApplyBinarization(byte[] result, int w, int h, BinarizationMethod method, int a, double k)
    {
        byte[] I = new byte[w * h];
        for (int i = 0; i < w * h; i++)
        {
            byte b = result[i * 4];
            byte g = result[i * 4 + 1];
            byte r = result[i * 4 + 2];
            I[i] = (byte)Math.Clamp((int)(0.2125 * r + 0.7154 * g + 0.0721 * b), 0, 255);
        }

        byte[] B = new byte[w * h];
        if (method == BinarizationMethod.Gavrilov)
        {
            double sum = 0;
            for (int i = 0; i < w * h; i++) sum += I[i];
            double t = sum / (w * h);
            for (int i = 0; i < w * h; i++) B[i] = I[i] < t ? (byte)0 : (byte)255;
        }
        else if (method == BinarizationMethod.Otsu)
        {
            long[] hist = new long[256];
            for (int i = 0; i < w * h; i++) hist[I[i]]++;
            int total = w * h;
            float sum = 0;
            for (int i = 0; i < 256; i++) sum += i * hist[i];
            float sumB = 0;
            int wB = 0, wF = 0;
            float varMax = 0;
            int threshold = 0;
            for (int t = 0; t < 256; t++)
            {
                wB += (int)hist[t];
                if (wB == 0) continue;
                wF = total - wB;
                if (wF == 0) break;
                sumB += (float)(t * hist[t]);
                float mB = sumB / wB;
                float mF = (sum - sumB) / wF;
                float varBetween = (float)wB * (float)wF * (mB - mF) * (mB - mF);
                if (varBetween > varMax)
                {
                    varMax = varBetween;
                    threshold = t;
                }
            }
            for (int i = 0; i < w * h; i++) B[i] = I[i] < threshold ? (byte)0 : (byte)255;
        }
        else
        {
            long[,] sum = new long[w, h];
            long[,] sqSum = new long[w, h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    long val = I[y * w + x];
                    long s = val;
                    long sq = val * val;
                    if (y > 0) { s += sum[x, y - 1]; sq += sqSum[x, y - 1]; }
                    if (x > 0) { s += sum[x - 1, y]; sq += sqSum[x - 1, y]; }
                    if (x > 0 && y > 0) { s -= sum[x - 1, y - 1]; sq -= sqSum[x - 1, y - 1]; }
                    sum[x, y] = s;
                    sqSum[x, y] = sq;
                }
            }

            int R_val = 128;
            int hw = a / 2;
            
            double maxStdDev = 0;
            byte minI = 255;
            if (method == BinarizationMethod.Wolf)
            {
                for (int i = 0; i < w * h; i++) if (I[i] < minI) minI = I[i];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int x1 = Math.Max(0, x - hw), y1 = Math.Max(0, y - hw);
                        int x2 = Math.Min(w - 1, x + hw), y2 = Math.Min(h - 1, y + hw);
                        long curSum = sum[x2, y2];
                        long curSqSum = sqSum[x2, y2];
                        if (x1 > 0) { curSum -= sum[x1 - 1, y2]; curSqSum -= sqSum[x1 - 1, y2]; }
                        if (y1 > 0) { curSum -= sum[x2, y1 - 1]; curSqSum -= sqSum[x2, y1 - 1]; }
                        if (x1 > 0 && y1 > 0) { curSum += sum[x1 - 1, y1 - 1]; curSqSum += sqSum[x1 - 1, y1 - 1]; }
                        int count = (x2 - x1 + 1) * (y2 - y1 + 1);
                        double M = (double)curSum / count;
                        double variance = (double)curSqSum / count - M * M;
                        if (variance > 0)
                        {
                            double stdDev = Math.Sqrt(variance);
                            if (stdDev > maxStdDev) maxStdDev = stdDev;
                        }
                    }
                }
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int x1 = Math.Max(0, x - hw), y1 = Math.Max(0, y - hw);
                    int x2 = Math.Min(w - 1, x + hw), y2 = Math.Min(h - 1, y + hw);
                    long curSum = sum[x2, y2];
                    long curSqSum = sqSum[x2, y2];
                    if (x1 > 0) { curSum -= sum[x1 - 1, y2]; curSqSum -= sqSum[x1 - 1, y2]; }
                    if (y1 > 0) { curSum -= sum[x2, y1 - 1]; curSqSum -= sqSum[x2, y1 - 1]; }
                    if (x1 > 0 && y1 > 0) { curSum += sum[x1 - 1, y1 - 1]; curSqSum += sqSum[x1 - 1, y1 - 1]; }

                    int count = (x2 - x1 + 1) * (y2 - y1 + 1);
                    double M = (double)curSum / count;
                    byte pixel = I[y * w + x];
                    
                    if (method == BinarizationMethod.BradleyRoth)
                    {
                        if (pixel * count < curSum * (1.0 - k)) B[y * w + x] = 0;
                        else B[y * w + x] = 255;
                    }
                    else
                    {
                        double t = 0;
                        double variance = (double)curSqSum / count - M * M;
                        double stdDev = variance > 0 ? Math.Sqrt(variance) : 0;

                        if (method == BinarizationMethod.Niblack) t = M + k * stdDev;
                        else if (method == BinarizationMethod.Sauvola) t = M * (1.0 + k * (stdDev / R_val - 1.0));
                        else if (method == BinarizationMethod.Wolf) t = (1 - k) * M + k * minI + k * (stdDev / maxStdDev) * (M - minI);

                        B[y * w + x] = pixel < t ? (byte)0 : (byte)255;
                    }
                }
            }
        }

        for (int i = 0; i < w * h; i++)
        {
            result[i * 4] = B[i];
            result[i * 4 + 1] = B[i];
            result[i * 4 + 2] = B[i];
        }
    }

    public static async Task<ProcessResult> ProcessLayersAsync(
        List<LayerViewModel> layers, 
        byte[]? lut,
        bool enableBinarization = false,
        BinarizationMethod binMethod = BinarizationMethod.None,
        int binWindowSize = 15,
        double binK = 0.2)
    {
        if (layers == null || layers.Count == 0) return new ProcessResult();

        return await Task.Run(() => 
        {
            try
            {
                int targetW = 0;
                int targetH = 0;
                foreach (var l in layers)
                {
                    if (l.BitmapCache != null)
                    {
                        targetW = Math.Max(targetW, l.BitmapCache.PixelSize.Width);
                        targetH = Math.Max(targetH, l.BitmapCache.PixelSize.Height);
                    }
                }

                if (targetW == 0 || targetH == 0) return new ProcessResult();

                byte[] result = new byte[targetW * targetH * 4];

                foreach (var layer in layers)
                {
                    if (layer.BitmapCache == null || layer.Opacity <= 0) continue;

                    byte[] layerPixels = GetScaledPixels(layer.BitmapCache, targetW, targetH);
                    
                    double opacity = layer.Opacity;
                    var operation = layer.BlendMode;
                    var channelMode = layer.SelectedChannelMode;
                    var maskShape = layer.SelectedMaskShape;

                    for (int y = 0; y < targetH; y++)
                    {
                        for (int x = 0; x < targetW; x++)
                        {
                            int index = (y * targetW + x) * 4;
                            
                            byte baseB = result[index];
                            byte baseG = result[index + 1];
                            byte baseR = result[index + 2];
                            byte baseA = result[index + 3];

                            byte lB = layerPixels[index];
                            byte lG = layerPixels[index + 1];
                            byte lR = layerPixels[index + 2];
                            
                            if (maskShape != MaskShape.None)
                            {
                                if (!IsInMask(x, y, targetW, targetH, maskShape))
                                {
                                    lR = lG = lB = 0;
                                }
                            }

                            if (channelMode != ChannelMode.RGB && channelMode != ChannelMode.R && channelMode != ChannelMode.RG && channelMode != ChannelMode.RB) lR = 0;
                            if (channelMode != ChannelMode.RGB && channelMode != ChannelMode.G && channelMode != ChannelMode.RG && channelMode != ChannelMode.GB) lG = 0;
                            if (channelMode != ChannelMode.RGB && channelMode != ChannelMode.B && channelMode != ChannelMode.RB && channelMode != ChannelMode.GB) lB = 0;

                            int outR = lR, outG = lG, outB = lB;

                            if (operation == ImageOperation.Sum)
                            {
                                outB = Math.Min(255, baseB + lB);
                                outG = Math.Min(255, baseG + lG);
                                outR = Math.Min(255, baseR + lR);
                            }
                            else if (operation == ImageOperation.Difference)
                            {
                                outB = Math.Abs(baseB - lB);
                                outG = Math.Abs(baseG - lG);
                                outR = Math.Abs(baseR - lR);
                            }
                            else if (operation == ImageOperation.Product || operation == ImageOperation.Mask)
                            {
                                outB = baseB * lB / 255;
                                outG = baseG * lG / 255;
                                outR = baseR * lR / 255;
                            }
                            else if (operation == ImageOperation.Average)
                            {
                                outB = (baseB + lB) / 2;
                                outG = (baseG + lG) / 2;
                                outR = (baseR + lR) / 2;
                            }
                            else if (operation == ImageOperation.Min)
                            {
                                outB = Math.Min(baseB, lB);
                                outG = Math.Min(baseG, lG);
                                outR = Math.Min(baseR, lR);
                            }
                            else if (operation == ImageOperation.Max)
                            {
                                outB = Math.Max(baseB, lB);
                                outG = Math.Max(baseG, lG);
                                outR = Math.Max(baseR, lR);
                            }
                            else 
                            {
                                outB = lB;
                                outG = lG;
                                outR = lR;
                            }

                            result[index]     = (byte)(baseB * (1.0 - opacity) + outB * opacity);
                            result[index + 1] = (byte)(baseG * (1.0 - opacity) + outG * opacity);
                            result[index + 2] = (byte)(baseR * (1.0 - opacity) + outR * opacity);
                            result[index + 3] = 255; 
                        }
                    }
                }

                if (enableBinarization && binMethod != BinarizationMethod.None)
                {
                    ApplyBinarization(result, targetW, targetH, binMethod, binWindowSize, binK);
                }

                int[] hist = new int[256];
                for (int i = 0; i < result.Length; i += 4)
                {
                    byte b = result[i];
                    byte g = result[i + 1];
                    byte r = result[i + 2];

                    if (lut != null)
                    {
                        b = lut[b];
                        g = lut[g];
                        r = lut[r];
                        result[i] = b;
                        result[i + 1] = g;
                        result[i + 2] = r;
                    }
                    
                    int intensity = (r + g + b) / 3;
                    hist[Math.Clamp(intensity, 0, 255)]++;
                }

                var resultBmp = new WriteableBitmap(
                    new PixelSize(targetW, targetH),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Opaque);

                using (var fb = resultBmp.Lock())
                {
                    System.Runtime.InteropServices.Marshal.Copy(result, 0, fb.Address, result.Length);
                }

                return new ProcessResult { Image = resultBmp, Histogram = hist };
            }
            catch(Exception)
            {
                return new ProcessResult();
            }
        });
    }

    private static bool IsInMask(int x, int y, int w, int h, MaskShape shape)
    {
        int cx = w / 2;
        int cy = h / 2;
        int size = Math.Min(w, h) / 4; 
        
        switch (shape)
        {
            case MaskShape.Circle:
                return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= size * size;
            case MaskShape.Square:
                return Math.Abs(x - cx) <= size && Math.Abs(y - cy) <= size;
            case MaskShape.Rectangle:
                return Math.Abs(x - cx) <= size * 1.5 && Math.Abs(y - cy) <= size;
            default:
                return true;
        }
    }

    private static byte[] GetScaledPixels(WriteableBitmap bmp, int targetW, int targetH)
    {
        int w = bmp.PixelSize.Width;
        int h = bmp.PixelSize.Height;
        
        byte[] src = new byte[w * h * 4];
        using (var fb = bmp.Lock())
        {
            for (int y = 0; y < h; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    fb.Address + y * fb.RowBytes, 
                    src, 
                    y * w * 4, 
                    w * 4);
            }
        }

        if (w == targetW && h == targetH)
            return src;

        byte[] dst = new byte[targetW * targetH * 4];
        for (int y = 0; y < targetH; y++)
        {
            int srcY = y * h / targetH;
            for (int x = 0; x < targetW; x++)
            {
                int srcX = x * w / targetW;
                
                int srcIndex = (srcY * w + srcX) * 4;
                int dstIndex = (y * targetW + x) * 4;
                
                dst[dstIndex] = src[srcIndex];
                dst[dstIndex + 1] = src[srcIndex + 1];
                dst[dstIndex + 2] = src[srcIndex + 2];
                dst[dstIndex + 3] = src[srcIndex + 3];
            }
        }
        return dst;
    }
}
