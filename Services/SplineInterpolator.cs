using System;
using System.Linq;

namespace AvaloniaApp.Services;

public class SplineInterpolator
{
    public static byte[] CreateLut(double[] x, double[] y)
    {
        byte[] lut = new byte[256];
        int n = x.Length;

        if (n == 0)
        {
            for (int i = 0; i < 256; i++) lut[i] = (byte)i;
            return lut;
        }
        if (n == 1)
        {
            byte val = (byte)Math.Clamp(Math.Round(y[0]), 0, 255);
            for (int i = 0; i < 256; i++) lut[i] = val;
            return lut;
        }
        if (n == 2)
        {
            for (int i = 0; i < 256; i++)
            {
                // Linear
                double t = (i - x[0]) / (x[1] - x[0]);
                double val = y[0] + t * (y[1] - y[0]);
                lut[i] = (byte)Math.Clamp(Math.Round(val), 0, 255);
            }
            return lut;
        }

        // Natural Cubic Spline
        double[] a = new double[n];
        double[] b = new double[n - 1];
        double[] c = new double[n + 1];
        double[] d = new double[n - 1];

        for (int i = 0; i < n; i++) a[i] = y[i];

        double[] h = new double[n - 1];
        for (int i = 0; i < n - 1; i++) h[i] = x[i + 1] - x[i];

        double[] alpha = new double[n - 1];
        for (int i = 1; i < n - 1; i++)
            alpha[i] = 3.0 / h[i] * (a[i + 1] - a[i]) - 3.0 / h[i - 1] * (a[i] - a[i - 1]);

        double[] l = new double[n + 1];
        double[] mu = new double[n + 1];
        double[] z = new double[n + 1];

        l[0] = 1.0;
        mu[0] = 0.0;
        z[0] = 0.0;

        for (int i = 1; i < n - 1; i++)
        {
            l[i] = 2.0 * (x[i + 1] - x[i - 1]) - h[i - 1] * mu[i - 1];
            mu[i] = h[i] / l[i];
            z[i] = (alpha[i] - h[i - 1] * z[i - 1]) / l[i];
        }

        l[n - 1] = 1.0;
        z[n - 1] = 0.0;
        c[n - 1] = 0.0;

        for (int j = n - 2; j >= 0; j--)
        {
            c[j] = z[j] - mu[j] * c[j + 1];
            b[j] = (a[j + 1] - a[j]) / h[j] - h[j] * (c[j + 1] + 2.0 * c[j]) / 3.0;
            d[j] = (c[j + 1] - c[j]) / (3.0 * h[j]);
        }

        for (int i = 0; i < 256; i++)
        {
            double val;
            if (i <= x[0])
            {
                val = y[0];
            }
            else if (i >= x[n - 1])
            {
                val = y[n - 1];
            }
            else
            {
                int k = 0;
                while (k < n - 1 && x[k + 1] < i)
                {
                    k++;
                }

                double dx = i - x[k];
                val = a[k] + b[k] * dx + c[k] * dx * dx + d[k] * dx * dx * dx;
            }

            lut[i] = (byte)Math.Clamp(Math.Round(val), 0, 255);
        }

        return lut;
    }
}
