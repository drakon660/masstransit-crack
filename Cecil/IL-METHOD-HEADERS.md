# Nagłówki metod IL w .NET

## Struktura PE → Metoda IL

Plik .NET DLL to plik PE (Portable Executable) z metadanymi CLI.
Ścieżka od pliku do instrukcji IL wygląda tak:

```
Plik DLL (PE)
  → DOS Header ("MZ")
    → PE Header
      → Section Table
        → .text section
          → CLI Header
            → Metadata
              → MethodDef table
                → RVA metody → Nagłówek + Ciało IL
```

### Konwersja RVA na offset w pliku

RVA (Relative Virtual Address) to adres w pamięci po załadowaniu DLL.
Aby znaleźć metodę w pliku na dysku, trzeba przeliczyć RVA na offset:

```
FileOffset = RVA - Section.VirtualAddress + Section.PointerToRawData
```

---

## Tiny Header (mały nagłówek, 1 bajt)

Używany gdy metoda jest prosta i spełnia **wszystkie** warunki:
- Kod IL < 64 bajty
- Max stos ≤ 8
- Brak zmiennych lokalnych
- Brak try/catch/finally

### Format

```
Jeden bajt: [ cccccc | 10 ]
              ^^^^^^   ^^
              rozmiar  format = 0x02 (tiny)
              kodu
              (6 bitów)
```

Rozmiar kodu jest zakodowany w górnych 6 bitach tego samego bajtu.

### Przykłady wartości

| Bajt   | Binarnie   | Rozmiar kodu |
|--------|------------|-------------|
| `0x0A` | `00001010` | 2 bajty     |
| `0x22` | `00100010` | 8 bajtów    |
| `0xFA` | `11111010` | 62 bajty (max) |

### Layout w pliku

```
┌──────────┬──────────────────┐
│ 0x0A     │ 17 2A            │
│ nagłówek │ ldc.i4.1  ret    │
│ (1 bajt) │ ciało IL         │
└──────────┴──────────────────┘
  razem: 3 bajty
```

---

## Fat Header (duży nagłówek, 12 bajtów)

Używany gdy metoda jest złożona — spełnia **którykolwiek** z warunków:
- Kod IL ≥ 64 bajty
- Max stos > 8
- Ma zmienne lokalne
- Ma obsługę wyjątków (try/catch/finally)

### Format

```
Bajt 0:     Flagi (dolny nibble)
              bit 0-1: format = 0x3 (fat)
              bit 2:   MoreSects (1 = po IL są sekcje z wyjątkami)
              bit 3:   InitLocals (1 = zeruj zmienne lokalne na starcie)

Bajt 1:     Rozmiar nagłówka w DWORD-ach (górny nibble)
              zawsze 0x30 = 3 × 4 = 12 bajtów

Bajty 2-3:  MaxStackSize (uint16)
              ile wartości może być jednocześnie na stosie ewaluacji

Bajty 4-7:  CodeSize (uint32)
              rozmiar ciała IL w bajtach

Bajty 8-11: LocalVarSigTok (uint32)
              token do sygnatury zmiennych lokalnych
              0x00000000 = brak zmiennych
```

### Layout w pliku

```
┌────────────────────────────────────────────────────┬──────────────┐
│  Fat Header (12 bajtów)                            │  Ciało IL    │
│  flagi  rozm  maxstos  rozm.kodu   zm.lokalne     │  (N bajtów)  │
│  [13]   [30]  [04 00]  [EE000000]  [01001100]     │  [73 21 ...] │
└────────────────────────────────────────────────────┴──────────────┘
  razem: 12 + N bajtów
```

### Dekodowanie flagi (bajt 0)

```
Przykład: 0x13 = 0001 0011
                  │       └┘── format = 0x3 (fat)
                  │      └──── MoreSects = 0 (brak sekcji wyjątków)
                  └───────── InitLocals = 1 (zeruj zmienne lokalne)
```

| Wartość | Binarnie   | Format | MoreSects | InitLocals |
|---------|------------|--------|-----------|------------|
| `0x03`  | `00000011` | fat    | nie       | nie        |
| `0x07`  | `00000111` | fat    | tak       | nie        |
| `0x13`  | `00010011` | fat    | nie       | tak        |
| `0x1B`  | `00011011` | fat    | tak       | tak        |

---

## Porównanie Tiny vs Fat

| Cecha              | Tiny       | Fat        |
|--------------------|------------|------------|
| Rozmiar nagłówka   | 1 bajt     | 12 bajtów  |
| Max rozmiar kodu   | 63 bajty   | 4 GB       |
| Max stos           | 8          | 65535      |
| Zmienne lokalne    | nie        | tak        |
| Try/catch/finally  | nie        | tak        |
| Bit formatu        | `0x02`     | `0x03`     |

---

## MoreSects — sekcje dodatkowe (tabela wyjątków)

