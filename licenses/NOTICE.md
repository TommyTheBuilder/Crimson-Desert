# Third-party notices

## Trinity

Engine layout, signatures and transaction algorithms are adapted from XeTrinityz/Trinity,
commit 70c9a00dd6e10b2081d706a837756844c11f5c2b.
Copyright (c) 2026 XeTrinityz. MIT license; full text in TRINITY-LICENSE.txt.
Source: https://github.com/XeTrinityz/Trinity

## External Windows reader

Version 0.3 introduced a separate C# process with Windows query/read permissions.
It does not load Frida or inject a DLL into the game. The former Frida runtime and
injected agent scripts have been removed from this release. Historical Frida license
texts are retained only as part of the earlier distribution's attribution record.

Current layout details were independently checked against the installed EXE.
Additional primary references consulted were MIT projects:
- https://github.com/tkhquang/CrimsonDesertTools/tree/6c9ccd18446aae08f60b8b6cc2a29e1fbd886b7f
- https://github.com/shin2344234/master-looter/tree/84b9e0ecb4586cb48c1362587f772067714dc60a

Windows access-right documentation:
- https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualqueryex
- https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights

## Offline save editor / item insertion

Version 0.4 adds `runtime/PywelSaveEditor.exe` for safe item insertion while Crimson
Desert is fully closed. The helper is built, without source modifications, from the
`parc_engine_cli` target of NattKh/CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS at the
pinned commit `96e7f78fcb00d6cb5615171a7f359f0c2de5b114`.

That project is licensed under Mozilla Public License 2.0. The build copies its
`LICENSE.txt` to `licenses/CrimsonSaveEditor-MPL-2.0.txt` in the generated trainer
release. The exact Source Code Form used to build the helper is available at:
https://github.com/NattKh/CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS/tree/96e7f78fcb00d6cb5615171a7f359f0c2de5b114/CrimsonSaveEditorCpp

The trainer calls this helper as a separate executable. It creates a backup first,
validates the generated save, and refuses to edit a save while a CrimsonDesert
process is running. No save-editor code is linked into PywelTrainer.exe.

## Node.js v24.18.0

Unmodified Windows x64 node.exe. Node.js and included components' license text is in
NODE-LICENSE.txt. Matching source: https://github.com/nodejs/node/tree/v24.18.0

## Item identifiers

The complete offline catalog is built locally from the user's installed ItemInfo,
ItemGroupInfo and PALOC files. No downloaded item database is bundled.
Read-only archive format and cipher implementation credits and MIT notices are
included in ARCHIVE-NOTICES.txt (Trinity, lazorr410 and LukeFZ).

A small list of factual in-game item identifiers was cross-checked against NattKh's
CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS data. The new offline save helper is built
from that project's pinned MPL-2.0 source as described above.
