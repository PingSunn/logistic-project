using System.Text.Json;

namespace CargoFit;

internal static class JsonOptions
{
    internal static readonly JsonSerializerOptions WriteIndented = new() { WriteIndented = true };
}
