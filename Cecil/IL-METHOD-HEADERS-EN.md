# .NET IL Method Headers

## PE Structure → IL Method

A .NET DLL is a PE (Portable Executable) file with CLI metadata.
The path from the file to IL instructions looks like this:

```
DLL File (PE)
  → DOS Header ("MZ")
    → PE Header
      → Section Table
        → .text section
          → CLI Header
            → Metadata
              → MethodDef table
                → Method RVA → Header + IL Body
```

### Converting RVA to File Offset

RVA (Relative Virtual Address) is the address in memory after the DLL is loaded.
To find the method in the file on disk, you need to convert the RVA to a file offset:

```
FileOffset = RVA - Section.VirtualAddress + Section.PointerToRawData
```

---

## Tiny Header (1 byte)

Used when the method is simple and meets **all** of the following conditions:
- IL code size < 64 bytes
- Max stack ≤ 8
- No local variables
- No exception handlers (try/catch/finally)

### Format

```
Single byte: [ cccccc | 10 ]
               ^^^^^^   ^^
               code     format = 0x02 (tiny)
               size
               (6 bits)
```

The code size is encoded in the upper 6 bits of the same byte.

### Example Values

| Byte   | Binary     | Code Size      |
|--------|------------|----------------|
| `0x0A` | `00001010` | 2 bytes        |
| `0x22` | `00100010` | 8 bytes        |
| `0xFA` | `11111010` | 62 bytes (max) |

### File Layout

```
┌──────────┬──────────────────┐
│ 0x0A     │ 17 2A            │
│ header   │ ldc.i4.1  ret    │
│ (1 byte) │ IL body          │
└──────────┴──────────────────┘
  total: 3 bytes
```

---

## Fat Header (12 bytes)

Used when the method is complex — meets **any** of the following conditions:
- IL code size ≥ 64 bytes
- Max stack > 8
- Has local variables
- Has exception handlers (try/catch/finally)

### Format

```
Byte 0:     Flags (lower nibble)
              bit 0-1: format = 0x3 (fat)
              bit 2:   MoreSects (1 = exception handler data follows IL body)
              bit 3:   InitLocals (1 = zero-initialize local variables on entry)

Byte 1:     Header size in DWORDs (upper nibble)
              always 0x30 = 3 × 4 = 12 bytes

Bytes 2-3:  MaxStackSize (uint16)
              maximum number of items on the evaluation stack at any point

Bytes 4-7:  CodeSize (uint32)
              size of the IL body in bytes

Bytes 8-11: LocalVarSigTok (uint32)
              metadata token for the local variable signature
              0x00000000 = no local variables
```

### File Layout

```
┌────────────────────────────────────────────────────┬──────────────┐
│  Fat Header (12 bytes)                             │  IL Body     │
│  flags  size  maxstack  codesize    localvarsig    │  (N bytes)   │
│  [13]   [30]  [04 00]   [EE000000]  [01001100]    │  [73 21 ...] │
└────────────────────────────────────────────────────┴──────────────┘
  total: 12 + N bytes
```

### Decoding the Flags (byte 0)

```
Example: 0x13 = 0001 0011
                 │       └┘── format = 0x3 (fat)
                 │      └──── MoreSects = 0 (no exception sections)
                 └───────── InitLocals = 1 (zero-init local variables)
```

| Value  | Binary     | Format | MoreSects | InitLocals |
|--------|------------|--------|-----------|------------|
| `0x03` | `00000011` | fat    | no        | no         |
| `0x07` | `00000111` | fat    | yes       | no         |
| `0x13` | `00010011` | fat    | no        | yes        |
| `0x1B` | `00011011` | fat    | yes       | yes        |

---

## Tiny vs Fat Comparison

| Feature            | Tiny       | Fat        |
|--------------------|------------|------------|
| Header size        | 1 byte     | 12 bytes   |
| Max code size      | 63 bytes   | 4 GB       |
| Max stack          | 8          | 65535      |
| Local variables    | no         | yes        |
| Try/catch/finally  | no         | yes        |
| Format bit         | `0x02`     | `0x03`     |

---

## MoreSects — Extra Data Sections (Exception Handler Table)

