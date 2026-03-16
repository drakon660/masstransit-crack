using System.Reflection.PortableExecutable;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cecil;

public class CecilPatcher
{
    private readonly string _assemblyPath;
    private readonly string _namespace;
    private readonly string _typeName;
    private readonly string _methodName;

    public CecilPatcher(string assemblyPath, string @namespace, string typeName, string methodName)
    {
        _assemblyPath = assemblyPath;
        _namespace = @namespace;
        _typeName = typeName;
        _methodName = methodName;
    }

    public MethodLocation? FindMethod()
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

        return ResolveFileOffset(_assemblyPath, method.RVA);
    }

    public void Patch(string? outputPath = null)
    {
        var target = outputPath ?? _assemblyPath;
        var backup = _assemblyPath + "_backup";
        File.Copy(_assemblyPath, backup, true);

        var assemblyDirectory = Path.GetDirectoryName(backup)!;
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(assemblyDirectory);

        using var assembly = AssemblyDefinition.ReadAssembly(backup, new ReaderParameters
        {
            ReadingMode = ReadingMode.Immediate,
            AssemblyResolver = resolver
        });

        var targetType = assembly.MainModule.Types
            .First(t => t.Namespace == _namespace && t.Name.StartsWith(_typeName));

        var method = targetType.Methods.First(m => m.Name == _methodName);

        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        method.Body.Variables.Clear();
        method.Body.ExceptionHandlers.Clear();

        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Ret));
        method.Body.MaxStackSize = 1;

        Console.WriteLine();
        Console.WriteLine("Patched IL:");
        foreach (var instruction in method.Body.Instructions)
            Console.WriteLine($"  {instruction}");

        assembly.Write(target);
        Console.WriteLine($"Patched assembly saved to: {target}");
    }

    internal static MethodLocation? ResolveFileOffset(string assemblyPath, int rva)
    {
        using var peStream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(peStream);
        var sections = peReader.PEHeaders.SectionHeaders;

        foreach (var section in sections)
        {
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.VirtualSize)
            {
                var methodOffset = rva - section.VirtualAddress + section.PointerToRawData;

                peStream.Seek(methodOffset, SeekOrigin.Begin);
                var firstByte = (byte)peStream.ReadByte();
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

                peStream.Seek(ilOffset, SeekOrigin.Begin);
                var ilBytes = new byte[ilSize];
                peStream.ReadExactly(ilBytes);

                var info = new MethodLocation
                {
                    MethodOffset = methodOffset,
                    IlOffset = ilOffset,
                    IlSize = ilSize,
                    IsTinyHeader = isTiny,
                    OriginalFlags = isTiny ? (byte)0 : firstByte,
                    IlBytes = ilBytes,
                    SectionName = section.Name,
                    SectionVirtualAddress = section.VirtualAddress,
                    SectionPointerToRawData = section.PointerToRawData,
                    Rva = rva
                };

                info.Print();
                return info;
            }
        }

        Console.WriteLine("Could not resolve RVA to file offset.");
        return null;
    }
}
