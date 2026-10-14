# The .keyservant file format

A `.keyservant` file is UTF-8 JSON describing one Windows keyboard layout completely: everything a layout
DLL can contain, plus descriptive metadata. It is the format Keyservant saves, and the one to keep in Git.

Design goals:

- **Lossless** for Windows layouts: importing a layout DLL and exporting it again gives the same tables.
- **Readable diffs**: each key and each dead-key composition is on its own line.
- **Visible characters**: control characters, combining marks, format and private-use characters are
  written as `\uXXXX` escapes; all other characters are written literally.

Comments (`//`, `/* */`) and trailing commas are accepted when reading; Keyservant does not write them.

## Top level

```json
{
  "format": "keyservant-layout",
  "schemaVersion": 1,
  "metadata": { ... },
  "flags": ["altgr"],
  "kbdVersion": 1,
  "keyboardType": 4,
  "keyboardSubType": 0,
  "modifiers": { ... },
  "scanCodes": { ... },
  "keys": [ ... ],
  "deadKeys": [ ... ],
  "keyNames": { ... },
  "keyNamesExt": { ... }
}
```

| Property | Meaning |
| --- | --- |
| `format` | Always `keyservant-layout`. |
| `schemaVersion` | `1`. Readers refuse newer versions they don't know. |
| `flags` | Any of `altgr` (Right Alt acts as AltGr), `shiftlock` (Shift releases Caps Lock), `lrm-rlm` (Shift+Backspace types LRM/RLM). |
| `kbdVersion` | kbd.h `KBD_VERSION`. `1` is required for ligatures. |
| `keyboardType`, `keyboardSubType` | kbd.h `dwType`/`dwSubType`. `4` is a standard 101/102-key keyboard. |

## metadata

```json
"metadata": {
  "name": "German (Austria) + IAST",
  "description": "…",
  "author": "…",
  "company": "…",
  "copyright": "…",
  "version": "1.0",
  "locale": "de-AT",
  "dllName": "kbdiast",
  "layoutId": "A0000C07",
  "license": "MIT"
}
```

- `dllName`: the DLL file name without `.dll`, 1–8 letters, digits or underscores.
- `locale`: BCP 47 tag of the input language the layout is offered under.
- `layoutId`: the KLID assigned on first install. Keyservant writes it back into the file, so the next
  install updates the same layout. Leave it out for new layouts.

## modifiers

```json
"modifiers": {
  "vkToBits": {"SHIFT": "shift", "CONTROL": "ctrl", "MENU": "alt"},
  "columns": ["base", "shift", "ctrl", "shift+ctrl", "altgr", "shift+altgr"],
  "maxModBits": 7
}
```

`columns` lists the shift states that produce characters, in table order. A column selected by several
modifier combinations is written as an array, e.g. `["altgr", "alt"]`; keys store their characters under
the first name.

Shift state names combine `shift`, `ctrl`, `alt`, `altgr` (= `ctrl+alt`), `kana`, `roya`, `loya` and
`grpseltap` with `+`; `base` means no modifier.

## scanCodes

```json
"scanCodes": {
  "count": 128,
  "normal": {"01": "ESCAPE", "02": "1", "36": "RSHIFT|EXT", "47": "HOME|SPECIAL|NUMPAD"},
  "e0": {"1C": "RETURN|EXT", "38": "RMENU|EXT"},
  "e1": {"1D": "PAUSE"}
}
```

Maps hex scan codes to virtual keys. Unlisted scan codes produce nothing. Flags: `EXT`, `MULTIVK`,
`SPECIAL`, `NUMPAD`, `UNICODE`, `INJECTEDVK`, `MAPPEDVK`, `BREAK` (kbd.h `KBDEXT` …).

Virtual key names drop the `VK_` prefix: `A`, `1`, `OEM_1`, `OEM_102`, `DECIMAL`, `SPACE`. Unnamed codes
are written as `0xNN`.

## keys

