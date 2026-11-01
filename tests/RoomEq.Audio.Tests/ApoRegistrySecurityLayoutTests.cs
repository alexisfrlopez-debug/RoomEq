using System.Runtime.InteropServices;
using RoomEq.Audio.SystemApo;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Verrou de marshaling des structures interop de la prise de possession
/// registre (correctif v3.9.3). RÉGRESSION v3.9.2 : le LUID déclaré
/// <c>long</c> C# (alignement 8 octets) insérait 4 octets de padding après
/// <c>PrivilegeCount</c> — TOKEN_PRIVILEGES marshalait en 24 octets au lieu
/// de 16, le noyau lisait un LUID falsifié (LowPart = le padding, HighPart =
/// le bas du LUID réel) et Attributes = 0, donc <c>AdjustTokenPrivileges</c>
/// « réussissait » avec ERROR_NOT_ALL_ASSIGNED : <c>EnablePrivilege</c>
/// concluait « non élevé » à tort et la prise de possession des clés MMDevices
/// n'a JAMAIS été tentée — l'activation échouait en « clé protégée » malgré
/// le portage v3.9.2 du mécanisme Equalizer APO (« toujours le problème de
/// clé protégée », retour terrain). Ces tests sont du marshaling pur : ils
/// tournent sous Linux, là où le module est sinon inerte.
/// </summary>
public class ApoRegistrySecurityLayoutTests
{
    [Fact]
    public void TokenPrivileges_Fait16Octets_CommeLeNatifWinnt()
    {
        // TOKEN_PRIVILEGES natif = DWORD + LUID_AND_ATTRIBUTES = 4 + 12 = 16.
        // v3.9.2 cassée : 24 (padding du long aligné sur 8).
        Assert.Equal(16, Marshal.SizeOf<ApoTokenPrivileges>());
    }

    [Fact]
    public void TokenPrivileges_LuidALOffset4_EtAttributesALOffset12()
    {
        var tp = new ApoTokenPrivileges
        {
            PrivilegeCount = 1,
            Luid = new ApoLuid { LowPart = 0x12345678, HighPart = unchecked((int)0x9ABCDEF0) },
            Attributes = 0x2, // SE_PRIVILEGE_ENABLED
        };
        byte[] b = new byte[Marshal.SizeOf<ApoTokenPrivileges>()];
        IntPtr p = Marshal.AllocHGlobal(b.Length);
        try
        {
            Marshal.StructureToPtr(tp, p, false);
            Marshal.Copy(p, b, 0, b.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }

        // offset 0 : PrivilegeCount
        Assert.Equal(1, BitConverter.ToInt32(b, 0));
        // offset 4 : LUID.LowPart — LE point de régression (le padding v3.9.2
        // siégeait ici : le noyau lisait 0 et prenait l'octet bas du LUID pour
        // son HighPart)
        Assert.Equal(0x12345678u, BitConverter.ToUInt32(b, 4));
        // offset 8 : LUID.HighPart
        Assert.Equal(unchecked((int)0x9ABCDEF0), BitConverter.ToInt32(b, 8));
        // offset 12 : Attributes — en v3.9.2 l'octet HAUT du long falsifiait
        // aussi ce champ (0 au lieu de SE_PRIVILEGE_ENABLED)
        Assert.Equal(0x2, BitConverter.ToInt32(b, 12));
    }

    [Fact]
    public void Luid_Fait8Octets_DeuxDWORDAlignement4()
    {
        Assert.Equal(8, Marshal.SizeOf<ApoLuid>());
    }

    [Fact]
    public void TokenPrivileges_LuidReelDePrivilege_ResteIntactAuMarshaling()
    {
        // SeTakeOwnershipPrivilege = LUID 9 sur toute machine Windows : le cas
        // exact du terrain — LowPart = 9, HighPart = 0.
        var tp = new ApoTokenPrivileges
        {
            PrivilegeCount = 1,
            Luid = new ApoLuid { LowPart = 9, HighPart = 0 },
            Attributes = 0x2,
        };
        byte[] b = new byte[16];
        IntPtr p = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.StructureToPtr(tp, p, false);
            Marshal.Copy(p, b, 0, 16);
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }
        // octets 4-7 = 09 00 00 00 : c'est CE que le noyau doit lire comme
        // LowPart. En v3.9.2 il lisait 00 00 00 00 (padding) puis 09 dans le
        // HighPart : LUID inexistant → ERROR_NOT_ALL_ASSIGNED.
        Assert.Equal(9u, BitConverter.ToUInt32(b, 4));
        Assert.Equal(0, BitConverter.ToInt32(b, 8));
        Assert.Equal(2, BitConverter.ToInt32(b, 12));
    }
}
