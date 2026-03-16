using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Cecil;

public class ReflectionHexFinder
{
    private readonly string _assemblyPath;
    private readonly string _namespace;
    private readonly string _typeName;
    private readonly string _methodName;

    public ReflectionHexFinder(string assemblyPath, string @namespace, string typeName, string methodName)
    {
        _assemblyPath = assemblyPath;
        _namespace = @namespace;
        _typeName = typeName;
        _methodName = methodName;
    }

    public HexPatcher.MethodInfo? FindMethod()
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

        // Get IL bytes via PEReader
        var methodBody = peReader.GetMethodBody(methodRva);
        var ilBytes = methodBody.GetILBytes()!;

        Console.WriteLine($"IL size: {ilBytes.Length} bytes");
        Console.WriteLine($"MaxStackSize: {methodBody.MaxStack}");
        Console.WriteLine($"LocalSignature: 0x{(methodBody.LocalSignature.IsNil ? 0 : MetadataTokens.GetToken(methodBody.LocalSignature)):X}");
        Console.WriteLine($"IL bytes: {BitConverter.ToString(ilBytes).Replace("-", " ")}");

        // Convert RVA to file offset
        var sections = peReader.PEHeaders.SectionHeaders;

        foreach (var section in sections)
        {
            if (methodRva >= section.VirtualAddress && methodRva < section.VirtualAddress + section.VirtualSize)
            {
                var methodOffset = methodRva - section.VirtualAddress + section.PointerToRawData;

                // Read header byte to determine tiny vs fat
                stream.Seek(methodOffset, SeekOrigin.Begin);
                var headerByte = (byte)stream.ReadByte();
                bool isTiny = (headerByte & 0x03) == 0x02;
                int ilOffset = isTiny ? methodOffset + 1 : methodOffset + 12;

                var info = new HexPatcher.MethodInfo
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

                Console.WriteLine();
                Console.WriteLine($"PE Section: {section.Name}");
                Console.WriteLine($"  Method file offset: 0x{methodOffset:X} ({methodOffset} bytes)");
                Console.WriteLine($"  Method header: {(isTiny ? "Tiny (1 byte)" : "Fat (12 bytes)")}");
                Console.WriteLine($"  IL code offset: 0x{ilOffset:X} ({ilOffset} bytes)");

                return info;
            }
        }

        Console.WriteLine("Could not resolve RVA to file offset.");
        return null;
    }
}
