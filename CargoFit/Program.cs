using Velopack;
using Avalonia;
using CargoFit;

// Must be the very first call — Velopack hooks into the process lifecycle here.
// In development (dotnet run) this is a safe no-op.
VelopackApp.Build().Run();

AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
