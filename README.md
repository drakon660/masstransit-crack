# masstransit-crack

A .NET CLI tool that patches [MassTransit](https://masstransit.io/) assemblies using [Mono.Cecil](https://github.com/jbevain/cecil) to modify IL (Intermediate Language) at the bytecode level.

Specifically, it rewrites the `BaseHostConfiguration.IsRunningInTestEnvironment()` method to always return `true`, bypassing environment detection logic without requiring source code changes.

## How It Works

1. Validates the target DLL (existence check + PE header verification)
2. Loads the assembly with Mono.Cecil
3. Locates `MassTransit.Configuration.BaseHostConfiguration`
4. Finds the `IsRunningInTestEnvironment()` method
5. Replaces the original IL body with:
   ```
   ldc.i4.1   // push true
   ret         // return
   ```
6. Writes the patched assembly as `{original}_patched.dll`

## Requirements

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

## Usage

```bash
cd Cecil
dotnet run -- <path-to-MassTransit.dll>
```

The tool outputs diagnostic information at each step — assembly metadata, original IL instructions, patched IL, and the output file path.

After patching, replace the original DLL:

```bash
copy MassTransit_patched.dll MassTransit.dll
```

## Tech Stack

| Component | Details |
|-----------|---------|
| Runtime | .NET 10.0 |
| IL Manipulation | Mono.Cecil 0.11.6 |
| Output | Console application |

## Project Structure

```
masstransit-crack/
├── Cecil/
│   ├── Cecil.csproj      # Project file with Mono.Cecil dependency
│   └── Program.cs        # All patching logic (~95 lines)
├── .gitignore
└── README.md
```

## License

This tool is intended for educational and testing purposes.
