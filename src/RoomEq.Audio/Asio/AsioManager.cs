using NAudio.Wave;

namespace RoomEq.Audio.Asio;

/// <summary>
/// Helpers de gestion des pilotes ASIO : énumération, capacités, panneau de contrôle.
/// Un pilote ASIO est fourni par le fabricant de l'interface audio (ou ASIO4ALL) ;
/// RoomEq n'embarque pas le SDK Steinberg, il pilote les DLL ASIO via NAudio.
/// </summary>
public static class AsioManager
{
    private static readonly int[] CommonRates = { 44100, 48000, 88200, 96000, 176400, 192000 };

    /// <summary>Noms des pilotes ASIO installés (registre Windows).</summary>
    public static string[] DriverNames
    {
        get
        {
            try
            {
                return AsioOut.GetDriverNames();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }

    public static bool HasDrivers => DriverNames.Length > 0;

    public static int OutputChannels(string driver)
    {
        using var o = new AsioOut(driver);
        return o.DriverOutputChannelCount;
    }

    public static int InputChannels(string driver)
    {
        using var o = new AsioOut(driver);
        return o.DriverInputChannelCount;
    }

    /// <summary>Taux d'échantillonnage supportés par le pilote (sonde les taux usuels).</summary>
    public static int[] SupportedSampleRates(string driver)
    {
        var list = new List<int>();
        using var o = new AsioOut(driver);
        foreach (int r in CommonRates)
        {
            try
            {
                if (o.IsSampleRateSupported(r)) list.Add(r);
            }
            catch
            {
                // ignore
            }
        }
        return list.ToArray();
    }

    /// <summary>Taille de buffer du pilote (frames).</summary>
    public static int FramesPerBuffer(string driver)
    {
        using var o = new AsioOut(driver);
        return o.FramesPerBuffer;
    }

    /// <summary>Latence de lecture du pilote (frames).</summary>
    public static int PlaybackLatency(string driver)
    {
        using var o = new AsioOut(driver);
        return o.PlaybackLatency;
    }

    /// <summary>Ouvre le panneau de contrôle du pilote (taille de buffer, etc.).</summary>
    public static void ShowControlPanel(string driver)
    {
        using var o = new AsioOut(driver);
        o.ShowControlPanel();
    }
}
