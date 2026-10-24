#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Patch AsioBridgeHost.cs : transport POSIX déterministe + pont périphérique macOS."""
import io

P = '/home/z/my-project/workspace/roomq-source/RoomEq/src/RoomEq.Audio/Asio/AsioBridgeHost.cs'
s = io.open(P, encoding='utf-8').read()
BS = chr(92)  # backslash

# ---- 1) transport : chemin POSIX déterministe + permissions ----
old_fallback = (
    "    private static string FallbackPath(string name)\n"
    "    {\n"
    '        // "Local' + BS + 'RoomEq.Asio.Bridge.v1" -> "roomeq.asio.bridge.v1.mmf"\n'
    "        char[] bad = Path.GetInvalidFileNameChars();\n"
    "        string clean = new string(name.Select(c => bad.Contains(c) ? '_' : char.ToLowerInvariant(c)).ToArray());\n"
    '        return Path.Combine(Path.GetTempPath(), clean + ".mmf");\n'
    "    }"
)
new_fallback = (
    "    /// <summary>\n"
    "    /// Chemin POSIX déterministe : « Local" + BS + "RoomEq.Asio.Bridge.v1 » →\n"
    '    /// « /tmp/roomeq.asio.bridge.v1.mmf ». La même règle est implémentée par le\n'
    "    /// greffon macOS (RoomEqHAL) — ne pas changer sans mettre à jour roomeq_hal.c.\n"
    "    /// </summary>\n"
    "    public static string FallbackPath(string name)\n"
    "    {\n"
    "        int lastSlash = name.LastIndexOf('" + BS + "');\n"
    "        string clean = (lastSlash >= 0 ? name[(lastSlash + 1)..] : name).ToLowerInvariant();\n"
    '        return Path.Combine("/tmp", clean + ".mmf");\n'
    "    }\n"
    "\n"
    "    /// <summary>macOS/Linux : rend le fichier accessible en écriture à coreaudiod (autre utilisateur).</summary>\n"
    "    private static void MakeWorldWritable(string path)\n"
    "    {\n"
    "        try\n"
    "        {\n"
    "            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())\n"
    "                System.IO.File.SetUnixFileMode(path,\n"
    "                    System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite |\n"
    "                    System.IO.UnixFileMode.GroupRead | System.IO.UnixFileMode.GroupWrite |\n"
    "                    System.IO.UnixFileMode.OtherRead | System.IO.UnixFileMode.OtherWrite);\n"
    "        }\n"
    "        catch\n"
    "        {\n"
    "            // système de fichiers exotique : le greffon peut recréer le fichier lui-même\n"
    "        }\n"
    "    }"
)
assert old_fallback in s, "fallback introuvable"
s = s.replace(old_fallback, new_fallback)

old_create = (
    "            string path = FallbackPath(name);\n"
    "            return MemoryMappedFile.CreateFromFile(path, System.IO.FileMode.OpenOrCreate, null, size);"
)
new_create = (
    "            string path = FallbackPath(name);\n"
    "            var mmf = MemoryMappedFile.CreateFromFile(path, System.IO.FileMode.OpenOrCreate, null, size);\n"
    "            MakeWorldWritable(path);\n"
    "            return mmf;"
)
assert old_create in s, "create introuvable"
s = s.replace(old_create, new_create)

# ---- 2) hôte : libellés paramétrables + fabrique du pont périphérique ----
old_fields = (
    "    private int _session;              // session courante vue côté hôte\n"
    "    private long _pulledTotal;         // échantillons tirés du pont (interne)\n"
    "    private long _consumedTotal;       // échantillons consommés par le mixeur\n"
)
new_fields = old_fields + (
    "\n"
    "    // libellés d'état (DAW côté ASIO Windows ; périphérique virtuel côté macOS)\n"
    '    private readonly string _idleText = "Pont ASIO à l\'arrêt.";\n'
    '    private readonly string _connectedText = "DAW connecté (48 kHz)";\n'
    '    private readonly string _waitingText = "En attente d\'un DAW (choisissez « RoomEq ASIO » dans son logiciel audio)";\n'
    "    private string? _idleTextOverride;\n"
    "    private string? _connectedTextOverride;\n"
    "    private string? _waitingTextOverride;\n"
)
assert old_fields in s, "champs introuvables"
s = s.replace(old_fields, new_fields, 1)

old_testctor = (
    "    /// <summary>Point d'entrée des tests : hôte isolé sur des noms dédiés.</summary>\n"
    "    internal static AsioBridgeHost ForTests(string mapName)\n"
    '        => new(mapName, mapName + ".Event");\n'
)
new_testctor = old_testctor + (
    "\n"
    "    /// <summary>\n"
    "    /// Hôte du pont du PÉRIPHÉRIQUE VIRTUEL macOS : le greffon système RoomEqHAL\n"
    "    /// (chargé par coreaudiod) y écrit ce que macOS joue dans « RoomEq Virtual ».\n"
    "    /// Même protocole, autre section — un DAW ASIO et le périphérique ne se\n"
    "    /// marchent pas dessus.\n"
    "    /// </summary>\n"
    "    public static AsioBridgeHost CreateDeviceBridge() => new(\n"
    "        AsioBridgeInterop.DeviceBridgeMapName, AsioBridgeInterop.DeviceBridgeEventName)\n"
    "    {\n"
    '        _idleTextOverride = "Pont du périphérique virtuel à l\'arrêt.",\n'
    '        _connectedTextOverride = "Périphérique virtuel actif (48 kHz)",\n'
    '        _waitingTextOverride = "En attente du périphérique virtuel — jouez du son sur « RoomEq Virtual » (sortie macOS)",\n'
    "    };\n"
)
assert old_testctor in s, "ForTests introuvable"
s = s.replace(old_testctor, new_testctor)

old_status = (
    "            if (!IsHosting) return \"Pont ASIO à l'arrêt.\";\n"
    "            var v = _view;\n"
    "            if (v == null) return \"Pont ASIO : mémoire partagée indisponible.\";"
)
new_status = (
    "            if (!IsHosting) return _idleTextOverride ?? _idleText;\n"
    "            var v = _view;\n"
    '            if (v == null) return "Pont : mémoire partagée indisponible.";'
)
assert old_status in s, "status tête introuvable"
s = s.replace(old_status, new_status)

old_tail = (
    "            return client\n"
    '                ? $"DAW connecté (48 kHz) — {backlogMs:0} ms en attente"\n'
    '                  + (dropped > 0 ? $", {dropped} éch. écartées" : "")\n'
    '                : "En attente d\'un DAW (choisissez « RoomEq ASIO » dans son logiciel audio)";'
)
new_tail = (
    "            return client\n"
    '                ? $"{_connectedTextOverride ?? _connectedText} — {backlogMs:0} ms en attente"\n'
    '                  + (dropped > 0 ? $", {dropped} éch. écartées" : "")\n'
    "                : _waitingTextOverride ?? _waitingText;"
)
assert old_tail in s, "status queue introuvable"
s = s.replace(old_tail, new_tail)

io.open(P, 'w', encoding='utf-8').write(s)
print("AsioBridgeHost.cs : 6 remplacements OK")
