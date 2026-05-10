using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

// BenchmarkDotNet's default out-of-process toolchain generates a child project
// targeting net10.0 (no OS suffix), which cannot reference this net10.0-windows
// project. InProcessEmitToolchain avoids the child project entirely.
var config = DefaultConfig.Instance
    .AddJob(Job.Default
        .WithToolchain(InProcessEmitToolchain.Instance)
        .AsDefault());

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);

// To run all benchmarks:
//   dotnet run --project Unosquare.FFME.Benchmarks/Unosquare.FFME.Benchmarks.csproj -c Release
// To run a specific class:
//   dotnet run --project Unosquare.FFME.Benchmarks/Unosquare.FFME.Benchmarks.csproj -c Release -- --filter "*CircularBuffer*"