```json
"keys": [
  {"vk": "A", "attrs": ["capslock"], "base": "a", "shift": "A", "altgr": "ā", "shift+altgr": "Ā"},
  {"vk": "OEM_6", "base": {"dead": "´"}, "shift": {"dead": "`"}},
  {"vk": "X", "attrs": ["capslock"], "base": "x", "shift": "X", "altgr": "ks"},
  {"vk": "OEM_1", "base": "ü", "shift": "è", "caps": {"base": "Ü", "shift": "È"}}
]
```

- Each shift state maps to either a string (the text typed; more than one UTF-16 unit makes it a
  ligature) or `{"dead": "<accent>"}` for a dead key.
- `attrs`: `capslock` (Caps Lock acts like Shift), `capslock-altgr` (also in AltGr states), `kanalock`,
  `grpseltap`.
- `caps`: separate characters while Caps Lock is on (kbd.h `SGCAPS`), used by Swiss and Czech layouts.
  Can't be combined with dead keys in the main row.
- Order matters: Windows' `VkKeyScan` finds the first key producing a character. Numpad digits are kept last.

## deadKeys

```json
"deadKeys": [
  {
    "accent": "̣",
    "name": "DOT BELOW",
    "entries": [
      ["t", "ṭ"],
      ["̄", "ṝ", "chained"]
    ]
  }
]
```

Each entry is `[base, result]`: the dead key followed by `base` types `result`. With `"chained"` the result
is itself a dead key (kbd.h `DKF_DEAD`), whose own entries apply to the next keystroke. `base` and `result`
are single UTF-16 characters. Pressing a dead key followed by a character without an entry types the
accent and then the character.

## keyNames, keyNamesExt

Names Windows shows for keys (`GetKeyNameText`), by hex scan code, for unprefixed and E0-prefixed scan
codes:

```json
"keyNames": {"01": "Esc", "0E": "Backspace", "39": "Space"}
```

## compose (optional)

A compose key and the sequences typed after it. Keyservant compiles them into chained dead keys when the
layout is built, so they work in every app without extra software.

```json
"compose": {
  "key": "E0 5D",
  "accent": "⎄",
  "sequences": [
    [".r", "ṛ"],
    ["--", "–"],
    ["oc", "©"]
  ]
}
```

- `key`: the scan code of the compose key, `E0 5D` (the Menu key) by default; `E0 38` is the right Alt,
  `3A` Caps Lock. Keyservant gives that key a free virtual key that types a dead key.
- `accent`: what Windows shows while it waits for the rest of the sequence (default ⎄ U+2384).
- `sequences`: `[keys, result]`. `keys` are the characters typed after the compose key, as they are typed
  with this layout (so `"A"` means Shift+a). `result` must be a single BMP character. Intermediate steps
  use private-use characters (U+E000–U+EFFF) as hidden dead keys.
- A sequence that is a prefix of another one wins; the longer one is reported and dropped. Keys the layout
  can't type are reported too.

`keyservant compose import` reads `.XCompose` files (Linux, WinCompose) and keeps the `<Multi_key>` lines.

## tests (optional)

Checks that run with `keyservant test`, in the app after every change, and as part of validation (a
failing test is an error, so the layout won't build):

```json
"tests": [
  {"keys": "AltGr+. t", "expect": "ṭ"},
  {"name": "long r", "keys": "AltGr+. AltGr+- r", "expect": "ṝ"},
  {"keys": "Compose . r", "expect": "ṛ"}
]
```

`keys` is a space-separated list of keystrokes. Each is an optional chain of modifiers (`Shift+`, `Ctrl+`,
`Alt+`, `AltGr+`, `Kana+`, `Caps+`) and a key: a character means the key that types it without modifiers
(a Shift-only character such as `A` implies Shift), `PLUS` means the + key, `Compose` the compose key,
and anything else a virtual key name (`OEM_8`, `SPACE`, `NUMPAD1`).
