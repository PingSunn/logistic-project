using System.Text.Json;

namespace logistic;

internal static class JsonOptions
{
    internal static readonly JsonSerializerOptions WriteIndented = new() { WriteIndented = true };
}
