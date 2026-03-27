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

public class ProcessResult
{
    public WriteableBitmap? Image { get; set; }
    public int[]? Histogram { get; set; }
}

public class ImageProcessor
{
    public static async Task<ProcessResult> ProcessLayersAsync(List<LayerViewModel> layers, byte[]? lut)
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
