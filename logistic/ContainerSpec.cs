using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace logistic;

/// <summary>
/// Represents a shipping container type.
/// Nominal = real physical size shown to the user.
/// Interior = usable space for calculation (nominal − 15 cm per side).
/// </summary>
public record ContainerSpec(
    string Name,
    string SizeLabel,
    int NominalW,
    int NominalL,
    int NominalH)
{
    [JsonIgnore] public int InteriorW => NominalW - 15;
    [JsonIgnore] public int InteriorL => NominalL - 15;
    [JsonIgnore] public int InteriorH => NominalH - 15;

    public static readonly List<ContainerSpec> All =
    [
        new("ตู้สั้น",     "20 ft",    244, 600,  259),
        new("ตู้ยาว",     "40 ft",    244, 1209, 260),
        new("ตู้ไฮคิวบ์", "40 ft HC", 244, 1203, 290),
    ];
}
