using System.Reflection.PortableExecutable;
using Mono.Cecil;

namespace Cecil;

public class HexPatcher
{
    private readonly string _assemblyPath;
    private readonly string _namespace;
    private readonly string _typeName;
    private readonly string _methodName;

    public HexPatcher(string assemblyPath, string @namespace, string typeName, string methodName)
    {
        _assemblyPath = assemblyPath;
        _namespace = @namespace;
        _typeName = typeName;
        _methodName = methodName;
    }

    public MethodInfo? FindMethod()
    {
        if (!File.Exists(_assemblyPath))
        {
            Console.WriteLine($"File not found: {_assemblyPath}");
            return null;
        }

        var header = new byte[2];
        using (var fs = File.OpenRead(_assemblyPath))
            fs.ReadExactly(header);

        if (header[0] != 0x4D || header[1] != 0x5A)
        {
            Console.WriteLine("Error: File is not a valid PE (MZ) executable.");
            return null;
        }

        var assemblyDirectory = Path.GetDirectoryName(_assemblyPath)!;
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(assemblyDirectory);

        using var assembly = AssemblyDefinition.ReadAssembly(_assemblyPath, new ReaderParameters
        {
            ReadingMode = ReadingMode.Immediate,
            AssemblyResolver = resolver
        });

        Console.WriteLine($"Assembly: {assembly.FullName}");
        Console.WriteLine($"Runtime:  {assembly.MainModule.RuntimeVersion}");
        Console.WriteLine($"File size: {new FileInfo(_assemblyPath).Length} bytes");

        var targetType = assembly.MainModule.Types
            .FirstOrDefault(t => t.Namespace == _namespace && t.Name.StartsWith(_typeName));

        if (targetType == null)
        {
            Console.WriteLine($"Could not find {_namespace}.{_typeName} type.");
            Console.WriteLine($"Available types in {_namespace}:");
            foreach (var t in assembly.MainModule.Types.Where(t => t.Namespace == _namespace))
                Console.WriteLine($"  {t.FullName}");
            return null;
        }

        var method = targetType.Methods.FirstOrDefault(m => m.Name == _methodName);

        if (method == null)
        {
            Console.WriteLine($"Could not find {_methodName} method.");
            Console.WriteLine("Available methods:");
            foreach (var m in targetType.Methods)
                Console.WriteLine($"  {m.Name}");
            return null;
        }

        Console.WriteLine($"Found method: {method.FullName}");
        Console.WriteLine($"Method RVA: 0x{method.RVA:X}");
        Console.WriteLine($"Original IL ({method.Body.Instructions.Count} instructions):");

        foreach (var instruction in method.Body.Instructions)
            Console.WriteLine($"  {instruction}");

        // Resolve file offset from RVA
        using var peStream = File.OpenRead(_assemblyPath);
        using var peReader = new PEReader(peStream);
        var sections = peReader.PEHeaders.SectionHeaders;
        int rva = method.RVA;

        foreach (var section in sections)
        {
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.VirtualSize)
            {
                var methodOffset = rva - section.VirtualAddress + section.PointerToRawData;

                peStream.Seek(methodOffset, SeekOrigin.Begin);
                var firstByte = peStream.ReadByte();
                bool isTiny = (firstByte & 0x03) == 0x02;

                int ilOffset;
                int ilSize;

                if (isTiny)
                {
                    ilSize = firstByte >> 2;
                    ilOffset = methodOffset + 1;
                }
                else
                {
                    peStream.Seek(methodOffset + 4, SeekOrigin.Begin);
                    var sizeBuf = new byte[4];
                    peStream.ReadExactly(sizeBuf);
                    ilSize = BitConverter.ToInt32(sizeBuf);
                    ilOffset = methodOffset + 12;
                }

                // Read raw IL bytes
                peStream.Seek(ilOffset, SeekOrigin.Begin);
                var ilBytes = new byte[ilSize];
                peStream.ReadExactly(ilBytes);

                // Read original fat header flags
                byte originalFlags = 0;
                if (!isTiny)
                {
                    peStream.Seek(methodOffset, SeekOrigin.Begin);
                    originalFlags = (byte)peStream.ReadByte();
                }

                var info = new MethodInfo
                {
                    MethodOffset = methodOffset,
                    IlOffset = ilOffset,
                    IlSize = ilSize,
                    IsTinyHeader = isTiny,
                    OriginalFlags = originalFlags,
                    IlBytes = ilBytes,
                    SectionName = section.Name,
                    SectionVirtualAddress = section.VirtualAddress,
                    SectionPointerToRawData = section.PointerToRawData,
                    Rva = rva
                };

                PrintMethodInfo(info);
                return info;
            }
        }

