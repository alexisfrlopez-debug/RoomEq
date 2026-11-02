using System.Reflection;
using System.Runtime.InteropServices;
using RoomEq.Audio.SystemApo;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Verrous de l'auto-test COM de la DLL APO (v3.9.8) — réflexion pure,
/// exécutable sous Linux là où <see cref="ApoDllSelfTest"/> est inerte
/// (même philosophie que ApoRegistrySecurityPinvokeTests, v3.9.4). Chaque
/// import natif doit venir de SA DLL et porter SES drapeaux : une dérive
/// rendrait l'auto-test muet ou faux au moment précis où il sert — sur la
/// machine de l'utilisateur, au premier échec de chargement.
/// </summary>
public class ApoDllSelfTestLayoutTests
{
    private static List<MethodInfo> ImportsNatif()
        => typeof(ApoDllSelfTest).GetMethods(
                 BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.GetCustomAttribute<DllImportAttribute>() != null)
            .ToList();

    [Fact]
    public void LesCinqImportsNatifs_SontPresents()
    {
        var names = ImportsNatif().Select(m => m.Name).ToList();
        Assert.Contains("LoadLibraryExW", names);
        Assert.Contains("GetProcAddress", names);
        Assert.Contains("CoCreateInstance", names);
        Assert.Contains("CoTaskMemFree", names);
        Assert.Contains("CoInitialize", names);
        Assert.Equal(5, names.Count); // pas d'import fantôme
    }

    [Fact]
    public void LoadLibraryExW_VientDeKernel32_AvecSetLastErrorEtUnicode()
    {
        var m = Import("LoadLibraryExW");
        var attr = m.GetCustomAttribute<DllImportAttribute>()!;
        Assert.Equal("kernel32.dll", attr.Value);
        Assert.True(attr.SetLastError, "sans SetLastError, le code Win32 exact du refus de chargement (5/126/193…) est PERDU");
        Assert.True(attr.CharSet == CharSet.Unicode, "le chemin %ProgramData% peut contenir des accents");
        Assert.True(attr.ExactSpelling, "pas de résolution LoadLibraryExA");
        // paramètre unique = chemin de la DLL, retour = HMODULE (IntPtr.Zero = échec)
        Assert.Equal(typeof(string), m.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(IntPtr), m.ReturnType);
    }

    [Fact]
    public void GetProcAddress_VientDeKernel32_AvecSetLastError()
    {
        var m = Import("GetProcAddress");
        var attr = m.GetCustomAttribute<DllImportAttribute>()!;
        Assert.Equal("kernel32.dll", attr.Value);
        Assert.True(attr.SetLastError);
        Assert.Equal(typeof(IntPtr), m.ReturnType);
    }

    [Fact]
    public void CoCreateInstance_VientDeOle32_AvecPreserveSig()
    {
        // PreserveSig est LA condition du diagnostic : sans lui, l'échec
        // deviendrait une COMException et le rapport perdrait le code exact
        // (REGDB_E_CLASSNOTREG vs E_ACCESSDENIED vs mauvaise architecture…)
        var m = Import("CoCreateInstance");
        var attr = m.GetCustomAttribute<DllImportAttribute>()!;
        Assert.Equal("ole32.dll", attr.Value);
        Assert.True(attr.PreserveSig, "l'HRESULT doit revenir TEL QUEL pour être diagnostiqué");
        Assert.Equal(typeof(int), m.ReturnType);
    }

    [Fact]
    public void CoTaskMemFree_VientDeOle32_SansRetour()
    {
        var m = Import("CoTaskMemFree");
        var attr = m.GetCustomAttribute<DllImportAttribute>()!;
        Assert.Equal("ole32.dll", attr.Value);
        Assert.Equal(typeof(void), m.ReturnType);
    }

    [Fact]
    public void LesGuidsDesInterfaces_SontCeuxDuSdk()
    {
        // miroir de natif/roomeq-apo/apo/apo_abi.h — une IID fausse = le
        // moteur refuse l'APO (les 4 sont vérifiées aussi par le harnais
        // natif ; ce verrou protège le côté C# de l'auto-test)
        Assert.Equal("fd7f2b29-24d0-4b5c-b177-592c39f9ca10",
            typeof(IApoNative).GUID.ToString());
        Assert.Equal("9e1d6a6d-ddbc-4e95-a4c7-ad64ba37846c",
            typeof(IApoRtNative).GUID.ToString());
        Assert.Equal("0e5ed805-aba6-49c3-8f9a-2b8c889c4fa8",
            typeof(IApoConfigNative).GUID.ToString());
    }

    [Fact]
    public void LesInterfacesExigentIUnknown_PasDual()
    {
        // InterfaceIsIUnknown = vtable brute dans l'ordre déclaré —
        // InterfaceIsIDispatch ou Dual décalerait TOUTE la vtable et
        // l'auto-test appellerait n'importe quoi (crash immédiat)
        foreach (var t in new[] { typeof(IApoNative), typeof(IApoRtNative), typeof(IApoConfigNative) })
        {
            var attr = t.GetCustomAttribute<InterfaceTypeAttribute>()!;
            Assert.True(attr.Value == ComInterfaceType.InterfaceIsIUnknown,
                t.Name + " doit être InterfaceIsIUnknown (vtable brute)");
        }
    }

    [Fact]
    public void LesMethodesAPO_SontToutesEnPreserveSig()
    {
        // mêmes raisons que CoCreateInstance : le code exact de Reset,
        // Initialize, LockForProcess… doit être capturé, jamais transformé
        // en exception qui masquerait l'étape en échec
        foreach (var t in new[] { typeof(IApoNative), typeof(IApoConfigNative) })
            foreach (var m in t.GetMethods())
            {
                Assert.True(m.GetCustomAttribute<PreserveSigAttribute>() != null,
                    t.Name + "." + m.Name + " doit être [PreserveSig]");
            }
    }

    [Fact]
    public void LesStructuresRespectentLaTailleCPlusPlus()
    {
        // Layout C++ natif : sur un StructLayout, le compilateur encode des
        // DRAPEAUX de métadonnées (pas un attribut réflexible) — le verrou
        // fort est la TAILLE marshalée, miroir exact de l'ABI apo_abi.h :
        //   APO_CONNECTION_DESCRIPTOR : Type(4+pad) pBuffer(8) frames(4)
        //     format(8) signature(4+pad) = 40 octets (x64)
        //   APO_CONNECTION_PROPERTY : pBuffer(8) frames(4) flag(4) sig(4+pad)
        //     = 24 octets (x64)
        //   UNCOMPRESSEDAUDIOFORMAT : GUID(16) 5×(4) = 36 octets (Pack=4)
        Assert.True(typeof(ApoUncompressedFormat).IsLayoutSequential);
        Assert.True(typeof(ApoConnectionDescriptor).IsLayoutSequential);
        Assert.True(typeof(ApoConnectionProperty).IsLayoutSequential);
        Assert.Equal(36, Marshal.SizeOf<ApoUncompressedFormat>());
        Assert.Equal(40, Marshal.SizeOf<ApoConnectionDescriptor>());
        Assert.Equal(24, Marshal.SizeOf<ApoConnectionProperty>());
        // le format non compressé est packé à 4 (alignement des GUID du SDK)
        var layout = typeof(ApoUncompressedFormat).StructLayoutAttribute;
        Assert.NotNull(layout);
        Assert.Equal(4, layout!.Pack);
    }

    private static MethodInfo Import(string name)
        => ImportsNatif().Single(m => m.Name == name);
}
