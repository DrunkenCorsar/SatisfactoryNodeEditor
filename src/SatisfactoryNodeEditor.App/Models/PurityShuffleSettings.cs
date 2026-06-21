namespace SatisfactoryNodeEditor.App.Models;

public sealed record PurityShuffleSettings(
    PurityDistributionMode Mode,
    PurityDistribution GlobalDistribution,
    IReadOnlyDictionary<string, PurityDistribution> PerResourceDistributions,
    IReadOnlyDictionary<string, IReadOnlyList<string>> NativePerResourcePurities);

public sealed record PurityDistribution(
    double ImpurePercent,
    double NormalPercent,
    double PurePercent);
