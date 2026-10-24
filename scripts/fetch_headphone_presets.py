#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Récupère les corrections paramétriques AutoEq (MIT, Jaakko Pasanen) pour une
liste curatoriale large de casques et écouteurs, et génère la bibliothèque
JSON embarquée dans RoomEq.Core (Headphones/headphone_presets.json).

Structure AutoEq master : results/<source>/<catégorie>/<Modèle>/<Modèle> ParametricEQ.txt
Sources couvertes : oratory1990, Super Review, crinacle, Rtings, HypetheSonics,
Innerfidelity, Headphone.com Legacy, ToneDeafMonk, kr0mka, Ted's Squig Hoard, …
Priorité en cas de doublon inter-sources : oratory1990 > Super Review > crinacle
> Rtings > HypetheSonics > Innerfidelity > Headphone.com Legacy > autres.
"""

import json
import os
import re
import sys
import time
import urllib.request
import urllib.error
from concurrent.futures import ThreadPoolExecutor

REPO = "jaakkopasanen/AutoEq"
TOKEN_FILE = "/home/z/my-project/secrets/token_git.txt"
CACHE = "/home/z/my-project/scripts/.cache/autoeq"
OUT_JSON = "/home/z/my-project/workspace/roomq-source/RoomEq/src/RoomEq.Core/Headphones/headphone_presets.json"
OUT_REPORT = "/home/z/my-project/scripts/.cache/autoeq/report.txt"

SOURCE_PRIORITY = [
    "oratory1990", "Super Review", "crinacle", "Rtings", "HypetheSonics",
    "Innerfidelity", "Headphone.com Legacy", "ToneDeafMonk", "kr0mka",
    "Ted's Squig Hoard", "Kuulokenurkka", "Regan Cipher", "RikudouGoku",
    "Jaytiss", "Kazi", "Harpo", "Filk", "Fahryst", "DHRME", "Bakkwatan",
    "Hi End Portable", "Auriculares Argentina",
]

# ---------------------------------------------------------------------------
# Liste curatoriale : (marque, modèle, type, gamme, [motifs alternatifs])
# type : over-ear | in-ear | earbud | gaming
# gamme (indicative) : budget | moyenne | premium
# ---------------------------------------------------------------------------
W = [
    # --- Sennheiser (circum-aural) ---
    ("Sennheiser", "HD 25", "over-ear", "moyenne", ["sennheiser hd 25", "sennheiser hd25"]),
    ("Sennheiser", "HD 280 Pro", "over-ear", "budget", []),
    ("Sennheiser", "HD 380 Pro", "over-ear", "moyenne", []),
    ("Sennheiser", "HD 4.40BT", "over-ear", "budget", []),
    ("Sennheiser", "HD 450BT", "over-ear", "budget", []),
    ("Sennheiser", "HD 560S", "over-ear", "moyenne", []),
    ("Sennheiser", "HD 569", "over-ear", "moyenne", []),
    ("Sennheiser", "HD 579", "over-ear", "moyenne", []),
    ("Sennheiser", "HD 598", "over-ear", "moyenne", []),
    ("Sennheiser", "HD 599", "over-ear", "moyenne", []),
    ("Sennheiser", "HD 600", "over-ear", "premium", []),
    ("Sennheiser", "HD 650", "over-ear", "premium", []),
    ("Sennheiser", "HD 660S", "over-ear", "premium", []),
    ("Sennheiser", "HD 660S2", "over-ear", "premium", []),
    ("Sennheiser", "HD 6XX", "over-ear", "premium", ["sennheiser hd 6xx", "drop + sennheiser hd 6xx", "massdrop x sennheiser hd 6xx"]),
    ("Sennheiser", "HD 58X", "over-ear", "moyenne", ["sennheiser hd 58x", "drop + sennheiser hd 58x", "massdrop x sennheiser hd 58x jubilee"]),
    ("Sennheiser", "HD 620S", "over-ear", "moyenne", ["sennheiser hd 620s"]),
    ("Sennheiser", "HD 700", "over-ear", "premium", []),
    ("Sennheiser", "HD 800", "over-ear", "premium", []),
    ("Sennheiser", "HD 800S", "over-ear", "premium", ["sennheiser hd 800s"]),
    ("Sennheiser", "HD 820", "over-ear", "premium", []),
    ("Sennheiser", "Momentum 4 Wireless", "over-ear", "premium", ["sennheiser momentum 4 wireless"]),
    ("Sennheiser", "Momentum 3 Wireless", "over-ear", "premium", ["sennheiser momentum 3 wireless"]),
    ("Sennheiser", "Momentum 2 Wireless", "over-ear", "moyenne", ["sennheiser momentum 2.0 wireless", "sennheiser momentum 2 wireless"]),
    # --- Beyerdynamic ---
    ("Beyerdynamic", "DT 770 Pro", "over-ear", "moyenne", ["beyerdynamic dt 770 pro"]),
    ("Beyerdynamic", "DT 880", "over-ear", "moyenne", ["beyerdynamic dt 880 edition", "beyerdynamic dt 880 600 ohm", "beyerdynamic dt 880"]),
    ("Beyerdynamic", "DT 990 Pro", "over-ear", "moyenne", ["beyerdynamic dt 990 pro"]),
    ("Beyerdynamic", "DT 1770 Pro", "over-ear", "premium", []),
    ("Beyerdynamic", "DT 1990 Pro", "over-ear", "premium", []),
    ("Beyerdynamic", "DT 700 Pro X", "over-ear", "moyenne", []),
    ("Beyerdynamic", "DT 900 Pro X", "over-ear", "moyenne", []),
    ("Beyerdynamic", "DT 1350", "over-ear", "moyenne", []),
    ("Beyerdynamic", "DT 240 Pro", "over-ear", "budget", []),
    ("Beyerdynamic", "Custom One Pro", "over-ear", "moyenne", ["beyerdynamic custom one pro"]),
    ("Beyerdynamic", "TYGR 300", "over-ear", "moyenne", ["beyerdynamic tygr 300 r"]),
    ("Beyerdynamic", "T1", "over-ear", "premium", ["beyerdynamic t1 (3rd gen)", "beyerdynamic t1 3rd gen"]),
    ("Beyerdynamic", "T5", "over-ear", "premium", ["beyerdynamic t5 3rd generation", "beyerdynamic t5 (3rd gen)"]),
    ("Beyerdynamic", "MMX 300", "gaming", "moyenne", ["beyerdynamic mmx 300 (2nd gen)", "beyerdynamic mmx 300"]),
    # --- AKG ---
    ("AKG", "K240 Studio", "over-ear", "budget", ["akg k240 studio"]),
    ("AKG", "K240 MKII", "over-ear", "budget", ["akg k240 mkii"]),
    ("AKG", "K271 MKII", "over-ear", "budget", ["akg k271 mkii"]),
    ("AKG", "K361", "over-ear", "budget", ["akg k361"]),
    ("AKG", "K371", "over-ear", "budget", ["akg k371"]),
    ("AKG", "K52", "over-ear", "budget", ["akg k52"]),
    ("AKG", "K72", "over-ear", "budget", ["akg k72"]),
    ("AKG", "K601", "over-ear", "moyenne", ["akg k601"]),
    ("AKG", "K612", "over-ear", "moyenne", ["akg k612 pro"]),
    ("AKG", "K701", "over-ear", "moyenne", ["akg k701"]),
    ("AKG", "K702", "over-ear", "moyenne", ["akg k702"]),
    ("AKG", "K712", "over-ear", "moyenne", ["akg k712 pro"]),
    ("AKG", "K7XX", "over-ear", "moyenne", ["akg k7xx", "drop + akg k7xx", "massdrop x akg k7xx"]),
    ("AKG", "K812", "over-ear", "premium", ["akg k812"]),
    ("AKG", "Q701", "over-ear", "moyenne", ["akg q701"]),
    ("AKG", "Y50", "over-ear", "budget", ["akg y50"]),
    ("AKG", "N60 NC Wireless", "over-ear", "moyenne", ["akg n60 nc wireless"]),
    ("AKG", "N700NC M2", "over-ear", "moyenne", ["akg n700nc m2"]),
    # --- Sony ---
    ("Sony", "MDR-7506", "over-ear", "budget", ["sony mdr-7506"]),
    ("Sony", "MDR-V6", "over-ear", "budget", ["sony mdr-v6"]),
    ("Sony", "MDR-1A", "over-ear", "moyenne", ["sony mdr-1a", "sony mdr-1am2"]),
    ("Sony", "WH-1000XM2", "over-ear", "premium", ["sony wh-1000xm2"]),
    ("Sony", "WH-1000XM3", "over-ear", "premium", ["sony wh-1000xm3"]),
    ("Sony", "WH-1000XM4", "over-ear", "premium", ["sony wh-1000xm4"]),
    ("Sony", "WH-1000XM5", "over-ear", "premium", ["sony wh-1000xm5"]),
    ("Sony", "WH-CH710N", "over-ear", "budget", ["sony wh-ch710n"]),
    ("Sony", "WH-XB910N", "over-ear", "moyenne", ["sony wh-xb910n"]),
    ("Sony", "MDR-Z1R", "over-ear", "premium", ["sony mdr-z1r"]),
    ("Sony", "MDR-R10", "over-ear", "premium", ["sony mdr-r10"]),
    # --- Audio-Technica ---
    ("Audio-Technica", "ATH-M20x", "over-ear", "budget", ["audio-technica ath-m20x"]),
    ("Audio-Technica", "ATH-M30x", "over-ear", "budget", ["audio-technica ath-m30x"]),
    ("Audio-Technica", "ATH-M40x", "over-ear", "budget", ["audio-technica ath-m40x"]),
    ("Audio-Technica", "ATH-M50x", "over-ear", "moyenne", ["audio-technica ath-m50x"]),
    ("Audio-Technica", "ATH-M60x", "over-ear", "moyenne", ["audio-technica ath-m60x"]),
    ("Audio-Technica", "ATH-R70x", "over-ear", "moyenne", ["audio-technica ath-r70x"]),
    ("Audio-Technica", "ATH-AD500X", "over-ear", "moyenne", ["audio-technica ath-ad500x"]),
    ("Audio-Technica", "ATH-AD700X", "over-ear", "moyenne", ["audio-technica ath-ad700x"]),
    ("Audio-Technica", "ATH-AD900X", "over-ear", "moyenne", ["audio-technica ath-ad900x"]),
    ("Audio-Technica", "ATH-AD1000X", "over-ear", "premium", ["audio-technica ath-ad1000x"]),
    ("Audio-Technica", "ATH-A990Z", "over-ear", "moyenne", ["audio-technica ath-a990z"]),
    # --- Bose ---
    ("Bose", "QuietComfort 25", "over-ear", "moyenne", ["bose quietcomfort 25", "bose qc25"]),
    ("Bose", "QuietComfort 35", "over-ear", "moyenne", ["bose quietcomfort 35", "bose qc35"]),
    ("Bose", "QuietComfort 45", "over-ear", "premium", ["bose quietcomfort 45", "bose qc45"]),
    ("Bose", "QuietComfort Ultra", "over-ear", "premium", ["bose quietcomfort ultra headphones", "bose quietcomfort ultra"]),
    ("Bose", "700", "over-ear", "premium", ["bose noise cancelling headphones 700", "bose 700"]),
    ("Bose", "QuietComfort Earbuds II", "in-ear", "premium", ["bose quietcomfort earbuds ii", "bose quietcomfort earbuds ultra"]),
    # --- Beats / Apple ---
    ("Beats", "Solo3 Wireless", "over-ear", "moyenne", ["beats solo3 wireless"]),
    ("Beats", "Studio3 Wireless", "over-ear", "moyenne", ["beats studio3 wireless"]),
    ("Beats", "Powerbeats Pro", "in-ear", "moyenne", ["beats powerbeats pro"]),
    ("Beats", "Fit Pro", "in-ear", "moyenne", ["beats fit pro"]),
    ("Apple", "AirPods 2", "earbud", "budget", ["apple airpods 2", "apple airpods (2nd generation)"]),
    ("Apple", "AirPods 3", "earbud", "moyenne", ["apple airpods 3", "apple airpods (3rd generation)"]),
    ("Apple", "AirPods Pro", "in-ear", "moyenne", ["apple airpods pro"]),
    ("Apple", "AirPods Pro 2", "in-ear", "moyenne", ["apple airpods pro 2", "apple airpods pro (2nd generation)"]),
    ("Apple", "AirPods Max", "over-ear", "premium", ["apple airpods max"]),
    ("Apple", "EarPods", "earbud", "budget", ["apple earpods"]),
    # --- Audeze ---
    ("Audeze", "LCD-2", "over-ear", "premium", ["audeze lcd-2 classic", "audeze lcd-2"]),
    ("Audeze", "LCD-X", "over-ear", "premium", ["audeze lcd-x"]),
    ("Audeze", "MM-500", "over-ear", "premium", ["audeze mm-500"]),
    ("Audeze", "Mobius", "gaming", "moyenne", ["audeze mobius"]),
    ("Audeze", "Maxwell", "gaming", "premium", ["audeze maxwell"]),
    ("Audeze", "Sine", "over-ear", "premium", ["audeze sine"]),
    ("Audeze", "Euclid", "in-ear", "premium", ["audeze euclid"]),
    # --- Focal ---
    ("Focal", "Clear", "over-ear", "premium", ["focal clear", "focal clear mg"]),
    ("Focal", "Elegia", "over-ear", "premium", ["focal elegia"]),
    ("Focal", "Stellia", "over-ear", "premium", ["focal stellia"]),
    ("Focal", "Listen", "over-ear", "moyenne", ["focal listen"]),
    ("Focal", "Celestee", "over-ear", "premium", ["focal celestee"]),
    # --- HiFiMAN ---
    ("HiFiMAN", "HE400se", "over-ear", "budget", ["hifiman he400se"]),
    ("HiFiMAN", "HE400i", "over-ear", "moyenne", ["hifiman he400i"]),
    ("HiFiMAN", "HE4XX", "over-ear", "budget", ["hifiman he4xx", "drop + hifiman he4xx", "massdrop x hifiman he4xx"]),
    ("HiFiMAN", "HE560", "over-ear", "moyenne", ["hifiman he560"]),
    ("HiFiMAN", "HE6se", "over-ear", "premium", ["hifiman he6se"]),
    ("HiFiMAN", "Sundara", "over-ear", "moyenne", ["hifiman sundara"]),
    ("HiFiMAN", "Sundara Closed", "over-ear", "moyenne", ["hifiman sundara closed-back", "hifiman sundara closed"]),
    ("HiFiMAN", "Ananda", "over-ear", "premium", ["hifiman ananda"]),
    ("HiFiMAN", "Arya", "over-ear", "premium", ["hifiman arya", "hifiman arya organic"]),
    ("HiFiMAN", "Edition XS", "over-ear", "premium", ["hifiman edition xs"]),
    ("HiFiMAN", "Edition XX", "over-ear", "moyenne", ["hifiman edition xx", "drop + hifiman edition xx"]),
    ("HiFiMAN", "Deva", "over-ear", "moyenne", ["hifiman deva", "hifiman deva pro"]),
    ("HiFiMAN", "RE400", "in-ear", "budget", ["hifiman re400"]),
    # --- Meze ---
    ("Meze", "99 Classics", "over-ear", "moyenne", ["meze 99 classics"]),
    ("Meze", "99 Neo", "over-ear", "moyenne", ["meze 99 neo"]),
    ("Meze", "109 Pro", "over-ear", "premium", ["meze 109 pro"]),
    ("Meze", "Empyrean", "over-ear", "premium", ["meze empyrean"]),
    # --- Shure ---
    ("Shure", "SRH-440", "over-ear", "budget", ["shure srh440", "shure srh-440"]),
    ("Shure", "SRH-840", "over-ear", "moyenne", ["shure srh840", "shure srh-840"]),
    ("Shure", "SRH-1540", "over-ear", "premium", ["shure srh1540", "shure srh-1540"]),
    ("Shure", "SRH-1840", "over-ear", "premium", ["shure srh1840", "shure srh-1840"]),
    ("Shure", "SE215", "in-ear", "budget", ["shure se215"]),
    ("Shure", "SE315", "in-ear", "moyenne", ["shure se315"]),
    ("Shure", "SE425", "in-ear", "moyenne", ["shure se425"]),
    ("Shure", "SE535", "in-ear", "premium", ["shure se535"]),
    ("Shure", "SE846", "in-ear", "premium", ["shure se846"]),
    ("Shure", "Aonic 50", "over-ear", "moyenne", ["shure aonic 50"]),
    # --- Grado / Koss / Superlux / Philips / Yamaha / V-Moda / Samson ---
    ("Grado", "SR60", "over-ear", "budget", ["grado sr60e", "grado sr60x", "grado sr60"]),
    ("Grado", "SR80", "over-ear", "budget", ["grado sr80e", "grado sr80x", "grado sr80"]),
    ("Grado", "SR125", "over-ear", "budget", ["grado sr125e", "grado sr125"]),
    ("Grado", "SR325", "over-ear", "moyenne", ["grado sr325e", "grado sr325x", "grado sr325"]),
    ("Grado", "PS500", "over-ear", "premium", ["grado ps500e", "grado ps500"]),
    ("Grado", "GS1000", "over-ear", "premium", ["grado gs1000e", "grado gs1000"]),
    ("Koss", "KSC75", "over-ear", "budget", ["koss ksc75"]),
    ("Koss", "Porta Pro", "over-ear", "budget", ["koss porta pro"]),
    ("Koss", "KPH30i", "over-ear", "budget", ["koss kph30i"]),
    ("Koss", "KPH40", "over-ear", "budget", ["koss kph40 utility"]),
    ("Superlux", "HD 668B", "over-ear", "budget", ["superlux hd 668b"]),
    ("Superlux", "HD 681", "over-ear", "budget", ["superlux hd 681"]),
    ("Philips", "Fidelio X2HR", "over-ear", "moyenne", ["philips fidelio x2hr", "philips fidelio x2"]),
    ("Philips", "Fidelio X3", "over-ear", "moyenne", ["philips fidelio x3"]),
    ("Yamaha", "HPH-MT220", "over-ear", "budget", ["yamaha hph-mt220"]),
    ("Yamaha", "HPH-200", "over-ear", "budget", ["yamaha hph-200"]),
    ("Yamaha", "YH-E700A", "over-ear", "moyenne", ["yamaha yh-e700a (anc on)", "yamaha yh-e700a"]),
    ("V-Moda", "M-100", "over-ear", "moyenne", ["v-moda m-100", "v-moda crossfade m-100"]),
    ("V-Moda", "M-200", "over-ear", "moyenne", ["v-moda m-200"]),
    ("1MORE", "SonoFlow", "over-ear", "budget", ["1more sonoflow"]),
    ("1MORE", "Triple Driver", "in-ear", "moyenne", ["1more triple driver", "1more eo323"]),
    # --- Gaming ---
    ("HyperX", "Cloud II", "gaming", "moyenne", ["hyperx cloud ii"]),
    ("HyperX", "Cloud Alpha", "gaming", "moyenne", ["hyperx cloud alpha"]),
    ("HyperX", "Cloud Stinger", "gaming", "budget", ["hyperx cloud stinger"]),
    ("HyperX", "Cloud Revolver", "gaming", "moyenne", ["hyperx cloud revolver"]),
    ("SteelSeries", "Arctis 1", "gaming", "budget", ["steelseries arctis 1"]),
    ("SteelSeries", "Arctis 5", "gaming", "moyenne", ["steelseries arctis 5"]),
    ("SteelSeries", "Arctis 7", "gaming", "moyenne", ["steelseries arctis 7"]),
    ("SteelSeries", "Arctis 7X", "gaming", "moyenne", ["steelseries arctis 7x"]),
    ("SteelSeries", "Arctis Nova 7", "gaming", "moyenne", ["steelseries arctis nova 7"]),
    ("SteelSeries", "Arctis Nova Pro", "gaming", "premium", ["steelseries arctis nova pro"]),
    ("Razer", "BlackShark V2", "gaming", "moyenne", ["razer blackshark v2"]),
    ("Razer", "BlackShark V2 X", "gaming", "budget", ["razer blackshark v2 x"]),
    ("Razer", "Kraken V3", "gaming", "budget", ["razer kraken v3"]),
    ("Logitech G", "G432", "gaming", "budget", ["logitech g432"]),
    ("Logitech G", "G733", "gaming", "moyenne", ["logitech g733"]),
    ("Logitech G", "G Pro X", "gaming", "moyenne", ["logitech g pro x"]),
    ("Logitech G", "G Pro X 2", "gaming", "premium", ["logitech g pro x 2 lightspeed", "logitech g pro x 2"]),
    ("Corsair", "HS60", "gaming", "budget", ["corsair hs60"]),
    ("Corsair", "HS70", "gaming", "moyenne", ["corsair hs70"]),
    ("EPOS Sennheiser", "GSP 600", "gaming", "moyenne", ["epos sennheiser gsp 600", "sennheiser gsp 600"]),
    # --- Sans fil / grand public ---
    ("JBL", "Live 660NC", "over-ear", "moyenne", ["jbl live 660nc"]),
    ("JBL", "Tune 660NC", "over-ear", "budget", ["jbl tune 660nc"]),
    ("JBL", "Tune 510BT", "over-ear", "budget", ["jbl tune 510bt"]),
    ("JBL", "Tune 770NC", "over-ear", "moyenne", ["jbl tune 770nc"]),
    ("JBL", "Live Pro 2", "in-ear", "moyenne", ["jbl live pro 2 tws", "jbl live pro 2"]),
    ("Jabra", "Elite 85h", "over-ear", "moyenne", ["jabra elite 85h"]),
    ("Jabra", "Elite 75t", "in-ear", "moyenne", ["jabra elite 75t"]),
    ("Jabra", "Elite 85t", "in-ear", "moyenne", ["jabra elite 85t"]),
    ("Jabra", "Elite 7 Pro", "in-ear", "moyenne", ["jabra elite 7 pro"]),
    ("Anker", "Soundcore Life Q30", "over-ear", "budget", ["anker soundcore life q30"]),
    ("Anker", "Soundcore Space Q45", "over-ear", "budget", ["anker soundcore space q45"]),
    ("Anker", "Soundcore Liberty 4 Pro", "in-ear", "moyenne", ["anker liberty 4 pro", "anker soundcore liberty 4 pro"]),
    ("Samsung", "Galaxy Buds Pro", "in-ear", "moyenne", ["samsung galaxy buds pro"]),
    ("Samsung", "Galaxy Buds2 Pro", "in-ear", "premium", ["samsung galaxy buds2 pro"]),
    ("Samsung", "Galaxy Buds FE", "in-ear", "budget", ["samsung galaxy buds fe"]),
    ("Sony", "WF-1000XM3", "in-ear", "moyenne", ["sony wf-1000xm3"]),
    ("Sony", "WF-1000XM4", "in-ear", "premium", ["sony wf-1000xm4"]),
    ("Sony", "WF-1000XM5", "in-ear", "premium", ["sony wf-1000xm5"]),
    ("Sony", "LinkBuds S", "in-ear", "moyenne", ["sony linkbuds s"]),
    ("Sennheiser", "Momentum True Wireless 2", "in-ear", "premium", ["sennheiser momentum true wireless 2"]),
    ("Sennheiser", "Momentum True Wireless 3", "in-ear", "premium", ["sennheiser momentum true wireless 3"]),
    ("Sennheiser", "Momentum True Wireless 4", "in-ear", "premium", ["sennheiser momentum true wireless 4"]),
    # --- Intra-aural audiophiles / Chi-Fi ---
    ("Etymotic", "ER2SE", "in-ear", "moyenne", ["etymotic er2se"]),
    ("Etymotic", "ER2XR", "in-ear", "moyenne", ["etymotic er2xr"]),
    ("Etymotic", "ER3SE", "in-ear", "moyenne", ["etymotic er3se"]),
    ("Etymotic", "ER4SR", "in-ear", "premium", ["etymotic er4sr"]),
    ("Etymotic", "ER4XR", "in-ear", "premium", ["etymotic er4xr"]),
    ("Etymotic", "EVO", "in-ear", "premium", ["etymotic evo"]),
    ("Moondrop", "Aria", "in-ear", "budget", ["moondrop aria"]),
    ("Moondrop", "Starfield", "in-ear", "moyenne", ["moondrop starfield"]),
    ("Moondrop", "KATO", "in-ear", "moyenne", ["moondrop kato"]),
    ("Moondrop", "CHU", "in-ear", "budget", ["moondrop chu"]),
    ("Moondrop", "CHU 2", "in-ear", "budget", ["moondrop chu 2", "moondrop chu ii"]),
    ("Moondrop", "Variations", "in-ear", "premium", ["moondrop variations"]),
    ("Moondrop", "Blessing 2", "in-ear", "moyenne", ["moondrop blessing 2"]),
    ("Moondrop", "S8", "in-ear", "premium", ["moondrop s8"]),
    ("Moondrop", "Quark", "in-ear", "budget", ["moondrop quark"]),
    ("Truthear", "Hexa", "in-ear", "moyenne", ["truthear hexa"]),
    ("Truthear", "Zero", "in-ear", "budget", ["truthear x crinacle zero", "truthear zero"]),
    ("Truthear", "Zero RED", "in-ear", "budget", ["truthear x crinacle zero red"]),
    ("Truthear", "Nova", "in-ear", "moyenne", ["truthear nova (silicone eartips)", "truthear nova"]),
    ("Truthear", "Hola", "in-ear", "budget", ["truthear hola"]),
    ("7Hz", "Salnotes Zero", "in-ear", "budget", ["7hz salnotes zero"]),
    ("7Hz", "Timeless", "in-ear", "moyenne", ["7hz timelez", "7hz timeless"]),
    ("7Hz", "Sonus", "in-ear", "moyenne", ["7hz sonus"]),
    ("Tanchjim", "One", "in-ear", "budget", ["tanchjim one"]),
    ("Tanchjim", "Darling", "in-ear", "moyenne", ["tanchjim darling"]),
    ("Tin HiFi", "T2", "in-ear", "budget", ["tin hiFi t2", "tinhifi t2"]),
    ("Tin HiFi", "T2 Plus", "in-ear", "moyenne", ["tin hiFi t2 plus", "tinhifi t2 plus"]),
    ("Tin HiFi", "P1", "in-ear", "moyenne", ["tin hiFi p1", "tinhifi p1"]),
    ("Blon", "BL-03", "in-ear", "budget", ["blon bl-03"]),
    ("KZ", "ZSN Pro", "in-ear", "budget", ["kz zsn pro"]),
    ("KZ", "ZS10", "in-ear", "budget", ["kz zs10 pro"]),
    ("CCA", "CRA", "in-ear", "budget", ["cca cra"]),
    ("Kiwi Ears", "Cadenza", "in-ear", "moyenne", ["kiwi ears cadenza"]),
    ("Kiwi Ears", "Orchestra Lite", "in-ear", "moyenne", ["kiwi ears orchestra lite"]),
    ("Simgot", "EA500", "in-ear", "moyenne", ["simgot audio ea500 (black nozzle)", "simgot audio ea500", "simgot ea500"]),
    ("Simgot", "EM6L", "in-ear", "moyenne", ["simgot audio em6l", "simgot em6l"]),
    ("Simgot", "EA1000", "in-ear", "premium", ["simgot audio ea1000 (red nozzle)", "simgot audio ea1000", "simgot ea1000"]),
    ("ThieAudio", "Legacy 3", "in-ear", "moyenne", ["thieaudio legacy 3"]),
    ("ThieAudio", "Legacy 4", "in-ear", "moyenne", ["thieaudio legacy 4 (off-off)", "thieaudio legacy 4"]),
    ("ThieAudio", "Hype 2", "in-ear", "moyenne", ["thieaudio hype 2"]),
    ("ThieAudio", "Monarch MKIII", "in-ear", "premium", ["thieaudio monarch mkiii"]),
    ("ThieAudio", "Clairvoyance", "in-ear", "premium", ["thieaudio clairvoyance"]),
    ("64 Audio", "U4S", "in-ear", "premium", ["64 audio u4s"]),
    ("64 Audio", "Duo", "in-ear", "premium", ["64 audio duo"]),
    ("Campfire Audio", "Andromeda", "in-ear", "premium", ["campfire audio andromeda (2020)", "campfire audio andromeda"]),
    ("Campfire Audio", "Ara", "in-ear", "premium", ["campfire audio ara"]),
    ("Westone", "MACH 80", "in-ear", "premium", ["westone mach 80"]),
    ("Symphonium", "Meteor", "in-ear", "premium", ["symphonium audio meteor"]),
    ("Symphonium", "Triton", "in-ear", "moyenne", ["symphonium audio triton"]),
    ("FiiO", "FH3", "in-ear", "moyenne", ["fiio fh3"]),
    ("FiiO", "FD5", "in-ear", "moyenne", ["fiio fd5"]),
    ("FiiO", "FT3", "over-ear", "moyenne", ["fiio ft3"]),
    ("FiiO", "FT5", "over-ear", "moyenne", ["fiio ft5"]),
    ("Sony", "IER-Z1R", "in-ear", "premium", ["sony ier-z1r"]),
    ("Xenns", "Mangird Top", "in-ear", "premium", ["xenns mangird top"]),
    ("Yanyin", "Canon", "in-ear", "premium", ["yanyin canon"]),
    # --- Earbuds ---
    ("FiiO", "FF1", "earbud", "budget", ["fiio ff1"]),
    ("TGXear", "Serratus", "earbud", "moyenne", ["tgxear serratus"]),
    ("Smabat", "ST10s", "earbud", "budget", ["smabat st10s"]),
]

TYPE_MAP = {"PK": "peaking", "P": "peaking",
            "LSC": "low-shelf", "LS": "low-shelf", "LOW_SHELF": "low-shelf",
            "HSC": "high-shelf", "HS": "high-shelf", "HIGH_SHELF": "high-shelf"}

PRE_RE = re.compile(r"Preamp:\s*(-?[\d.]+)\s*dB", re.I)
FLT_RE = re.compile(
    r"Filter\s+\d+:\s+(ON|OFF)\s+([A-Za-z_]+)\s+Fc\s+([\d.]+)\s*Hz\s+"
    r"Gain\s+(-?[\d.]+)\s*dB\s+Q\s+([\d.]+)", re.I)


def norm(s: str) -> str:
    return re.sub(r"[^a-z0-9]", "", s.lower())


def http_get(url: str, token: str | None = None, tries: int = 3) -> bytes:
    last = None
    for i in range(tries):
        try:
            req = urllib.request.Request(url)
            if token:
                req.add_header("Authorization", "Bearer " + token)
            with urllib.request.urlopen(req, timeout=40) as r:
                return r.read()
        except Exception as e:  # noqa: BLE001
            last = e
            time.sleep(1.5 * (i + 1))
    raise RuntimeError(f"GET échoué {url} : {last}")


def cached_json(name: str, url: str, token: str):
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, name)
    if os.path.exists(path) and os.path.getsize(path) > 10:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    data = json.loads(http_get(url, token).decode("utf-8"))
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f)
    return data


def main() -> int:
    with open(TOKEN_FILE, encoding="utf-8") as f:
        token = f.read().strip()

    # 1) dossiers de sources + 2) arbres récursifs par source
    sources = cached_json("sources.json",
                          f"https://api.github.com/repos/{REPO}/contents/results", token)
    sources = [s for s in sources if s["type"] == "dir"]

    index = {}  # model_dirname -> list of (source_rank, repo_path)
    for s in sources:
        rank = SOURCE_PRIORITY.index(s["name"]) if s["name"] in SOURCE_PRIORITY else 99
        tree = cached_json("tree_" + norm(s["name"]) + ".json",
                           f"https://api.github.com/repos/{REPO}/git/trees/{s['sha']}?recursive=1",
                           token)
        if tree.get("truncated"):
            print(f"!! arbre tronqué pour {s['name']}", file=sys.stderr)
        for e in tree.get("tree", []):
            p = e["path"]
            if p.endswith("ParametricEQ.txt") and e["type"] == "blob":
                # les chemins d'arbre sont RELATIFS à la source -> préfixer
                full = f"results/{s['name']}/{p}"
                model = os.path.basename(os.path.dirname(full))
                index.setdefault(model, []).append((rank, full))
    print(f"Index : {len(index)} modèles avec ParametricEQ.txt")

    # 3) appariement (normes précalculées)
    norms = {model_dir: norm(model_dir) for model_dir in index}

    def best_for(patterns, taken):
        pats = [norm(p) for p in patterns]
        best = None  # (score, rank, model_dir, path)
        for model_dir, entries in index.items():
            if model_dir in taken:
                continue
            nd = norms[model_dir]
            score = None
            for p in pats:
                if nd == p:
                    score = 0                      # égalité exacte
                elif nd.startswith(p) and score is None:
                    score = 1                      # variante « (earpads) »
                elif p in nd and score is None:
                    score = 2                      # contient
            if score is None:
                continue
            for rank, path in sorted(entries):
                cand = (score, rank, model_dir, path)
                if best is None or cand < best:
                    best = cand
        return best

    selected = []   # (meta, path)
    taken = set()
    missing = []
    for brand, model, typ, gamme, alts in W:
        patterns = [f"{brand} {model}"] + alts
        b = best_for(patterns, taken)
        if b is None:
            missing.append(f"{brand} {model}")
            continue
        _, rank, model_dir, path = b
        taken.add(model_dir)
        src = path.split("/")[1]
        selected.append(dict(brand=brand, model=model, type=typ, gamme=gamme,
                             source=src, path=path))

    print(f"Sélection : {len(selected)} / {len(W)}  (manquants : {len(missing)})")
    for m in missing:
        print("   MANQUANT :", m)

    # 4) téléchargement parallèle + analyse
    def fetch_txt(item):
        url = ("https://raw.githubusercontent.com/" + REPO + "/master/"
               + urllib.request.quote(item["path"]))
        return item, http_get(url).decode("utf-8")

    out_models, errors = [], []
    with ThreadPoolExecutor(max_workers=10) as pool:
        results = list(pool.map(fetch_txt, selected))
    for item, txt in results:
        try:
            pm = PRE_RE.search(txt)
            filters = []
            for on, ftype, fc, gain, q in FLT_RE.findall(txt):
                if on.upper() != "ON":
                    continue
                t = TYPE_MAP.get(ftype.upper())
                if t is None:
                    continue
                filters.append({"type": t, "f": round(float(fc), 1),
                                "g": round(float(gain), 2), "q": round(float(q), 3)})
            if pm is None or not (1 <= len(filters) <= 20):
                errors.append(f"{item['path']} : format inattendu "
                              f"({len(filters)} filtres, preamp={bool(pm)})")
                continue
            preamp = max(-15.0, min(0.0, float(pm.group(1))))
            out_models.append({
                "id": norm(f"{item['brand']} {item['model']}")[:60],
                "brand": item["brand"], "model": item["model"],
                "type": item["type"], "gamme": item["gamme"],
                "source": item["source"],
                "preampDb": round(preamp, 2),
                "filters": filters,
            })
        except Exception as e:  # noqa: BLE001
            errors.append(f"{item['path']} : {e}")
            continue

    # unicité des id
    seen = {}
    for m in out_models:
        base = m["id"]
        i = seen.get(base, 0)
        seen[base] = i + 1
        if i:
            m["id"] = f"{base}-{i + 1}"

    # 5) écriture
    db = {
        "version": 1,
        "generated": time.strftime("%Y-%m-%d"),
        "source": "AutoEq (MIT) — Jaakko Pasanen — jaakkopasanen.github.io/AutoEq",
        "sources": ["oratory1990", "Super Review", "crinacle", "Rtings",
                    "HypetheSonics", "Innerfidelity", "Headphone.com Legacy"],
        "note": ("Corrections parametriques derivees de mesures publiques, cible "
                 "Harman (OE 2018 / IE 2019 selon la source). La capture acoustique "
                 "n'etant pas applicable aux casques, ces presets remplacent la "
                 "calibration pour l'ecoute au casque."),
        "models": out_models,
    }
    os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
    with open(OUT_JSON, "w", encoding="utf-8") as f:
        json.dump(db, f, ensure_ascii=False, indent=1)
        f.write("\n")

    os.makedirs(os.path.dirname(OUT_REPORT), exist_ok=True)
    with open(OUT_REPORT, "w", encoding="utf-8") as f:
        f.write(f"Modèles retenus : {len(out_models)} / {len(W)}\n\n")
        brands = {}
        for m in out_models:
            brands.setdefault(m["brand"], []).append(m["model"])
        for b in sorted(brands):
            f.write(f"{b} ({len(brands[b])}) : {', '.join(sorted(brands[b]))}\n")
        f.write("\nMANQUANTS :\n" + ("\n".join(" - " + m for m in missing) or " aucun") + "\n")
        f.write("\nERREURS :\n" + ("\n".join(" - " + e for e in errors) or " aucune") + "\n")

    print(f"Écrit {OUT_JSON} ({len(out_models)} modèles, "
          f"{os.path.getsize(OUT_JSON)} octets)")
    print(f"Rapport : {OUT_REPORT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
