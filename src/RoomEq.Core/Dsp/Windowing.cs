namespace RoomEq.Core.Dsp;

public enum WindowType { Hann, BlackmanHarris, Rectangular }

public static class Windowing
{
    public static double[] Create(WindowType type, int n)
    {
        var w = new double[n];
        switch (type)
        {
            case WindowType.Rectangular:
                Array.Fill(w, 1.0);
                break;

            case WindowType.Hann:
                for (int i = 0; i < n; i++)
                    w[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (n - 1)));
                break;

            case WindowType.BlackmanHarris:
                for (int i = 0; i < n; i++)
                {
                    double t = 2.0 * Math.PI * i / (n - 1);
                    w[i] = 0.35875 - 0.48829 * Math.Cos(t)
                         + 0.14128 * Math.Cos(2 * t) - 0.01168 * Math.Cos(3 * t);
                }
                break;
        }
        return w;
    }

    public static double Sum(double[] w)
    {
        double s = 0; foreach (double v in w) s += v; return s;
    }
}
