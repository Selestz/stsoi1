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
public enum FilterMethod { None, Median, Gaussian, Linear }

public class ProcessResult
{
    public WriteableBitmap? Image { get; set; }
    public WriteableBitmap? SpectrumImage { get; set; }
    public WriteableBitmap? MaskImage { get; set; }
    public int[]? Histogram { get; set; }
}

public class ImageProcessor
{
    private static void ApplyBinarization(byte[] result, int w, int h, BinarizationMethod method, int a, double k, Action<double>? progress = null)
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
                if (y % 10 == 0) progress?.Invoke((double)y / h * 50.0);
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
                
                if (y % 10 == 0) progress?.Invoke(50.0 + (double)y / h * 50.0);
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
        double binK = 0.2,
        bool enableFilter = false,
        FilterMethod filterMethod = FilterMethod.None,
        int filterKw = 13,
        int filterKh = 13,
        double filterSigma = 3.0,
        bool enableFourier = false,
        FourierFilterType fourierType = FourierFilterType.None,
        double fourierR1 = 10,
        double fourierR2 = 50,
        int fourierCx = 20,
        int fourierCy = 20,
        IProgress<double>? progress = null)
    {
        if (layers == null || layers.Count == 0) return new ProcessResult();

        return await Task.Run(() => 
        {
            try
            {
                int totalStages = 1 /* blend */ + (enableFilter && filterMethod != FilterMethod.None ? 1 : 0) + (enableFourier && fourierType != FourierFilterType.None ? 3 : 0) + (enableBinarization && binMethod != BinarizationMethod.None ? 1 : 0) + 1 /* hist */;
                double currentStage = 0;
                double stageWeight = 100.0 / totalStages;
                Action<double> report = (percent) => progress?.Report(currentStage * stageWeight + percent * stageWeight / 100.0);

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
                
                currentStage++;
                report(0);

                if (enableFilter && filterMethod != FilterMethod.None)
                {
                    if (filterMethod == FilterMethod.Median)
                        ApplyMedianFilter(result, targetW, targetH, filterKw, filterKh, report);
                    else if (filterMethod == FilterMethod.Gaussian)
                    {
                        double[] kernelX = Generate1DGaussianKernel(filterKw, filterSigma);
                        double[] kernelY = Generate1DGaussianKernel(filterKh, filterSigma);
                        ApplySeparableFilter(result, targetW, targetH, kernelX, kernelY, report);
                    }
                    else if (filterMethod == FilterMethod.Linear)
                    {
                        double[] kernelX = new double[filterKw];
                        for (int i = 0; i < filterKw; i++) kernelX[i] = 1.0 / filterKw;
                        
                        double[] kernelY = new double[filterKh];
                        for (int i = 0; i < filterKh; i++) kernelY[i] = 1.0 / filterKh;
                        
                        ApplySeparableFilter(result, targetW, targetH, kernelX, kernelY, report);
                    }
                    currentStage++;
                    report(0);
                }

                byte[]? spectrumPixels = null;
                byte[]? maskPixels = null;
                int fourierW = targetW;
                int fourierH = targetH;

                if (enableFourier && fourierType != FourierFilterType.None)
                {
                    double[,] R = new double[targetH, targetW];
                    double[,] G = new double[targetH, targetW];
                    double[,] B = new double[targetH, targetW];
                    
                    for (int y = 0; y < targetH; y++)
                    {
                        for (int x = 0; x < targetW; x++)
                        {
                            int idx = (y * targetW + x) * 4;
                            B[y, x] = result[idx];
                            G[y, x] = result[idx + 1];
                            R[y, x] = result[idx + 2];
                        }
                    }

                    var freqR = FourierProcessor.FFT2D(R, targetW, targetH, out fourierW, out fourierH);
                    var freqG = FourierProcessor.FFT2D(G, targetW, targetH, out _, out _);
                    var freqB = FourierProcessor.FFT2D(B, targetW, targetH, out _, out _);

                    currentStage++;
                    report(0);
                    
                    spectrumPixels = FourierProcessor.GetSpectrumImage(freqG, fourierType, fourierR1, fourierR2, fourierCx, fourierCy, false);
                    maskPixels = FourierProcessor.GetSpectrumImage(freqG, fourierType, fourierR1, fourierR2, fourierCx, fourierCy, true);

                    FourierProcessor.ApplyFilter(freqR, fourierType, fourierR1, fourierR2, fourierCx, fourierCy);
                    FourierProcessor.ApplyFilter(freqG, fourierType, fourierR1, fourierR2, fourierCx, fourierCy);
                    FourierProcessor.ApplyFilter(freqB, fourierType, fourierR1, fourierR2, fourierCx, fourierCy);

                    currentStage++;
                    report(0);

                    double[,] outR = FourierProcessor.IFFT2D(freqR, targetW, targetH);
                    double[,] outG = FourierProcessor.IFFT2D(freqG, targetW, targetH);
                    double[,] outB = FourierProcessor.IFFT2D(freqB, targetW, targetH);

                    Parallel.For(0, targetH, y =>
                    {
                        for (int x = 0; x < targetW; x++)
                        {
                            int idx = (y * targetW + x) * 4;
                            result[idx]     = (byte)Math.Clamp(outB[y, x], 0, 255);
                            result[idx + 1] = (byte)Math.Clamp(outG[y, x], 0, 255);
                            result[idx + 2] = (byte)Math.Clamp(outR[y, x], 0, 255);
                            result[idx + 3] = 255;
                        }
                    });

                    currentStage++;
                    report(0);
                }

                if (enableBinarization && binMethod != BinarizationMethod.None)
                {
                    ApplyBinarization(result, targetW, targetH, binMethod, binWindowSize, binK, report);
                    currentStage++;
                    report(0);
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

                WriteableBitmap? spectrumBmp = null;
                WriteableBitmap? maskBmp = null;

                if (spectrumPixels != null && maskPixels != null && fourierW > 0 && fourierH > 0)
                {
                    spectrumBmp = new WriteableBitmap(new PixelSize(fourierW, fourierH), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                    using (var fb = spectrumBmp.Lock()) System.Runtime.InteropServices.Marshal.Copy(spectrumPixels, 0, fb.Address, spectrumPixels.Length);

                    maskBmp = new WriteableBitmap(new PixelSize(fourierW, fourierH), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                    using (var fb = maskBmp.Lock()) System.Runtime.InteropServices.Marshal.Copy(maskPixels, 0, fb.Address, maskPixels.Length);
                }

                return new ProcessResult { Image = resultBmp, SpectrumImage = spectrumBmp, MaskImage = maskBmp, Histogram = hist };
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

    private static int Mirror(int x, int max)
    {
        if (x < 0) return -x;
        if (x >= max) return 2 * max - 2 - x;
        return x;
    }

    private static void Swap(byte[] arr, int i, int j)
    {
        byte temp = arr[i];
        arr[i] = arr[j];
        arr[j] = temp;
    }

    private static int Partition(byte[] arr, int left, int right)
    {
        byte pivot = arr[left + (right - left) / 2];
        int i = left - 1;
        int j = right + 1;
        while (true)
        {
            do { i++; } while (arr[i] < pivot);
            do { j--; } while (arr[j] > pivot);
            if (i >= j) return j;
            Swap(arr, i, j);
        }
    }

    private static byte QuickSelect(byte[] arr, int left, int right, int k)
    {
        while (left < right)
        {
            int pivotIndex = Partition(arr, left, right);
            if (k <= pivotIndex)
                right = pivotIndex;
            else
                left = pivotIndex + 1;
        }
        return arr[k];
    }

    public static double[] Generate1DGaussianKernel(int k, double sigma)
    {
        double[] kernel = new double[k];
        int kHalf = k / 2;
        double sum = 0;
        
        for (int i = 0; i < k; i++)
        {
            int d = i - kHalf;
            double val = Math.Exp(-(d * d) / (2 * sigma * sigma));
            kernel[i] = val;
            sum += val;
        }
        
        for (int i = 0; i < k; i++)
        {
            kernel[i] /= sum;
        }
        
        return kernel;
    }

    public static void ApplySeparableFilter(byte[] pixels, int width, int height, double[] kernelX, double[] kernelY, Action<double>? progress = null)
    {
        int kw = kernelX.Length;
        int kh = kernelY.Length;
        
        byte[] temp = new byte[pixels.Length];
        
        int kwHalf = kw / 2;
        int khHalf = kh / 2;

        int rowsProcessed = 0;
        
        // Горизонтальный проход
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                double sumB = 0, sumG = 0, sumR = 0;
                
                for (int kx = 0; kx < kw; kx++)
                {
                    int px = Mirror(x + kx - kwHalf, width);
                    int pidx = (y * width + px) * 4;
                    double wVal = kernelX[kx];
                    
                    sumB += pixels[pidx] * wVal;
                    sumG += pixels[pidx + 1] * wVal;
                    sumR += pixels[pidx + 2] * wVal;
                }
                
                int index = (y * width + x) * 4;
                temp[index] = (byte)Math.Clamp(sumB, 0, 255);
                temp[index + 1] = (byte)Math.Clamp(sumG, 0, 255);
                temp[index + 2] = (byte)Math.Clamp(sumR, 0, 255);
                temp[index + 3] = pixels[index + 3];
            }
            
            int cur = System.Threading.Interlocked.Increment(ref rowsProcessed);
            if (cur % 5 == 0) progress?.Invoke((double)cur / (2 * height) * 100.0);
        });

        // Вертикальный проход
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                double sumB = 0, sumG = 0, sumR = 0;
                
                for (int ky = 0; ky < kh; ky++)
                {
                    int py = Mirror(y + ky - khHalf, height);
                    int pidx = (py * width + x) * 4;
                    double wVal = kernelY[ky];
                    
                    sumB += temp[pidx] * wVal;
                    sumG += temp[pidx + 1] * wVal;
                    sumR += temp[pidx + 2] * wVal;
                }
                
                int index = (y * width + x) * 4;
                pixels[index] = (byte)Math.Clamp(sumB, 0, 255);
                pixels[index + 1] = (byte)Math.Clamp(sumG, 0, 255);
                pixels[index + 2] = (byte)Math.Clamp(sumR, 0, 255);
                pixels[index + 3] = temp[index + 3];
            }
            
            int cur = System.Threading.Interlocked.Increment(ref rowsProcessed);
            if (cur % 5 == 0) progress?.Invoke((double)cur / (2 * height) * 100.0);
        });
    }

    public static double[,] GenerateGaussianKernel(int kw, int kh, double sigma)
    {
        double[,] kernel = new double[kh, kw];
        int kwHalf = kw / 2;
        int khHalf = kh / 2;
        double sum = 0;
        
        for (int y = 0; y < kh; y++)
        {
            for (int x = 0; x < kw; x++)
            {
                int dx = x - kwHalf;
                int dy = y - khHalf;
                double val = Math.Exp(-(dx * dx + dy * dy) / (2 * sigma * sigma)) / (2 * Math.PI * sigma * sigma);
                kernel[y, x] = val;
                sum += val;
            }
        }
        
        for (int y = 0; y < kh; y++)
        {
            for (int x = 0; x < kw; x++)
            {
                kernel[y, x] /= sum;
            }
        }
        
        return kernel;
    }

    public static void ApplyLinearFilter(byte[] pixels, int width, int height, double[,] kernel, Action<double>? progress = null)
    {
        int kw = kernel.GetLength(1);
        int kh = kernel.GetLength(0);
        int kwHalf = kw / 2;
        int khHalf = kh / 2;

        byte[] result = new byte[pixels.Length];
        
        int rowsProcessed = 0;
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                double sumB = 0, sumG = 0, sumR = 0;
                
                for (int ky = 0; ky < kh; ky++)
                {
                    for (int kx = 0; kx < kw; kx++)
                    {
                        int py = Mirror(y + ky - khHalf, height);
                        int px = Mirror(x + kx - kwHalf, width);
                        
                        int pidx = (py * width + px) * 4;
                        double wVal = kernel[ky, kx];
                        
                        sumB += pixels[pidx] * wVal;
                        sumG += pixels[pidx + 1] * wVal;
                        sumR += pixels[pidx + 2] * wVal;
                    }
                }
                
                int index = (y * width + x) * 4;
                result[index] = (byte)Math.Clamp(sumB, 0, 255);
                result[index + 1] = (byte)Math.Clamp(sumG, 0, 255);
                result[index + 2] = (byte)Math.Clamp(sumR, 0, 255);
                result[index + 3] = pixels[index + 3];
            }
            
            int cur = System.Threading.Interlocked.Increment(ref rowsProcessed);
            if (cur % 5 == 0) progress?.Invoke((double)cur / height * 100.0);
        });
        
        Array.Copy(result, pixels, pixels.Length);
    }

    public static void ApplyMedianFilter(byte[] pixels, int width, int height, int kw, int kh, Action<double>? progress = null)
    {
        int kwHalf = kw / 2;
        int khHalf = kh / 2;
        int windowSize = kw * kh;
        int kHalf = windowSize / 2;
        
        byte[] result = new byte[pixels.Length];
        
        int rowsProcessed = 0;
        Parallel.For(0, height, y =>
        {
            byte[] windowB = new byte[windowSize];
            byte[] windowG = new byte[windowSize];
            byte[] windowR = new byte[windowSize];
            
            for (int x = 0; x < width; x++)
            {
                int wIdx = 0;
                for (int ky = 0; ky < kh; ky++)
                {
                    for (int kx = 0; kx < kw; kx++)
                    {
                        int py = Mirror(y + ky - khHalf, height);
                        int px = Mirror(x + kx - kwHalf, width);
                        
                        int pidx = (py * width + px) * 4;
                        windowB[wIdx] = pixels[pidx];
                        windowG[wIdx] = pixels[pidx + 1];
                        windowR[wIdx] = pixels[pidx + 2];
                        wIdx++;
                    }
                }
                
                int index = (y * width + x) * 4;
                result[index] = QuickSelect((byte[])windowB.Clone(), 0, windowSize - 1, kHalf);
                result[index + 1] = QuickSelect((byte[])windowG.Clone(), 0, windowSize - 1, kHalf);
                result[index + 2] = QuickSelect((byte[])windowR.Clone(), 0, windowSize - 1, kHalf);
                result[index + 3] = pixels[index + 3];
            }
            
            int cur = System.Threading.Interlocked.Increment(ref rowsProcessed);
            if (cur % 5 == 0) progress?.Invoke((double)cur / height * 100.0);
        });
        
        Array.Copy(result, pixels, pixels.Length);
    }
}
