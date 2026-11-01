#!/usr/bin/env python3
"""Vérificateur PE pour les DLL APO RoomEq (build-apo.sh).

binutils-objdump ne reconnaît pas le PE ARM64 sur cette chaîne : ce script
relit directement les tables d'export et d'import du fichier (formats
documentés PE/COFF — Microsoft Learn, « PE Format »).

Usage : check_pe_exports.py <dll> <x64|arm64> <symbole>...
Sortie : OK/ÉCHEC + listes ; code de sortie non nul si un contrôle échoue.
"""
import struct
import sys

MACHINES = {"x64": 0x8664, "arm64": 0xAA64}
IMAGE_FILE_DLL = 0x2000


def die(msg: str) -> None:
    print(f"  ÉCHEC : {msg}")
    raise SystemExit(1)


def main() -> int:
    if len(sys.argv) < 4:
        print("usage: check_pe_exports.py <dll> <x64|arm64> <symbole>...", file=sys.stderr)
        return 2
    path, arch, required = sys.argv[1], sys.argv[2], sys.argv[3:]
    blob = open(path, "rb").read()

    # ---------------------------------------------------------------- en-têtes
    if blob[:2] != b"MZ":
        die("pas un exécutable Windows (magic MZ absent)")
    (e_lfanew,) = struct.unpack_from("<I", blob, 0x3C)
    if blob[e_lfanew:e_lfanew + 4] != b"PE\x00\x00":
        die("signature PE absente")
    coff = e_lfanew + 4
    machine, nsec, _, _, _, opt_size, characteristics = struct.unpack_from(
        "<HHIIIHH", blob, coff)
    if machine != MACHINES.get(arch):
        die(f"machine 0x{machine:04X} ≠ {arch} (0x{MACHINES.get(arch):04X})")
    if not (characteristics & IMAGE_FILE_DLL):
        die("bit IMAGE_FILE_DLL absent (pas une DLL)")
    opt = coff + 20
    (magic,) = struct.unpack_from("<H", blob, opt)
    if magic != 0x20B:
        die(f"optional header magic 0x{magic:04X} ≠ PE32+ (0x20B)")

    # ---------------------------------------------------------------- sections
    dd_off = opt + (112 if magic == 0x20B else 96)
    dirs = [struct.unpack_from("<II", blob, dd_off + 8 * i) for i in range(16)]
    sections = []
    sec_off = opt + opt_size
    for i in range(nsec):
        name, vsize, vaddr, rsize, rptr = struct.unpack_from(
            "<8sIIII", blob, sec_off + 40 * i)
        sections.append((vaddr, max(vsize, rsize), rptr))

    def rva_to_off(rva: int) -> int:
        for vaddr, size, rptr in sections:
            if vaddr <= rva < vaddr + size:
                return rva - vaddr + rptr
        die(f"RVA 0x{rva:08X} hors sections")

    # ---------------------------------------------------------------- exports
    exports: list[str] = []
    exp_rva, _ = dirs[0]
    if exp_rva:
        e = rva_to_off(exp_rva)
        (_chars, _ts, _maj, _min, _name_rva, _base, _nfun, nnames,
         _aof, aon, _aoo) = struct.unpack_from("<IIHHIIIIIII", blob, e)
        for i in range(nnames):
            (name_rva,) = struct.unpack_from("<I", blob, rva_to_off(aon) + 4 * i)
            off = rva_to_off(name_rva)
            end = blob.index(b"\x00", off)
            exports.append(blob[off:end].decode("ascii"))
    missing = [s for s in required if s not in exports]
    if missing:
        die(f"exports manquants : {', '.join(missing)} (trouvés : {', '.join(exports) or 'aucun'})")

    # ---------------------------------------------------------------- imports
    imports: list[str] = []
    imp_rva, _ = dirs[1]
    if imp_rva:
        d = rva_to_off(imp_rva)
        while True:
            oft, _ts, _fwd, name_rva, _ftk = struct.unpack_from("<IIIII", blob, d)
            if oft == 0 and name_rva == 0:
                break
            off = rva_to_off(name_rva)
            end = blob.index(b"\x00", off)
            imports.append(blob[off:end].decode("ascii").lower())
            d += 20
    allowed = ("kernel32.dll", "ole32.dll", "ntdll.dll")
    bad = [i for i in imports if not i.startswith("api-ms-")
           and not i.startswith("ext-ms-") and i not in allowed]
    if bad:
        die(f"dépendances inattendues : {', '.join(bad)} (la DLL APO doit être autonome)")

    print(f"  machine {arch} + DLL ✓ ; exports : {', '.join(exports)} ; "
          f"imports : {', '.join(imports) or 'aucun'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
