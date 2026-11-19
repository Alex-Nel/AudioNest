namespace AudioNest.Domain;

/// <summary>Explicit menu/serialization identities; HardLimiter remains a legacy clipper.</summary>
public static class BuiltInEffects
{
    public static IReadOnlyList<EffectKind> Available { get; } =
        Array.AsReadOnly(new[] { EffectKind.Gain, EffectKind.Compressor, EffectKind.Limiter, EffectKind.Normalizer });
    public static string Name(EffectKind kind) => kind switch
    {
        EffectKind.Gain => "Gain",
        EffectKind.HardLimiter => "Hard limiter",
        EffectKind.Compressor => "Compressor",
        EffectKind.Limiter => "Limiter",
        EffectKind.Normalizer => "Normalizer",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public static EffectSpec Create(EffectKind kind) => kind switch
    {
        EffectKind.Gain => new(kind, 1),
        EffectKind.HardLimiter => new(kind, 0.9f),
        EffectKind.Compressor => new(kind, -18),
        EffectKind.Limiter => new(kind, 0.8912509f, ReleaseMs: 50),
        EffectKind.Normalizer => new(kind, 0.8912509f),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
