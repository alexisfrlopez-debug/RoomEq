using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RoomEq.Audio.SystemApo;

// ============================================================ interfaces COM
// Miroir EXACT de natif/roomeq-apo/apo/apo_abi.h (IIDs et ordres de vtable
// vérifiés contre le SDK) — [PreserveSig] partout : les signatures déclarées
// SONT les signatures natives (HRESULT retourné tel quel, jamais traduit en
// exception, pour que le rapport nomme le code exact).

/// <summary>IAudioProcessingObject — IID {FD7F2B29-24D0-4b5c-B177-592C39F9CA10}.</summary>
[ComImport, Guid("FD7F2B29-24D0-4B5C-B177-592C39F9CA10"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApoNative
{
    [PreserveSig] int Reset();
    [PreserveSig] int GetLatency(out long latency);
    [PreserveSig] int GetRegistrationProperties(out IntPtr props);
    [PreserveSig] int Initialize(uint cbDataSize, IntPtr pbyData);
    [PreserveSig] int IsInputFormatSupported(IntPtr opposite, IntPtr requested, IntPtr supported);
    [PreserveSig] int IsOutputFormatSupported(IntPtr opposite, IntPtr requested, IntPtr supported);
    [PreserveSig] int GetInputChannelCount(out uint channels);
}

/// <summary>IAudioProcessingObjectRT — IID {9E1D6A6D-DDBC-4E95-A4C7-AD64BA37846C}.</summary>
[ComImport, Guid("9E1D6A6D-DDBC-4E95-A4C7-AD64BA37846C"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApoRtNative
{
    void APOProcess(uint numInputs, IntPtr inputProps, uint numOutputs, IntPtr outputProps);
    [PreserveSig] uint CalcInputFrames(uint outputFrames);
    [PreserveSig] uint CalcOutputFrames(uint inputFrames);
}

/// <summary>IAudioProcessingObjectConfiguration — IID {0E5ED805-ABA6-49c3-8F9A-2B8C889C4FA8}.</summary>
[ComImport, Guid("0E5ED805-ABA6-49C3-8F9A-2B8C889C4FA8"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApoConfigNative
{
    [PreserveSig] int LockForProcess(uint numInputs, IntPtr inputDescs, uint numOutputs, IntPtr outputDescs);
    [PreserveSig] int UnlockForProcess();
}

/// <summary>
/// IAudioMediaType — l'interface que le moteur passe à LockForProcess et que
/// NOTRE APO relit (GetUncompressedAudioFormat). L'IID officielle du SDK
/// n'est pas exposée publiquement ; notre cœur natif ne fait JAMAIS
/// QueryInterface sur l'objet format (il appelle la vtable reçue), donc ce
/// GUID ne sert qu'au wrapper .NET — tout GUID fonctionne pour l'auto-test.
/// </summary>
[ComImport, Guid("8A3BE61B-0F2E-4A61-9D3C-25E1B6A2C9F4"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApoMediaTypeNative
{
    [PreserveSig] int IsCompressedFormat(out bool compressed);
    [PreserveSig] int IsEqual(IntPtr other, out uint flags);
    [PreserveSig] IntPtr GetAudioFormat();
    [PreserveSig] int GetUncompressedAudioFormat(ref ApoUncompressedFormat format);
}

/// <summary>UNCOMPRESSEDAUDIOFORMAT (audiomediatype.h) — float32 entrelacé.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct ApoUncompressedFormat
{
    public Guid FormatType;              // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
    public uint SamplesPerFrame;         // canaux
    public uint BytesPerSampleContainer;
    public uint ValidBitsPerSample;
    public float FramesPerSecond;
    public uint ChannelMask;
}

/// <summary>APO_CONNECTION_DESCRIPTOR (audioenginebaseapo.h) — LockForProcess.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ApoConnectionDescriptor
{
    public int Type;                     // APO_CONNECTION_BUFFER_TYPE_ALLOCATED
    public IntPtr Buffer;
    public uint MaxFrameCount;
    public IntPtr Format;                // IAudioMediaType* (CCW)
    public uint Signature;
}

/// <summary>APO_CONNECTION_PROPERTY (AudioAPOTypes.h) — APOProcess.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ApoConnectionProperty
{
    public IntPtr Buffer;
    public uint ValidFrameCount;
    public uint BufferFlag;              // BUFFER_VALID = 1
    public uint Signature;
}

/// <summary>
/// Format de test : 48 kHz, 2 voies, float32 — le format de mixage standard
/// du moteur audio Windows (celui qu'audiodg feed à un EFX/MFX).
/// </summary>
internal sealed class SyntheticMediaType : IApoMediaTypeNative
{
    private readonly ApoUncompressedFormat _format;

    public SyntheticMediaType()
    {
        _format = new ApoUncompressedFormat
        {
            FormatType = new Guid("00000003-0000-0010-8000-00AA00389B71"), // IEEE_FLOAT
            SamplesPerFrame = 2,
            BytesPerSampleContainer = 4,
            ValidBitsPerSample = 32,
            FramesPerSecond = 48000f,
            ChannelMask = 0x3,
        };
    }

    public int IsCompressedFormat(out bool compressed)
    {
        compressed = false;
        return 0;
    }

    public int IsEqual(IntPtr other, out uint flags)
    {
        flags = 0;
        return 1; // S_FALSE : jamais appelé par notre APO
    }

    public IntPtr GetAudioFormat() => IntPtr.Zero;

    public int GetUncompressedAudioFormat(ref ApoUncompressedFormat format)
    {
        format = _format;
        return 0;
    }
}

/// <summary>
/// AUTO-TEST COM de la DLL APO (v3.9.8 — correctif « EQ activé = sortie
/// muette ») : rejoue DANS le processus d'installation élevé tout ce que le
/// moteur audio fait pour instancier l'APO — chargement de la DLL
/// (LoadLibrary, code Win32 exact), résolution de DllGetClassObject,
/// CoCreateInstance (via l'inscription COM réelle, comme audiodg),
/// QueryInterface pour les QUATRE interfaces exigées, lecture des
/// propriétés d'enregistrement, Initialize, LockForProcess au format de
/// mixage, APOProcess sur un signal témoin (le son doit traverser),
/// UnlockForProcess. Chaque étape passe ou échoue SANS être fatale au
/// processus ; le rapport nomme la première qui échoue et son code exact.
/// Windows uniquement (jamais appelé ailleurs, inerte par construction).
/// </summary>
[SupportedOSPlatform("windows")]
public static class ApoDllSelfTest
{
    private const uint ClsctxInprocServer = 0x1;
    private const uint BufferValid = 1;

    private static readonly Guid ClsidRoomEqApo = new("EAA24610-59C6-4591-A23E-9D0A2C4E1A15");
    private static readonly Guid IidIUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IidIAudioProcessingObject = new("FD7F2B29-24D0-4B5C-B177-592C39F9CA10");
    private static readonly Guid IidIAudioProcessingObjectRT = new("9E1D6A6D-DDBC-4E95-A4C7-AD64BA37846C");
    private static readonly Guid IidIAudioProcessingObjectConfiguration = new("0E5ED805-ABA6-49C3-8F9A-2B8C889C4FA8");
    private static readonly Guid IidIAudioSystemEffects = new("5FA00F27-ADD6-499A-8A9D-6B98521FA75B");

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoCreateInstance(
        ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoInitialize(IntPtr pvReserved);

    /// <summary>
    /// Exécute l'auto-test complet. Ne lève JAMAIS d'exception : tout échec
    /// devient une étape du rapport. Durée typique : quelques dizaines de ms.
    /// </summary>
    public static ApoSelfTestReport Run(string dllPath)
    {
        var steps = new List<ApoSelfTestStep>();
        var toRelease = new List<object>();
        IntPtr hModule = IntPtr.Zero, comUnknown = IntPtr.Zero, regProps = IntPtr.Zero;
        IntPtr descMem = IntPtr.Zero, descPtr = IntPtr.Zero;
        IntPtr inPropMem = IntPtr.Zero, outPropMem = IntPtr.Zero;
        IntPtr inPtr = IntPtr.Zero, outPtr = IntPtr.Zero, inBuf = IntPtr.Zero, outBuf = IntPtr.Zero;
        IntPtr formatCom = IntPtr.Zero;
        try
        {
            _ = CoInitialize(IntPtr.Zero); // S_OK/S_FALSE : déjà fait n'est pas un échec

            // ---- 1. Chargement de la DLL (dépendances + DllMain + ACL)
            hModule = LoadLibraryExW(dllPath, IntPtr.Zero, 0);
            if (hModule == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                steps.Add(ApoSelfTestStep.Fail("Chargement de la DLL",
                    ApoHresult.Hex(unchecked((int)0x80070000 | (err & 0xFFFF))) + " — " + DescribeWin32(err)));
                return Report(steps);
            }
            steps.Add(ApoSelfTestStep.Ok("Chargement de la DLL", "LoadLibrary OK"));

            // ---- 2. Point d'entrée COM
            IntPtr entry = GetProcAddress(hModule, "DllGetClassObject");
            if (entry == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                steps.Add(ApoSelfTestStep.Fail("Point d'entrée DllGetClassObject",
                    "GetProcAddress a échoué (Win32 " + err + ") — la DLL n'expose pas la fabrique COM"));
                return Report(steps);
            }
            steps.Add(ApoSelfTestStep.Ok("Point d'entrée DllGetClassObject", "résolu"));

            // ---- 3. CoCreateInstance : l'inscription COM réelle (comme audiodg)
            Guid clsid = ClsidRoomEqApo, iidUnknown = IidIUnknown;
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxInprocServer, ref iidUnknown, out comUnknown);
            if (hr != 0)
            {
                // différentiel : la fabrique DIRECTE marche-t-elle ? (alors
                // c'est l'INSCRIPTION COM qui est en cause, pas la DLL)
                string direct = TryFactoryDirect(hModule, entry);
                steps.Add(ApoSelfTestStep.Fail("CoCreateInstance (inscription COM)",
                    ApoHresult.DescribeFull(hr) + (direct.Length > 0 ? " ; en direct : " + direct : "")));
                return Report(steps);
            }
            steps.Add(ApoSelfTestStep.Ok("CoCreateInstance (inscription COM)", "objet créé"));

            object apo = Marshal.GetObjectForIUnknown(comUnknown);
            toRelease.Add(apo);

            // ---- 4. Les QUATRE interfaces exigées par le moteur audio
            foreach (var (name, iid) in new[]
                     {
                         ("IAudioProcessingObject", IidIAudioProcessingObject),
                         ("IAudioProcessingObjectRT", IidIAudioProcessingObjectRT),
                         ("IAudioProcessingObjectConfiguration", IidIAudioProcessingObjectConfiguration),
                         ("IAudioSystemEffects", IidIAudioSystemEffects),
                     })
            {
                Guid g = iid;
                int qhr = Marshal.QueryInterface(comUnknown, ref g, out _);
                if (qhr != 0)
                {
                    steps.Add(ApoSelfTestStep.Fail("Interface " + name, ApoHresult.DescribeFull(qhr)));
                    return Report(steps);
                }
                steps.Add(ApoSelfTestStep.Ok("Interface " + name, "obtenue"));
            }

            var apoCom = (IApoNative)apo;
            var apoConfig = (IApoConfigNative)apo;
            var apoRt = (IApoRtNative)apo;

            // ---- 5. Propriétés d'enregistrement (ce que le moteur lit pour
            //      valider l'APO : CLSID, drapeaux, connexions)
            hr = apoCom.GetRegistrationProperties(out regProps);
            if (hr != 0 || regProps == IntPtr.Zero)
            {
                steps.Add(ApoSelfTestStep.Fail("Propriétés d'enregistrement",
                    hr == 0 ? "tampon nul" : ApoHresult.DescribeFull(hr)));
                return Report(steps);
            }
            Guid declaredClsid = Marshal.PtrToStructure<Guid>(regProps);
            int declaredFlags = Marshal.ReadInt32(regProps, 16);
            if (declaredClsid != ClsidRoomEqApo)
            {
                steps.Add(ApoSelfTestStep.Fail("Propriétés d'enregistrement",
                    "CLSID déclaré " + declaredClsid + " ≠ " + ClsidRoomEqApo));
                return Report(steps);
            }
            steps.Add(ApoSelfTestStep.Ok("Propriétés d'enregistrement",
                "CLSID conforme, drapeaux " + declaredFlags));

            // ---- 6. Initialize (données opaques — notre APO accepte tout)
            hr = apoCom.Initialize(0, IntPtr.Zero);
            if (hr != 0)
            {
                steps.Add(ApoSelfTestStep.Fail("Initialize", ApoHresult.DescribeFull(hr)));
                return Report(steps);
            }
            steps.Add(ApoSelfTestStep.Ok("Initialize", "accepté"));

            // ---- 7. LockForProcess au format de mixage (48 kHz, 2 voies, float32)
            var mediaType = new SyntheticMediaType();
            formatCom = Marshal.GetComInterfaceForObject(mediaType, typeof(IApoMediaTypeNative));
            const int frames = 256;
            inBuf = Marshal.AllocHGlobal(frames * 2 * sizeof(float));
            outBuf = Marshal.AllocHGlobal(frames * 2 * sizeof(float));
            var desc = new ApoConnectionDescriptor
            {
                Type = 0, // APO_CONNECTION_BUFFER_TYPE_ALLOCATED
                Buffer = inBuf,
                MaxFrameCount = (uint)frames,
                Format = formatCom,
                Signature = 0,
            };
            descMem = Marshal.AllocHGlobal(Marshal.SizeOf<ApoConnectionDescriptor>());
            Marshal.StructureToPtr(desc, descMem, false);
            descPtr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(descPtr, descMem);

            hr = apoConfig.LockForProcess(1, descPtr, 1, descPtr);
            if (hr != 0)
            {
                steps.Add(ApoSelfTestStep.Fail("LockForProcess", ApoHresult.DescribeFull(hr)));
                return Report(steps);
            }
            steps.Add(ApoSelfTestStep.Ok("LockForProcess", "48000 Hz, 2 voies, float32 verrouillé"));

            // ---- 8. APOProcess : un signal témoin doit TRAVERSER (le défaut
            //      de v3.9.6-v3.9.7 : la DLL n'était jamais instanciée par le
            //      moteur — ici on prouve qu'une fois instanciée, elle laisse
            //      passer le son, courbe vide = transparence)
            for (int i = 0; i < frames * 2; i++)
                Marshal.WriteInt32(inBuf, i * sizeof(float), BitConverter.SingleToInt32Bits((i % 2 == 0) ? 0.25f : -0.25f));
            var inProp = new ApoConnectionProperty
            {
                Buffer = inBuf, ValidFrameCount = frames, BufferFlag = BufferValid, Signature = 0,
            };
            var outProp = new ApoConnectionProperty
            {
                Buffer = outBuf, ValidFrameCount = frames, BufferFlag = BufferValid, Signature = 0,
            };
            inPropMem = Marshal.AllocHGlobal(Marshal.SizeOf<ApoConnectionProperty>());
            outPropMem = Marshal.AllocHGlobal(Marshal.SizeOf<ApoConnectionProperty>());
            Marshal.StructureToPtr(inProp, inPropMem, false);
            Marshal.StructureToPtr(outProp, outPropMem, false);
            inPtr = Marshal.AllocHGlobal(IntPtr.Size);
            outPtr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(inPtr, inPropMem);
            Marshal.WriteIntPtr(outPtr, outPropMem);

            apoRt.APOProcess(1, inPtr, 1, outPtr);

            double energy = 0;
            bool finite = true;
            for (int i = 0; i < frames * 2; i++)
            {
                float v = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(outBuf, i * sizeof(float)));
                if (!float.IsFinite(v))
                    finite = false;
                energy += Math.Abs(v);
            }
            if (!finite || energy <= 0)
            {
                steps.Add(ApoSelfTestStep.Fail("APOProcess",
                    "le signal témoin ne traverse pas (énergie " + energy.ToString("0.###") +
                    ", valeurs finies : " + (finite ? "oui" : "non") + ") — l'APO couperait le son"));
                return Report(steps);
            }
            steps.Add(ApoSelfTestStep.Ok("APOProcess",
                "signal témoin traversé (énergie conservée : " + energy.ToString("0") + ")"));

            // ---- 9. UnlockForProcess (le moteur le fait à chaque re-configuration)
            hr = apoConfig.UnlockForProcess();
            if (hr != 0)
            {
                steps.Add(ApoSelfTestStep.Fail("UnlockForProcess", ApoHresult.DescribeFull(hr)));
                return Report(steps);
            }
            steps.Add(ApoSelfTestStep.Ok("UnlockForProcess", "déverrouillé"));
        }
        catch (Exception ex)
        {
            steps.Add(ApoSelfTestStep.Fail("Exception", ex.GetType().Name + " : " + ex.Message));
        }
        finally
        {
            foreach (var o in toRelease)
            {
                try { Marshal.ReleaseComObject(o); } catch { /* déjà parti */ }
            }
            if (regProps != IntPtr.Zero) CoTaskMemFree(regProps);
            if (formatCom != IntPtr.Zero) Marshal.Release(formatCom);
            foreach (var p in new[] { descPtr, descMem, inPtr, outPtr, inPropMem, outPropMem, inBuf, outBuf })
                if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            // Pas de FreeLibrary : DllCanUnloadNow répond S_FALSE (durée de vie
            // = celle du processus), et le processus d'installation exit juste
            // après — le nettoyage système suffit et évite tout déchargement
            // sous les pieds d'un RCW.
        }
        return Report(steps);
    }

    /// <summary>Assemble le rapport (réussite = AUCUNE étape en échec).</summary>
    private static ApoSelfTestReport Report(List<ApoSelfTestStep> steps)
        => new(steps.All(s => s.Passed), steps);

    /// <summary>
    /// Différentiel CoCreateInstance ↔ fabrique directe : si l'appel direct
    /// de DllGetClassObject réussit là où CoCreateInstance échoue, c'est
    /// l'INSCRIPTION COM qui est fausse (et non la DLL). Jamais d'exception.
    /// </summary>
    private static string TryFactoryDirect(IntPtr hModule, IntPtr entry)
    {
        try
        {
            var factory =
                Marshal.GetDelegateForFunctionPointer<DllGetClassObjectDelegate>(entry);
            Guid clsid = ClsidRoomEqApo, iidFactory = new("00000001-0000-0000-C000-000000000046");
            int hr = factory(ref clsid, ref iidFactory, out IntPtr factoryPtr);
            if (hr != 0)
                return "fabrique directe " + ApoHresult.DescribeFull(hr) + " (la DLL elle-même est en cause)";
            Marshal.Release(factoryPtr);
            return "fabrique directe OK (l'INSCRIPTION COM est en cause, pas la DLL)";
        }
        catch
        {
            return "";
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DllGetClassObjectDelegate(ref Guid rclsid, ref Guid riid, out IntPtr ppv);

    /// <summary>Texte des codes GetLastError classiques du chargement de DLL.</summary>
    private static string DescribeWin32(int err) => err switch
    {
        5 => "accès refusé (Win32 5) — le dossier ou la DLL refuse la lecture à cette identité",
        126 => "module introuvable (Win32 126) — la DLL ou une de ses dépendances est absente",
        127 => "point d'entrée introuvable (Win32 127)",
        193 => "mauvais format binaire (Win32 193) — l'architecture de la DLL ne correspond pas au processus",
        998 => "accès mémoire invalide (Win32 998)",
        1114 => "échec de l'initialisation de la DLL (Win32 1114) — DllMain a refusé le chargement",
        14001 => "configuration side-by-side incorrecte (Win32 14001) — runtime VC++ manquant",
        _ => "erreur Win32 " + err,
    };
}
