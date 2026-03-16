using Cecil;

args =
[
    @"F:\src\masstransit-crack\Masstransit.Samples\Publisher\bin\Debug\net10.0\MassTransit.dll",
    @"F:\src\masstransit-crack\Masstransit.Samples\Consumer\bin\Debug\net10.0\MassTransit.dll"
];

foreach (var assemblyPath in args)
{
    Console.WriteLine($"=== Processing: {assemblyPath} ===");
    Console.WriteLine();

    var patcher = new ReflectionHexPatcher(
        assemblyPath,
        @namespace: "MassTransit.Configuration",
        typeName: "BaseHostConfiguration",
        methodName: "IsRunningInTestEnvironment"
    );

    var location = patcher.FindMethod();

    if (location == null)
    {
        Console.WriteLine($"Skipping: {assemblyPath}");
        Console.WriteLine();
        continue;
    }

    patcher.PatchReturnTrue(location);
    Console.WriteLine();
}
