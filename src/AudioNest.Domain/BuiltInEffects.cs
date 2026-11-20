namespace AudioNest.Domain;

/// <summary>Explicit menu/serialization identities; HardLimiter remains a legacy clipper.</summary>
public static class BuiltInEffects
{
    public static IReadOnlyList<EffectKind> Available { get; } =
        Array.AsReadOnly(new[]
        {
            EffectKind.Gain, EffectKind.Compressor, EffectKind.Limiter, EffectKind.Normalizer,
            EffectKind.NoiseReduction, EffectKind.Amplifier, EffectKind.Reverb, EffectKind.Echo, EffectKind.Distortion
        });
    public static string Name(EffectKind kind) => kind switch
    {
        EffectKind.Gain => "Gain",
        EffectKind.HardLimiter => "Hard limiter",
        EffectKind.Compressor => "Compressor",
        EffectKind.Limiter => "Limiter",
        EffectKind.Normalizer => "Normalizer",
        EffectKind.NoiseReduction => "Noise Reduction",
        EffectKind.Amplifier => "Amplifier",
        EffectKind.Reverb => "Reverb",
        EffectKind.Echo => "Echo",
        EffectKind.Distortion => "Distortion",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public static EffectSpec Create(EffectKind kind) => kind switch
    {
        EffectKind.Gain => new(kind, 1),
        EffectKind.HardLimiter => new(kind, 0.9f),
        EffectKind.Compressor => new(kind, -18),
        EffectKind.Limiter => new(kind, 0.8912509f, ReleaseMs: 50),
        EffectKind.Normalizer => new(kind, 0.8912509f),
        EffectKind.NoiseReduction => new(kind, -45, AttackMs: 5, ReleaseMs: 100, ReductionDb: 12),
        EffectKind.Amplifier => new(kind, 3),
        EffectKind.Reverb => new(kind, 1.5f),
        EffectKind.Echo => new(kind, 250),
        EffectKind.Distortion => new(kind, 12, Mix: 1),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
