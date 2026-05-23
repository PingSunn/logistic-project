namespace CargoFit;

public record BoxPlacement(
    double X, double Y, double Z,
    double BW, double BL, double BH,
    int ProductIndex,
    bool Rotated = false,
    int StackIndex = 0,
    int LayerIndex = 0);
