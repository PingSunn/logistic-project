using Velopack;
using Avalonia;
using CargoFit;
using CargoFit.Cli;

// Must be the very first call — Velopack hooks into the process lifecycle here.
// In development (dotnet run) this is a safe no-op.
VelopackApp.Build().Run();

// CLI mode: dotnet run --project CargoFit/CargoFit.csproj -- --input testdata/devpreset.json
if (Array.IndexOf(args, "--input") >= 0)
{
    Environment.Exit(CliRunner.Run(args));
    return;
}

AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