When bit 2 of the Fat flags is set, the exception handler table follows the IL body (aligned to a 4-byte boundary):

```
┌────────────┬──────────────┬──────────────────────────┐
│ Fat Header │ IL Body      │ Exception Handler Table   │
│ (12 bytes) │ (N bytes)    │ (try/catch/finally)       │
│            │              │ ← only present when       │
│            │              │   MoreSects = 1           │
└────────────┴──────────────┴──────────────────────────┘
                             ^
                             position = ilOffset + CodeSize
                             (aligned to 4-byte boundary)
```

The runtime calculates the exception table position as `ilOffset + CodeSize` (with 4-byte alignment).
If you change `CodeSize` but leave `MoreSects = 1`, the runtime will look for the table at the wrong offset — crash.

---

## Patching Example: `return true`

### Before (original, 238 bytes of IL)

```
0x921A4: [13 30] [04 00] [EE 00 00 00] [xx xx xx xx] [73 21 36 ...]
          │  │    │       │              │              └── IL body (238 bytes)
          │  │    │       │              └── LocalVarSigTok (local variables token)
          │  │    │       └── CodeSize = 238
          │  │    └── MaxStackSize = 4
          │  └── header size = 12 bytes
          └── flags: fat + InitLocals
```

### After (patched, 2 bytes of IL)

```
0x921A4: [03 30] [01 00] [02 00 00 00] [00 00 00 00] [17 2A]
          │  │    │       │              │              │  └── ret
          │  │    │       │              │              └── ldc.i4.1
          │  │    │       │              └── no local variables
          │  │    │       └── CodeSize = 2
          │  │    └── MaxStackSize = 1
          │  └── header size = 12 bytes (unchanged)
          └── flags: fat (cleared InitLocals and MoreSects)
```

### What Changed

| Field          | Before       | After        | Why                                                              |
|----------------|--------------|--------------|------------------------------------------------------------------|
| Flags (byte 0) | `0x13`       | `0x03`       | Cleared InitLocals (no variables) and MoreSects (no exceptions)  |
| MaxStack       | `4`          | `1`          | Only need 1 stack slot (for the `true` value)                    |
| CodeSize       | `238`        | `2`          | Two instructions instead of 238 bytes of logic                   |
| LocalVarSigTok | token        | `0`          | No local variables needed                                        |
| IL Body        | 238 bytes    | `17 2A`      | `ldc.i4.1` (push true) + `ret` (return)                         |

### Why Just Patching `17 2A` Is Not Enough

The runtime reads `CodeSize` from the header and attempts to JIT-compile all bytes of the specified size.
If you only change the first 2 bytes of IL to `17 2A` but `CodeSize` still says 238, the JIT compiler
will try to compile the remaining 236 bytes — which are now meaningless code after the `ret` instruction.
Result: `InvalidProgramException` or `BadImageFormatException`.

---

## Common IL Opcodes

| Opcode        | Hex    | Description                              |
|---------------|--------|------------------------------------------|
| `nop`         | `0x00` | No operation                             |
| `ret`         | `0x2A` | Return value from the stack              |
| `ldc.i4.0`    | `0x16` | Push 0 (false) onto the stack            |
| `ldc.i4.1`    | `0x17` | Push 1 (true) onto the stack             |
| `ldc.i4.2`    | `0x18` | Push 2 onto the stack                    |
| `ldc.i4.s N`  | `0x1F` | Push N onto the stack (1-byte argument)  |
| `ldc.i4 N`    | `0x20` | Push N onto the stack (4-byte argument)  |
| `ldnull`      | `0x14` | Push null onto the stack                 |
| `ldarg.0`     | `0x02` | Push argument 0 (this) onto the stack    |
| `call`        | `0x28` | Call a method (4-byte token)             |
| `callvirt`    | `0x6F` | Call a virtual method                    |
| `newobj`      | `0x73` | Create a new object                      |
| `stloc.0`     | `0x0A` | Store to local variable 0                |
| `ldloc.0`     | `0x06` | Load local variable 0                    |

In .NET IL there is no `bool` type on the stack — it is always `int32`. Value `0` = false, any non-zero = true.
