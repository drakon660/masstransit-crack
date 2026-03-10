using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length == 0)
{
    Console.WriteLine("Usage: Cecil <path-to-dll>");
    return;
}

var assemblyPath = args[0];

if (!File.Exists(assemblyPath))
{
    Console.WriteLine($"File not found: {assemblyPath}");
    return;
}

// Verify the file starts with MZ (valid PE header)
var header = new byte[2];
using (var fs = File.OpenRead(assemblyPath))
    fs.Read(header, 0, 2);

if (header[0] != 0x4D || header[1] != 0x5A)
{
    Console.WriteLine("Error: File is not a valid PE (MZ) executable.");
    return;
}

Console.WriteLine($"File size: {new FileInfo(assemblyPath).Length} bytes");

var outputPath = Path.Combine(
    Path.GetDirectoryName(assemblyPath)!,
    Path.GetFileNameWithoutExtension(assemblyPath) + "_patched" + Path.GetExtension(assemblyPath));

using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters
{
    ReadingMode = ReadingMode.Immediate
});

Console.WriteLine($"Assembly: {assembly.FullName}");
Console.WriteLine($"Runtime:  {assembly.MainModule.RuntimeVersion}");

var targetType = assembly.MainModule.Types
    .FirstOrDefault(t => t.Namespace == "MassTransit.Configuration"
                         && t.Name.StartsWith("BaseHostConfiguration"));

if (targetType == null)
{
    Console.WriteLine("Could not find BaseHostConfiguration type.");
    Console.WriteLine("Available types in MassTransit.Configuration:");
    foreach (var t in assembly.MainModule.Types.Where(t => t.Namespace == "MassTransit.Configuration"))
        Console.WriteLine($"  {t.FullName}");
    return;
}

Console.WriteLine($"Found type: {targetType.FullName}");

var method = targetType.Methods
    .FirstOrDefault(m => m.Name == "IsRunningInTestEnvironment");

if (method == null)
{
    Console.WriteLine("Could not find IsRunningInTestEnvironment method.");
    Console.WriteLine("Available methods:");
    foreach (var m in targetType.Methods)
        Console.WriteLine($"  {m.Name}");
    return;
}

Console.WriteLine($"Found method: {method.FullName}");
Console.WriteLine($"Original IL ({method.Body.Instructions.Count} instructions):");

foreach (var instruction in method.Body.Instructions)
    Console.WriteLine($"  {instruction}");

// Replace the method body to simply: return true;
var il = method.Body.GetILProcessor();
method.Body.Instructions.Clear();
method.Body.Variables.Clear();
method.Body.ExceptionHandlers.Clear();

il.Append(il.Create(OpCodes.Ldc_I4_1)); // push 1 (true) onto stack
il.Append(il.Create(OpCodes.Ret));       // return

method.Body.MaxStackSize = 1;

Console.WriteLine();
Console.WriteLine("Patched IL:");
foreach (var instruction in method.Body.Instructions)
    Console.WriteLine($"  {instruction}");

assembly.Write(outputPath);
Console.WriteLine();
Console.WriteLine($"Patched assembly saved to: {outputPath}");
Console.WriteLine($"Replace the original with: copy \"{outputPath}\" \"{assemblyPath}\"");
