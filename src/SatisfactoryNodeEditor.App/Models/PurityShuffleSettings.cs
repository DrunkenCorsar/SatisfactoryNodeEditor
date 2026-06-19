namespace SatisfactoryNodeEditor.App.Models;

public sealed record PurityShuffleSettings(
    PurityDistributionMode Mode,
    PurityDistribution GlobalDistribution,
    IReadOnlyDictionary<string, PurityDistribution> PerResourceDistributions);

public sealed record PurityDistribution(
    double ImpurePercent,
    double NormalPercent,
    double PurePercent);
