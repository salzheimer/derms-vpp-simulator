using System.Runtime.CompilerServices;

namespace Rva.SharedKernel.Units;

internal static class UnitGuard
{
    public static void Finite(double value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name, value, "A physical quantity must be a finite number, not NaN or infinity.");
    }
}
