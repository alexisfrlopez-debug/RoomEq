using RoomEq.Audio.SystemApo;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Politique de position FxProperties (v3.9.8) — logique pure : choix EFX/MFX
/// selon le périphérique, noms de valeurs par position, persistance du slot
/// dans la sauvegarde (compatibilité ≤ v3.9.7), normalisation « notre CLSID
/// compte comme absent ». Source terrain : Equalizer APO DeviceAPOInfo.cpp
/// (« bluetooth devices may be combined in Windows 11, EFX will not work
/// then » → la référence installe en SFX/MFX sur les périphériques combinés).
/// </summary>
public class ApoSlotPolicyTests
{
    [Theory]
    [InlineData(false, ApoFxSlot.Efx)]   // sortie classique : EFX (,7)
    [InlineData(true, ApoFxSlot.Mfx)]    // périphérique combiné (BT Win11) : MFX (,6)
    public void Choose_FollowsCombinedDevice(bool combined, ApoFxSlot expected)
        => Assert.Equal(expected, ApoSlotPolicy.Choose(combined));

    [Fact]
    public void ClsidValueName_MapsSlotToRegistryName()
    {
        // EFX = PKEY_FX_EndpointEffectClsid (,7) ; MFX = PKEY_FX_ModeEffectClsid (,6)
        Assert.Equal(ApoConstants.FxEndpointEffectClsid, ApoSlotPolicy.ClsidValueName(ApoFxSlot.Efx));
        Assert.Equal(ApoConstants.FxModeEffectClsid, ApoSlotPolicy.ClsidValueName(ApoFxSlot.Mfx));
        // les noms DIFFÈRENT bien (une faute de frappe dans les PID serait
        // silencieuse et fatale au chargement)
        Assert.NotEqual(ApoSlotPolicy.ClsidValueName(ApoFxSlot.Efx), ApoSlotPolicy.ClsidValueName(ApoFxSlot.Mfx));
    }

    [Fact]
    public void ModesValueName_MapsSlotToProcessingModesKey()
    {
        // EFX modes = {d3993a3f…},7 ; MFX modes = {d3993a3f…},6 (recette
        // Equalizer APO : chaque position rattache SES modes)
        Assert.Equal(ApoConstants.FxEfxProcessingModes, ApoSlotPolicy.ModesValueName(ApoFxSlot.Efx));
        Assert.Equal(ApoConstants.FxMfxProcessingModes, ApoSlotPolicy.ModesValueName(ApoFxSlot.Mfx));
        Assert.NotEqual(ApoSlotPolicy.ModesValueName(ApoFxSlot.Efx), ApoSlotPolicy.ModesValueName(ApoFxSlot.Mfx));
    }

    [Theory]
    [InlineData(ApoFxSlot.Efx, "EFX")]
    [InlineData(ApoFxSlot.Mfx, "MFX")]
    public void SlotName_RoundTripsThroughParseSlot(ApoFxSlot slot, string persisted)
    {
        Assert.Equal(persisted, ApoSlotPolicy.SlotName(slot));
        Assert.Equal(slot, ApoSlotPolicy.ParseSlot(ApoSlotPolicy.SlotName(slot)));
    }

    [Theory]
    [InlineData(null)]        // sauvegarde v3.7–v3.9.7 : pas de champ
    [InlineData("")]          // vide toléré
    [InlineData("EFX")]
    [InlineData("efx")]       // insensible à la casse
    [InlineData("XYZ")]       // valeur inconnue : EFX (jamais d'échec de restauration)
    [InlineData("SFX")]       // position jamais utilisée par nous : EFX
    public void ParseSlot_UnknownOrNull_IsEfx(string? persisted)
        => Assert.Equal(ApoFxSlot.Efx, ApoSlotPolicy.ParseSlot(persisted));

    [Theory]
    [InlineData("mfx")]
    [InlineData("MFX")]
    public void ParseSlot_Mfx_IsMfx(string persisted)
        => Assert.Equal(ApoFxSlot.Mfx, ApoSlotPolicy.ParseSlot(persisted));

    [Fact]
    public void NotOurs_OurClsidCountsAsAbsent()
    {
        // une activation précédente qui a perdu sa sauvegarde a pu laisser
        // notre CLSID : le restituer comme « origine » serait faux
        // (comportement Equalizer APO sur ses propres GUID)
        Assert.Null(ApoSlotPolicy.NotOurs(ApoConstants.Clsid));
        Assert.Null(ApoSlotPolicy.NotOurs(ApoConstants.Clsid.ToLowerInvariant()));
        Assert.Equal("{realtek-efx}", ApoSlotPolicy.NotOurs("{realtek-efx}"));
        Assert.Null(ApoSlotPolicy.NotOurs(null));
    }

    [Fact]
    public void CombinedDeviceProperty_IsTheEqualizerApoKey()
    {
        // verrou littéral : {b3f8fa53-0004-438e-9003-51a46e139bfc},41
        // (DeviceAPOInfo.cpp combinedDeviceValueName — une dérive casserait
        // la détection des Bluetooth Windows 11 silencieusement)
        Assert.Equal("{b3f8fa53-0004-438e-9003-51a46e139bfc},41",
            ApoConstants.CombinedDeviceProperty);
    }

    [Fact]
    public void RegistryNames_AreTheDocumentedPids()
    {
        // verrous littéraux des PID FxProperties (SFX ,5 / MFX ,6 / EFX ,7 /
        // multi ,13 ,14 ,15 — mmdeviceapi.h et ticket Equalizer APO #77)
        Assert.Equal("{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5", ApoConstants.FxStreamEffectClsid);
        Assert.Equal("{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6", ApoConstants.FxModeEffectClsid);
        Assert.Equal("{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7", ApoConstants.FxEndpointEffectClsid);
        Assert.Equal("{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},13", ApoConstants.FxMultiStreamEffectClsid);
        Assert.Equal("{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},14", ApoConstants.FxMultiModeEffectClsid);
        Assert.Equal("{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},15", ApoConstants.FxMultiEndpointEffectClsid);
        Assert.Equal("{d3993a3f-99c2-4402-b5ec-a92a0367664b},6", ApoConstants.FxMfxProcessingModes);
        Assert.Equal("{d3993a3f-99c2-4402-b5ec-a92a0367664b},7", ApoConstants.FxEfxProcessingModes);
    }
}