        Console.WriteLine("Could not resolve RVA to file offset.");
        return null;
    }

    public void PatchReturnTrue(MethodInfo info, string? outputPath = null)
    {
        var target = outputPath ?? _assemblyPath;

        if (target != _assemblyPath)
            File.Copy(_assemblyPath, target, true);

        using var writer = new BinaryWriter(File.Open(target, FileMode.Open, FileAccess.Write));

        if (info.IsTinyHeader)
        {
            // Tiny header: (codeSize << 2) | 0x02, codeSize=2 => 0x0A
            writer.Seek(info.MethodOffset, SeekOrigin.Begin);
            writer.Write((byte)0x0A);  // tiny header, code size = 2
            writer.Write((byte)0x17);  // ldc.i4.1
            writer.Write((byte)0x2A);  // ret
        }
        else
        {
            // Fat header layout:
            //   byte 0: lower 2 bits = format (0x3=fat), bit 2 = MoreSects, bit 3 = InitLocals
            //   byte 1: upper nibble = header size in dwords (0x30 = 12 bytes)
            //   bytes 2-3: MaxStackSize
            //   bytes 4-7: CodeSize
            //   bytes 8-11: LocalVarSigTok
            //   bytes 12+: IL body

            // Clear MoreSects (bit 2) and InitLocals (bit 3), keep fat format (0x03)
            byte cleanedFlags = (byte)((info.OriginalFlags & ~0x0C) | 0x03);

            writer.Seek(info.MethodOffset, SeekOrigin.Begin);
            writer.Write(cleanedFlags);        // flags byte 0
            writer.Write((byte)0x30);          // flags byte 1 (header size = 3 dwords)
            writer.Write((ushort)1);           // MaxStackSize = 1
            writer.Write((int)2);              // CodeSize = 2
            writer.Write((int)0);              // LocalVarSigTok = 0
            writer.Write((byte)0x17);          // ldc.i4.1
            writer.Write((byte)0x2A);          // ret
        }

        Console.WriteLine();
        Console.WriteLine("Hex-patched successfully!");
        Console.WriteLine($"  Patched file: {target}");
    }

    private static void PrintMethodInfo(MethodInfo info)
    {
        Console.WriteLine();
        Console.WriteLine($"PE Section: {info.SectionName}");
        Console.WriteLine($"  Section VirtualAddress: 0x{info.SectionVirtualAddress:X}");
        Console.WriteLine($"  Section PointerToRawData: 0x{info.SectionPointerToRawData:X}");
        Console.WriteLine($"  Method file offset: 0x{info.MethodOffset:X} ({info.MethodOffset} bytes)");
        Console.WriteLine($"  Method header: {(info.IsTinyHeader ? "Tiny (1 byte)" : "Fat (12 bytes)")}");
        Console.WriteLine($"  IL code offset: 0x{info.IlOffset:X} ({info.IlOffset} bytes)");
        Console.WriteLine($"  IL code size: {info.IlSize} bytes");
        Console.WriteLine($"  IL bytes: {BitConverter.ToString(info.IlBytes).Replace("-", " ")}");
    }

    public class MethodInfo
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
    }
}
