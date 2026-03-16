namespace Cecil;

public class MethodLocation
{
    public int MethodOffset { get; init; }
    public int IlOffset { get; init; }
    public int IlSize { get; init; }
    public bool IsTinyHeader { get; init; }
    public byte OriginalFlags { get; init; }
    public byte[] IlBytes { get; init; } = [];
    public string SectionName { get; init; } = "";
    public int SectionVirtualAddress { get; init; }
    public int SectionPointerToRawData { get; init; }
    public int Rva { get; init; }

    public void Print()
    {
        Console.WriteLine();
        Console.WriteLine($"PE Section: {SectionName}");
        Console.WriteLine($"  Section VirtualAddress: 0x{SectionVirtualAddress:X}");
        Console.WriteLine($"  Section PointerToRawData: 0x{SectionPointerToRawData:X}");
        Console.WriteLine($"  Method RVA: 0x{Rva:X}");
        Console.WriteLine($"  Method file offset: 0x{MethodOffset:X} ({MethodOffset} bytes)");
        Console.WriteLine($"  Method header: {(IsTinyHeader ? "Tiny (1 byte)" : "Fat (12 bytes)")}");
        Console.WriteLine($"  IL code offset: 0x{IlOffset:X} ({IlOffset} bytes)");
        Console.WriteLine($"  IL code size: {IlSize} bytes");
        Console.WriteLine($"  IL bytes: {BitConverter.ToString(IlBytes).Replace("-", " ")}");
    }
}
