using System;
using System.Numerics;
using System.Threading.Tasks;

namespace AvaloniaApp.Services;

public enum FourierFilterType
{
    None,
    LowPass,
    HighPass,
    BandStop,
    BandPass,
    NotchStop,
    NotchPass
}

public class FourierProcessor
{
    // 1D Cooley-Tukey FFT
    private static void FFT1D(Complex[] x, bool invert)
    {
        int n = x.Length;
        int shift = (int)Math.Log2(n);

        // Bit-reversal permutation
        for (int i = 0; i < n; i++)
        {
            int j = ReverseBits(i, shift);
            if (j > i)
            {
                var temp = x[i];
                x[i] = x[j];
                x[j] = temp;
            }
        }

        // Cooley-Tukey
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = 2 * Math.PI / len * (invert ? 1 : -1);
            Complex wlen = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (int i = 0; i < n; i += len)
            {
                Complex w = Complex.One;
                for (int j = 0; j < len / 2; j++)
                {
                    Complex u = x[i + j];
                    Complex v = x[i + j + len / 2] * w;
                    x[i + j] = u + v;
                    x[i + j + len / 2] = u - v;
                    w *= wlen;
                }
            }
        }

        if (invert)
        {
            for (int i = 0; i < n; i++)
                x[i] /= n;
        }
    }

    private static int ReverseBits(int n, int bits)
    {
        int res = 0;
        for (int i = 0; i < bits; i++)
        {
            res = (res << 1) | (n & 1);
            n >>= 1;
        }
        return res;
    }

    private static int NextPowerOf2(int n)
    {
        int count = 0;
        if (n > 0 && (n & (n - 1)) == 0) return n;
        while (n != 0)
        {
            n >>= 1;
            count += 1;
        }
        return 1 << count;
    }

    public static Complex[,] FFT2D(double[,] input, int width, int height, out int padWidth, out int padHeight)
    {
        padWidth = NextPowerOf2(width);
        padHeight = NextPowerOf2(height);
        
        int wLocal = padWidth;
        int hLocal = padHeight;

        Complex[,] data = new Complex[hLocal, wLocal];

        // Copy and center
        for (int y = 0; y < hLocal; y++)
        {
            for (int x = 0; x < wLocal; x++)
            {
                double val = (y < height && x < width) ? input[y, x] : 0;
                double centered = val * (((x + y) % 2 == 0) ? 1 : -1);
                data[y, x] = new Complex(centered, 0);
            }
        }

        // FFT Rows
        Parallel.For(0, hLocal, y =>
        {
            Complex[] row = new Complex[wLocal];
            for (int x = 0; x < wLocal; x++) row[x] = data[y, x];
            FFT1D(row, false);
            for (int x = 0; x < wLocal; x++) data[y, x] = row[x];
        });

        // FFT Columns
        Parallel.For(0, wLocal, x =>
        {
            Complex[] col = new Complex[hLocal];
            for (int y = 0; y < hLocal; y++) col[y] = data[y, x];
            FFT1D(col, false);
            for (int y = 0; y < hLocal; y++) data[y, x] = col[y];
        });

        return data;
    }

    public static double[,] IFFT2D(Complex[,] data, int width, int height)
    {
        int padHeight = data.GetLength(0);
        int padWidth = data.GetLength(1);

        // IFFT Columns
        Parallel.For(0, padWidth, x =>
        {
            Complex[] col = new Complex[padHeight];
            for (int y = 0; y < padHeight; y++) col[y] = data[y, x];
            FFT1D(col, true);
            for (int y = 0; y < padHeight; y++) data[y, x] = col[y];
        });

        // IFFT Rows
        Parallel.For(0, padHeight, y =>
        {
            Complex[] row = new Complex[padWidth];
            for (int x = 0; x < padWidth; x++) row[x] = data[y, x];
            FFT1D(row, true);
            for (int x = 0; x < padWidth; x++) data[y, x] = row[x];
        });

        double[,] output = new double[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double val = data[y, x].Real * (((x + y) % 2 == 0) ? 1 : -1);
                output[y, x] = val;
            }
        }

        return output;
    }

    public static void ApplyFilter(Complex[,] freqData, FourierFilterType type, double r1, double r2, int cx, int cy)
    {
        if (type == FourierFilterType.None) return;

        int h = freqData.GetLength(0);
        int w = freqData.GetLength(1);
        int centerX = w / 2;
        int centerY = h / 2;

        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                double dVector = Math.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
                
                // For notch filters
                double d1 = Math.Sqrt((x - centerX - cx) * (x - centerX - cx) + (y - centerY - cy) * (y - centerY - cy));
                double d2 = Math.Sqrt((x - centerX + cx) * (x - centerX + cx) + (y - centerY + cy) * (y - centerY + cy));

                bool keep = true;

                switch (type)
                {
                    case FourierFilterType.LowPass:
                        if (dVector > r1) keep = false;
                        break;
                    case FourierFilterType.HighPass:
                        if (dVector < r1) keep = false;
                        break;
                    case FourierFilterType.BandStop:
                        if (dVector >= r1 && dVector <= r2) keep = false;
                        break;
                    case FourierFilterType.BandPass:
                        if (dVector < r1 || dVector > r2) keep = false;
                        break;
                    case FourierFilterType.NotchStop:
                        if (d1 <= r1 || d2 <= r1) keep = false;
                        break;
                    case FourierFilterType.NotchPass:
                        if (d1 > r1 && d2 > r1) keep = false;
                        break;
                }

                if (!keep)
                {
                    freqData[y, x] = Complex.Zero;
                }
            }
        });
    }

    public static byte[] GetSpectrumImage(Complex[,] freqData, FourierFilterType type, double r1, double r2, int cx, int cy, bool asMask = false)
    {
        int h = freqData.GetLength(0);
        int w = freqData.GetLength(1);
        byte[] pixels = new byte[w * h * 4];

        if (asMask)
        {
            int centerX = w / 2;
            int centerY = h / 2;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double dVector = Math.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
                    double d1 = Math.Sqrt((x - centerX - cx) * (x - centerX - cx) + (y - centerY - cy) * (y - centerY - cy));
                    double d2 = Math.Sqrt((x - centerX + cx) * (x - centerX + cx) + (y - centerY + cy) * (y - centerY + cy));

                    bool keep = true;
                    switch (type)
                    {
                        case FourierFilterType.LowPass: keep = (dVector <= r1); break;
                        case FourierFilterType.HighPass: keep = (dVector >= r1); break;
                        case FourierFilterType.BandStop: keep = (dVector < r1 || dVector > r2); break;
                        case FourierFilterType.BandPass: keep = (dVector >= r1 && dVector <= r2); break;
                        case FourierFilterType.NotchStop: keep = (d1 > r1 && d2 > r1); break;
                        case FourierFilterType.NotchPass: keep = (d1 <= r1 || d2 <= r1); break;
                    }

                    byte val = keep ? (byte)255 : (byte)0;
                    int i = (y * w + x) * 4;
                    pixels[i] = val;
                    pixels[i + 1] = val;
                    pixels[i + 2] = val;
                    pixels[i + 3] = 255;
                }
            }
            return pixels;
        }

        double maxLog = 0;
        double[,] logMag = new double[h, w];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double mag = freqData[y, x].Magnitude;
                double log = Math.Log(1 + mag);
                logMag[y, x] = log;
                if (log > maxLog) maxLog = log;
            }
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte val = maxLog > 0 ? (byte)Math.Clamp((logMag[y, x] / maxLog) * 255.0, 0, 255) : (byte)0;
                int i = (y * w + x) * 4;
                pixels[i] = val;
                pixels[i + 1] = val;
                pixels[i + 2] = val;
                pixels[i + 3] = 255;
            }
        }

        return pixels;
    }
}