Gdy bit 2 flagi Fat = 1, po ciele IL (wyrównanym do 4 bajtów) znajduje się tabela wyjątków:

```
┌────────────┬──────────────┬─────────────────────┐
│ Fat Header │ Ciało IL     │ Tabela wyjątków     │
│ (12 bajtów)│ (N bajtów)   │ (try/catch/finally) │
│            │              │ ← tylko gdy          │
│            │              │   MoreSects = 1      │
└────────────┴──────────────┴─────────────────────┘
                             ^
                             pozycja = ilOffset + CodeSize
                             (wyrównana do granicy 4 bajtów)
```

Runtime oblicza pozycję tabeli wyjątków jako `ilOffset + CodeSize` (z wyrównaniem do 4 bajtów).
Jeśli zmienisz `CodeSize` ale zostawisz `MoreSects = 1`, runtime będzie szukał tabeli pod złym adresem — crash.

---

## Przykład patchowania: `return true`

### Przed (oryginał, 238 bajtów IL)

```
0x921A4: [13 30] [04 00] [EE 00 00 00] [xx xx xx xx] [73 21 36 ...]
          │  │    │       │              │              └── ciało IL (238 bajtów)
          │  │    │       │              └── LocalVarSigTok (token zmiennych)
          │  │    │       └── CodeSize = 238
          │  │    └── MaxStackSize = 4
          │  └── rozmiar nagłówka = 12 bajtów
          └── flagi: fat + InitLocals
```

### Po (spatchowany, 2 bajty IL)

```
0x921A4: [03 30] [01 00] [02 00 00 00] [00 00 00 00] [17 2A]
          │  │    │       │              │              │  └── ret
          │  │    │       │              │              └── ldc.i4.1
          │  │    │       │              └── brak zmiennych lokalnych
          │  │    │       └── CodeSize = 2
          │  │    └── MaxStackSize = 1
          │  └── rozmiar nagłówka = 12 bajtów (bez zmian)
          └── flagi: fat (wyczyszczono InitLocals i MoreSects)
```

### Co się zmieniło

| Pole           | Przed        | Po           | Dlaczego                                                  |
|----------------|--------------|--------------|-----------------------------------------------------------|
| Flagi (bajt 0) | `0x13`       | `0x03`       | Usunięto InitLocals (brak zmiennych) i MoreSects (brak wyjątków) |
| MaxStack       | `4`          | `1`          | Potrzebujemy tylko 1 miejsca na stosie (dla wartości `true`)  |
| CodeSize       | `238`        | `2`          | Dwie instrukcje zamiast 238 bajtów logiki                 |
| LocalVarSigTok | token        | `0`          | Nie potrzebujemy zmiennych lokalnych                      |
| Ciało IL       | 238 bajtów   | `17 2A`      | `ldc.i4.1` (wrzuć true) + `ret` (zwróć)                  |

### Dlaczego sam `17 2A` nie wystarczy

Runtime czyta `CodeSize` z nagłówka i próbuje skompilować (JIT) wszystkie bajty o podanym rozmiarze.
Jeśli zmienisz tylko pierwsze 2 bajty IL na `17 2A`, ale `CodeSize` nadal wynosi 238, JIT skompiluje
pozostałe 236 bajtów — które teraz są bezsensownym kodem po instrukji `ret`. Skutek: `InvalidProgramException` lub `BadImageFormatException`.

---

## Popularne opcody IL

| Opcode        | Hex    | Opis                                    |
|---------------|--------|-----------------------------------------|
| `nop`         | `0x00` | Nic nie rób                             |
| `ret`         | `0x2A` | Zwróć wartość ze stosu                  |
| `ldc.i4.0`    | `0x16` | Wrzuć 0 (false) na stos                |
| `ldc.i4.1`    | `0x17` | Wrzuć 1 (true) na stos                 |
| `ldc.i4.2`    | `0x18` | Wrzuć 2 na stos                        |
| `ldc.i4.s N`  | `0x1F` | Wrzuć N na stos (1 bajt argumentu)     |
| `ldc.i4 N`    | `0x20` | Wrzuć N na stos (4 bajty argumentu)    |
| `ldnull`      | `0x14` | Wrzuć null na stos                     |
| `ldarg.0`     | `0x02` | Wrzuć argument 0 (this) na stos        |
| `call`        | `0x28` | Wywołaj metodę (4 bajty tokenu)        |
| `callvirt`    | `0x6F` | Wywołaj metodę wirtualną               |
| `newobj`      | `0x73` | Utwórz nowy obiekt                     |
| `stloc.0`     | `0x0A` | Zapisz do zmiennej lokalnej 0          |
| `ldloc.0`     | `0x06` | Wczytaj zmienną lokalną 0              |

W .NET IL nie ma typu `bool` na stosie — to zawsze `int32`. Wartość `0` = false, każda inna = true.
