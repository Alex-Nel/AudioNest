namespace AudioNest.Desktop;

/// <summary>Inspector focus is distinct from the remembered import/paste destination.</summary>
public readonly record struct EffectTarget(Guid Id, bool IsTrack = false);
