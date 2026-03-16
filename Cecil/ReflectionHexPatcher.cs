using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Cecil;

public class ReflectionHexPatcher
{
    private readonly string _assemblyPath;
    private readonly string _namespace;
    private readonly string _typeName;
    private readonly string _methodName;

    public ReflectionHexPatcher(string assemblyPath, string @namespace, string typeName, string methodName)
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

        using var stream = File.OpenRead(_assemblyPath);
        using var peReader = new PEReader(stream);
        var metadataReader = peReader.GetMetadataReader();

        // Find the type
        TypeDefinitionHandle? foundTypeHandle = null;

        foreach (var typeHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeHandle);
            var ns = metadataReader.GetString(typeDef.Namespace);
            var name = metadataReader.GetString(typeDef.Name);

            if (ns == _namespace && name.StartsWith(_typeName))
            {
                Console.WriteLine($"Found type: {ns}.{name}");
                foundTypeHandle = typeHandle;
                break;
            }
        }

        if (foundTypeHandle == null)
        {
            Console.WriteLine($"Could not find {_namespace}.{_typeName}");
            Console.WriteLine($"Available types in {_namespace}:");
            foreach (var typeHandle in metadataReader.TypeDefinitions)
            {
                var typeDef = metadataReader.GetTypeDefinition(typeHandle);
                var ns = metadataReader.GetString(typeDef.Namespace);
                if (ns == _namespace)
                    Console.WriteLine($"  {ns}.{metadataReader.GetString(typeDef.Name)}");
            }
            return null;
        }

        // Find the method
        var targetType = metadataReader.GetTypeDefinition(foundTypeHandle.Value);
        MethodDefinition? foundMethod = null;
        int methodRva = 0;

        foreach (var methodHandle in targetType.GetMethods())
        {
            var methodDef = metadataReader.GetMethodDefinition(methodHandle);
            var name = metadataReader.GetString(methodDef.Name);

            if (name == _methodName)
            {
                Console.WriteLine($"Found method: {name}");
                methodRva = methodDef.RelativeVirtualAddress;
                foundMethod = methodDef;
                break;
            }
        }

        if (foundMethod == null)
        {
            Console.WriteLine($"Could not find method {_methodName}");
            Console.WriteLine("Available methods:");
            foreach (var methodHandle in targetType.GetMethods())
            {
                var methodDef = metadataReader.GetMethodDefinition(methodHandle);
                Console.WriteLine($"  {metadataReader.GetString(methodDef.Name)}");
            }
            return null;
        }

        Console.WriteLine($"Method RVA: 0x{methodRva:X}");

        // Get IL info via PEReader
        var methodBody = peReader.GetMethodBody(methodRva);
        var ilBytes = methodBody.GetILBytes()!;

        Console.WriteLine($"IL size: {ilBytes.Length} bytes");
        Console.WriteLine($"MaxStackSize: {methodBody.MaxStack}");
        Console.WriteLine($"LocalSignature: 0x{(methodBody.LocalSignature.IsNil ? 0 : MetadataTokens.GetToken(methodBody.LocalSignature)):X}");
        Console.WriteLine($"IL bytes: {BitConverter.ToString(ilBytes).Replace("-", " ")}");

        // Resolve file offset
        var sections = peReader.PEHeaders.SectionHeaders;

        foreach (var section in sections)
        {
            if (methodRva >= section.VirtualAddress && methodRva < section.VirtualAddress + section.VirtualSize)
            {
                var methodOffset = methodRva - section.VirtualAddress + section.PointerToRawData;

                stream.Seek(methodOffset, SeekOrigin.Begin);
                var headerByte = (byte)stream.ReadByte();
                bool isTiny = (headerByte & 0x03) == 0x02;
                int ilOffset = isTiny ? methodOffset + 1 : methodOffset + 12;

                var info = new MethodLocation
                {
                    MethodOffset = methodOffset,
                    IlOffset = ilOffset,
                    IlSize = ilBytes.Length,
                    IsTinyHeader = isTiny,
                    OriginalFlags = isTiny ? (byte)0 : headerByte,
                    IlBytes = ilBytes,
                    SectionName = section.Name,
                    SectionVirtualAddress = section.VirtualAddress,
                    SectionPointerToRawData = section.PointerToRawData,
                    Rva = methodRva
                };

                info.Print();
                return info;
            }
        }

        Console.WriteLine("Could not resolve RVA to file offset.");
        return null;
    }

    public void PatchReturnTrue(MethodLocation info, string? outputPath = null)
    {
        var target = outputPath ?? _assemblyPath;

        if (target != _assemblyPath)
            File.Copy(_assemblyPath, target, true);

        using var writer = new BinaryWriter(File.Open(target, FileMode.Open, FileAccess.Write));

        if (info.IsTinyHeader)
        {
            // Tiny header: (codeSize << 2) | 0x02, codeSize=2 => 0x0A
            writer.Seek(info.MethodOffset, SeekOrigin.Begin);
            writer.Write((byte)0x0A);                         // tiny header, code size = 2
            writer.Write((byte)OpCodes.Ldc_I4_1.Value);      // ldc.i4.1 (0x17)
            writer.Write((byte)OpCodes.Ret.Value);            // ret (0x2A)
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
            writer.Write(cleanedFlags);                       // flags byte 0
            writer.Write((byte)0x30);                         // flags byte 1 (header size = 3 dwords)
            writer.Write((ushort)1);                          // MaxStackSize = 1
            writer.Write((int)2);                             // CodeSize = 2
            writer.Write((int)0);                             // LocalVarSigTok = 0
            writer.Write((byte)OpCodes.Ldc_I4_1.Value);      // ldc.i4.1 (0x17)
            writer.Write((byte)OpCodes.Ret.Value);            // ret (0x2A)
        }

        Console.WriteLine();
        Console.WriteLine("Hex-patched successfully!");
        Console.WriteLine($"  Patched file: {target}");
    }
}
