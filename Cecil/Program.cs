using System.Collections.Frozen;
using Cecil;

// if (args.Length == 0)
// {
//     Console.WriteLine("Usage: Cecil <path-to-dll> [--info] [--hex-patch]");
//     return;
// }

args = ["F:\\src\\masstransit-crack\\Masstransit.Samples\\Publisher\\bin\\Debug\\net10.0\\MassTransit.dll"];

var assemblyPath = args[0];
var infoOnly = args.Any(a => a == "--info");
//var hexPatch = args.Any(a => a == "--hex-patch");


var hexPatch = true;

var patcher = new HexPatcher(
    assemblyPath,
    @namespace: "MassTransit.Configuration",
    typeName: "BaseHostConfiguration",
    methodName: "IsRunningInTestEnvironment"
);

var reflectionFinder = new ReflectionHexFinder( assemblyPath,  "MassTransit.Configuration", "BaseHostConfiguration","IsRunningInTestEnvironment");
var method1 = reflectionFinder.FindMethod();

var methodInfo = patcher.FindMethod();
patcher.PatchReturnTrue(methodInfo);

if (methodInfo == null)
    return;

if (infoOnly)
{
    Console.WriteLine();
    Console.WriteLine("Info-only mode, no changes made.");
    return;
}

if (hexPatch)
{
    var backup = assemblyPath + "_backup";
    File.Copy(assemblyPath, backup, true);
    patcher.PatchReturnTrue(methodInfo);
}
else
{
    // Cecil-based patching
    var backup = assemblyPath + "_backup";
    File.Copy(assemblyPath, backup, true);

    using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(backup, new Mono.Cecil.ReaderParameters
    {
        ReadingMode = Mono.Cecil.ReadingMode.Immediate,
        AssemblyResolver = new Mono.Cecil.DefaultAssemblyResolver()
    });

    var targetType = assembly.MainModule.Types
        .First(t => t.Namespace == "MassTransit.Configuration"
                     && t.Name.StartsWith("BaseHostConfiguration"));

    var method = targetType.Methods.First(m => m.Name == "IsRunningInTestEnvironment");

    var il = method.Body.GetILProcessor();
    method.Body.Instructions.Clear();
    method.Body.Variables.Clear();
    method.Body.ExceptionHandlers.Clear();

    il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_1));
    il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
    method.Body.MaxStackSize = 1;

    Console.WriteLine();
    Console.WriteLine("Patched IL:");
    foreach (var instruction in method.Body.Instructions)
        Console.WriteLine($"  {instruction}");

    assembly.Write(assemblyPath);
    Console.WriteLine();
    Console.WriteLine($"Patched assembly saved to: {assemblyPath}");
}
