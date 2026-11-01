using System.Reflection;
using System.Runtime.InteropServices;
using RoomEq.Audio.SystemApo;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Verrou de binding P/Invoke de la prise de possession registre (correctif
/// v3.9.4). RÉGRESSION v3.9.2→v3.9.3 : <c>CloseHandle</c> était importé
/// d'advapi32.dll — or ce point d'entrée n'existe QUE dans kernel32.dll.
/// <c>EnablePrivilege</c> activait BIEN SeTakeOwnershipPrivilege (le
/// correctif v3.9.3 du LUID fonctionnait, AdjustTokenPrivileges réussissait)
/// mais le <c>CloseHandle(token)</c> du finally levait
/// EntryPointNotFoundException, qui remplaçait le « return null » de succès :
/// EnablePrivilege répondait « impossible d'activer le privilège », le
/// takeover n'était jamais tenté et l'activation échouait en « clé protégée »
/// (retour terrain 3.9.3 : « le point d'entrée CloseHandle est introuvable
/// dans la DLL advapi32.dll »). Ces tests sont de la réflexion pure : ils
/// tournent sous Linux, là où le module est sinon inerte — même philosophie
/// que ApoRegistrySecurityLayoutTests (v3.9.3).
/// </summary>
public class ApoRegistrySecurityPinvokeTests
{
    /// <summary>
    /// Les 6 imports natifs avec leur DLL d'origine EXACTE (winnt.h /
    /// kernel32 : CloseHandle et GetCurrentProcess sont des handles d'OBJET
    /// — kernel32 ; le reste manipule le JETON et le registre — advapi32).
    /// </summary>
    private static readonly Dictionary<string, string> DllsNatales = new()
    {
        ["OpenProcessToken"] = "advapi32.dll",
        ["CloseHandle"] = "kernel32.dll", // ← LE verrou v3.9.4
        ["GetCurrentProcess"] = "kernel32.dll",
        ["LookupPrivilegeValue"] = "advapi32.dll",
        ["AdjustTokenPrivileges"] = "advapi32.dll",
        ["RegSetKeySecurity"] = "advapi32.dll",
    };

    private static List<(string Methode, string Dll)> ImportsNatif()
    {
        var imports = new List<(string, string)>();
        foreach (var m in typeof(ApoRegistrySecurity).GetMethods(
                     BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            var attr = m.GetCustomAttribute<DllImportAttribute>();
            if (attr != null)
                imports.Add((m.Name, attr.Value));
        }
        return imports;
    }

    [Fact]
    public void CloseHandle_EstImporteDeKernel32_JamaisAdvapi32()
    {
        // CloseHandle n'est PAS exporté par advapi32.dll : ce binding exact
        // (v3.9.2–v3.9.3) levait EntryPointNotFoundException au nettoyage du
        // jeton et faisait échouer TOUTE l'activation après un succès réel.
        var close = ImportsNatif().Single(i => i.Methode == "CloseHandle");
        Assert.Equal("kernel32.dll", close.Dll);
    }

    [Fact]
    public void ChaqueImportVientDeSaDllNatale_EtAucunNeManque()
    {
        var imports = ImportsNatif();
        // exactement les 6 imports attendus — ni un de plus (binding non
        // audité qui surprendrait à l'exécution Windows), ni un de moins
        // (maillon silencieusement retiré du takeover)
        Assert.Equal(DllsNatales.Count, imports.Count);
        foreach (var attendu in DllsNatales)
        {
            var trouve = imports.Single(i => i.Methode == attendu.Key);
            Assert.Equal(attendu.Value, trouve.Dll);
        }
    }
}
