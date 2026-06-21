using SatisfactoryNodeEditor.App.Models;
using SatisfactoryNodeEditor.App.ViewModels;

namespace SatisfactoryNodeEditor.App.Services;

public sealed class ResourceNodeShuffleService
{
    private const int NeighborCount = 8;
    private const int LocalSearchPasses = 2;
    private const int MaxLocalSearchAttemptsPerPass = 900;
    private readonly Random _random;

    public ResourceNodeShuffleService(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public ShufflePreviewResult Shuffle(
        IReadOnlyList<ResourceNodeViewModel> nodes,
        double clusteringPercent = 100,
        bool hardMode = false,
        IReadOnlyDictionary<string, int>? requestedCounts = null,
        PurityShuffleSettings? puritySettings = null)
    {
        var clusteringRatio = Math.Clamp(clusteringPercent, 0, 100) / 100.0;
        var weights = ShuffleWeights.From(clusteringRatio);
        var ordinaryNodes = nodes
            .Where(node => node.NodeKind.Equals("ResourceNode", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (ordinaryNodes.Length == 0)
        {
            return new ShufflePreviewResult(0, 0, 0, "No ordinary resource nodes found to shuffle.");
        }

        var groups = BuildResourceGroups(ordinaryNodes, requestedCounts, puritySettings)
            .OrderBy(_ => _random.Next())
            .ToArray();

        var clusterNodes = BuildBalancedClusters(ordinaryNodes, groups, weights, hardMode);
        var resourceChanges = 0;
        var purityChanges = 0;
        var touchedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var group = groups[groupIndex];
            var positions = clusterNodes[groupIndex];
            for (var assignmentIndex = 0; assignmentIndex < group.Assignments.Length; assignmentIndex++)
            {
                var node = positions[assignmentIndex];
                var assignment = group.Assignments[assignmentIndex];

                if (!node.ResourceType.Equals(assignment.ResourceType, StringComparison.OrdinalIgnoreCase))
                {
                    resourceChanges += 1;
                    touchedNodes.Add(node.Id);
                }

                if (!node.Purity.Equals(assignment.Purity, StringComparison.OrdinalIgnoreCase))
                {
                    purityChanges += 1;
                    touchedNodes.Add(node.Id);
                }

                node.ResourceType = assignment.ResourceType;
                node.Purity = assignment.Purity;
            }
        }

        var metrics = CalculateMetrics(ordinaryNodes);
        return new ShufflePreviewResult(
            ordinaryNodes.Length,
            groups.Length,
            touchedNodes.Count,
            $"""
            Preview shuffled {ordinaryNodes.Length} ordinary resource nodes with {clusteringRatio:P0} clustering into {groups.Length} resource regions. Hard mode: {(hardMode ? "on" : "off")}.
            Resource changes: {resourceChanges}. Purity changes: {purityChanges}. Wells and geysers were not changed.
            Shuffle metrics: same-resource nearest distance {metrics.AverageSameResourceNearestDistance:0}, compactness distance {metrics.AverageCentroidDistance:0}, graph components {metrics.SameResourceGraphComponents}.
            """);
    }

    public ShufflePreviewResult ShufflePurities(
        IReadOnlyList<ResourceNodeViewModel> nodes,
        PurityShuffleSettings puritySettings)
    {
        var ordinaryNodes = nodes
            .Where(node => node.NodeKind.Equals("ResourceNode", StringComparison.OrdinalIgnoreCase))
            .Where(node => !node.ResourceType.Equals("Empty", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (ordinaryNodes.Length == 0)
        {
            return new ShufflePreviewResult(0, 0, 0, "No ordinary non-empty resource nodes found for purity shuffle.");
        }

        var changes = puritySettings.Mode switch
        {
            PurityDistributionMode.PerResource => ApplyPerResourcePurityShuffle(ordinaryNodes, puritySettings),
            PurityDistributionMode.Global => ApplyGlobalPurityShuffle(ordinaryNodes, puritySettings.GlobalDistribution),
            PurityDistributionMode.Native => ApplyNativePurityShuffle(ordinaryNodes, puritySettings.NativePerResourcePurities),
            _ => ApplyCurrentPurityShuffle(ordinaryNodes)
        };

        return new ShufflePreviewResult(
            ordinaryNodes.Length,
            ordinaryNodes.Select(node => node.ResourceType).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            changes,
            $"""
            Preview shuffled purities only on {ordinaryNodes.Length} ordinary resource nodes. Resources, positions, wells, geysers, and empty nodes were not changed.
            Purity changes: {changes}. Mode: {puritySettings.Mode}.
            """);
    }

    private int ApplyCurrentPurityShuffle(ResourceNodeViewModel[] nodes)
    {
        var changes = 0;
        foreach (var group in nodes.GroupBy(node => node.ResourceType, StringComparer.OrdinalIgnoreCase))
        {
            var groupNodes = group.OrderBy(_ => _random.Next()).ToArray();
            var pool = group
                .Select(node => NormalizePurityLabel(node.Purity))
                .OrderBy(_ => _random.Next())
                .ToArray();
            changes += ApplyPurityPool(groupNodes, pool);
        }

        return changes;
    }

    private int ApplyNativePurityShuffle(
        ResourceNodeViewModel[] nodes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> nativePuritiesByResource)
    {
        var changes = 0;
        foreach (var group in nodes.GroupBy(node => node.ResourceType, StringComparer.OrdinalIgnoreCase))
        {
            var groupNodes = group.OrderBy(_ => _random.Next()).ToArray();
            var pool = nativePuritiesByResource.TryGetValue(group.Key, out var nativePurities) && nativePurities.Count > 0
                ? RepeatNativePurityPool(nativePurities, groupNodes.Length).OrderBy(_ => _random.Next()).ToArray()
                : group.Select(node => NormalizePurityLabel(node.Purity)).OrderBy(_ => _random.Next()).ToArray();
            changes += ApplyPurityPool(groupNodes, pool);
        }

        return changes;
    }

    private int ApplyGlobalPurityShuffle(ResourceNodeViewModel[] nodes, PurityDistribution distribution)
    {
        var pool = CreatePurityPool(CalculatePurityCounts(nodes.Length, distribution))
            .OrderBy(_ => _random.Next())
            .ToArray();
        return ApplyPurityPool(nodes.OrderBy(_ => _random.Next()).ToArray(), pool);
    }

    private int ApplyPerResourcePurityShuffle(ResourceNodeViewModel[] nodes, PurityShuffleSettings puritySettings)
    {
        var changes = 0;
        foreach (var group in nodes.GroupBy(node => node.ResourceType, StringComparer.OrdinalIgnoreCase))
        {
            var groupNodes = group.OrderBy(_ => _random.Next()).ToArray();
            var distribution = puritySettings.PerResourceDistributions.TryGetValue(group.Key, out var resourceDistribution)
                ? resourceDistribution
                : puritySettings.GlobalDistribution;
            var pool = CreatePurityPool(CalculatePurityCounts(groupNodes.Length, distribution))
                .OrderBy(_ => _random.Next())
                .ToArray();
            changes += ApplyPurityPool(groupNodes, pool);
        }

        return changes;
    }

    private static int ApplyPurityPool(ResourceNodeViewModel[] nodes, string[] purityPool)
    {
        var changes = 0;
        for (var index = 0; index < nodes.Length && index < purityPool.Length; index++)
        {
            var purity = purityPool[index];
            if (nodes[index].Purity.Equals(purity, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            nodes[index].Purity = purity;
            changes += 1;
        }

        return changes;
    }

    private static IEnumerable<ResourceGroup> BuildResourceGroups(
        IReadOnlyCollection<ResourceNodeViewModel> ordinaryNodes,
        IReadOnlyDictionary<string, int>? requestedCounts,
        PurityShuffleSettings? puritySettings)
    {
        if (requestedCounts is null)
        {
            return ordinaryNodes
                .Select(node => new NodeAssignment(node.ResourceType, node.Purity))
                .GroupBy(assignment => assignment.ResourceType, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ResourceGroup(
                    group.Key,
                    group.OrderBy(assignment => PurityRank(assignment.Purity)).ToArray()));
        }

        var currentPuritiesByResource = BuildNativePurityPools(ordinaryNodes);
        var nativePuritiesByResource = puritySettings?.NativePerResourcePurities ?? currentPuritiesByResource;
        var groups = new List<ResourceGroup>();
        var requestedTotal = Math.Min(ordinaryNodes.Count, requestedCounts.Values.Where(count => count > 0).Sum());
        var remainingNodes = ordinaryNodes.Count;
        var requestedResourceCounts = new List<KeyValuePair<string, int>>();
        foreach (var pair in requestedCounts.Where(pair => pair.Value > 0))
        {
            var count = Math.Min(pair.Value, remainingNodes);
            if (count <= 0)
            {
                break;
            }

            requestedResourceCounts.Add(new KeyValuePair<string, int>(pair.Key, count));
            remainingNodes -= count;
        }

        var globalPurityAllocations = BuildGlobalPurityAllocations(requestedResourceCounts, puritySettings);

        foreach (var pair in requestedResourceCounts)
        {
            var purityPool = BuildPurityPoolForResource(
                pair.Key,
                pair.Value,
                puritySettings,
                globalPurityAllocations,
                currentPuritiesByResource,
                nativePuritiesByResource);
            var assignments = purityPool
                .Select(purity => new NodeAssignment(pair.Key, purity))
                .OrderBy(assignment => PurityRank(assignment.Purity))
                .ToArray();

            if (assignments.Length > 0)
            {
                groups.Add(new ResourceGroup(pair.Key, assignments));
            }
        }

        var emptyCount = ordinaryNodes.Count - requestedTotal;
        if (emptyCount > 0)
        {
            groups.Add(new ResourceGroup("Empty", Enumerable.Range(0, emptyCount)
                .Select(_ => new NodeAssignment("Empty", "Not applicable"))
                .ToArray()));
        }

        return groups;
    }

    private static Dictionary<string, PurityCounts>? BuildGlobalPurityAllocations(
        IReadOnlyList<KeyValuePair<string, int>> requestedResourceCounts,
        PurityShuffleSettings? puritySettings)
    {
        if (puritySettings?.Mode != PurityDistributionMode.Global)
        {
            return null;
        }

        var total = requestedResourceCounts.Sum(pair => pair.Value);
        var target = CalculatePurityCounts(total, puritySettings.GlobalDistribution);
        var allocations = requestedResourceCounts.ToDictionary(
            pair => pair.Key,
            pair => CalculatePurityCounts(pair.Value, puritySettings.GlobalDistribution),
            StringComparer.OrdinalIgnoreCase);
        BalanceGlobalPurityAllocations(allocations, target);
        return allocations;
    }

    private static string[] BuildPurityPoolForResource(
        string resourceType,
        int count,
        PurityShuffleSettings? puritySettings,
        IReadOnlyDictionary<string, PurityCounts>? globalPurityAllocations,
        IReadOnlyDictionary<string, IReadOnlyList<string>> currentPuritiesByResource,
        IReadOnlyDictionary<string, IReadOnlyList<string>> nativePuritiesByResource)
    {
        if (resourceType.Equals("Empty", StringComparison.OrdinalIgnoreCase))
        {
            return Enumerable.Repeat("Not applicable", count).ToArray();
        }

        if (puritySettings?.Mode == PurityDistributionMode.PerResource)
        {
            var distribution = puritySettings.PerResourceDistributions.TryGetValue(resourceType, out var resourceDistribution)
                ? resourceDistribution
                : puritySettings.GlobalDistribution;
            return CreatePurityPool(CalculatePurityCounts(count, distribution));
        }

        if (puritySettings?.Mode == PurityDistributionMode.Global && globalPurityAllocations is not null)
        {
            var counts = globalPurityAllocations.TryGetValue(resourceType, out var allocatedCounts)
                ? allocatedCounts
                : CalculatePurityCounts(count, puritySettings.GlobalDistribution);
            return CreatePurityPool(counts);
        }

        var sourcePurities = puritySettings?.Mode == PurityDistributionMode.Native
            ? nativePuritiesByResource
            : currentPuritiesByResource;
        return sourcePurities.TryGetValue(resourceType, out var nativePurities) && nativePurities.Count > 0
            ? RepeatNativePurityPool(nativePurities, count)
            : Enumerable.Repeat("Normal", count).ToArray();
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildNativePurityPools(IEnumerable<ResourceNodeViewModel> nodes) =>
        nodes
            .Where(node => !node.ResourceType.Equals("Empty", StringComparison.OrdinalIgnoreCase))
            .GroupBy(node => node.ResourceType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(node => NormalizePurityLabel(node.Purity))
                    .OrderBy(PurityRank)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

    private static string[] RepeatNativePurityPool(IReadOnlyList<string> nativePurities, int count)
    {
        var result = new string[count];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = nativePurities[index % nativePurities.Count];
        }

        return result;
    }

    private static string[] CreatePurityPool(PurityCounts counts) =>
    [
        .. Enumerable.Repeat("Impure", counts.Impure),
        .. Enumerable.Repeat("Normal", counts.Normal),
        .. Enumerable.Repeat("Pure", counts.Pure)
    ];

    private static void BalanceGlobalPurityAllocations(Dictionary<string, PurityCounts> allocations, PurityCounts target)
    {
        var current = SumPurityCounts(allocations.Values);
        while (!current.Equals(target))
        {
            var neededIndex = LargestPositiveDifference(target, current);
            var excessIndex = LargestPositiveDifference(current, target);
            if (neededIndex < 0 || excessIndex < 0)
            {
                break;
            }

            var resource = allocations
                .Where(pair => GetPurityCount(pair.Value, excessIndex) > 0)
                .OrderByDescending(pair => GetPurityCount(pair.Value, excessIndex))
                .Select(pair => pair.Key)
                .FirstOrDefault();
            if (resource is null)
            {
                break;
            }

            allocations[resource] = MoveOnePurity(allocations[resource], excessIndex, neededIndex);
            current = SumPurityCounts(allocations.Values);
        }
    }

    private static PurityCounts SumPurityCounts(IEnumerable<PurityCounts> counts) =>
        counts.Aggregate(new PurityCounts(0, 0, 0), (total, next) => new PurityCounts(
            total.Impure + next.Impure,
            total.Normal + next.Normal,
            total.Pure + next.Pure));

    private static int LargestPositiveDifference(PurityCounts left, PurityCounts right)
    {
        var differences = new[]
        {
            left.Impure - right.Impure,
            left.Normal - right.Normal,
            left.Pure - right.Pure
        };
        var max = differences.Max();
        return max > 0 ? Array.IndexOf(differences, max) : -1;
    }

    private static int GetPurityCount(PurityCounts counts, int index) => index switch
    {
        0 => counts.Impure,
        1 => counts.Normal,
        2 => counts.Pure,
        _ => 0
    };

    private static PurityCounts MoveOnePurity(PurityCounts counts, int fromIndex, int toIndex)
    {
        var values = new[] { counts.Impure, counts.Normal, counts.Pure };
        if (fromIndex < 0 || fromIndex >= values.Length || toIndex < 0 || toIndex >= values.Length || values[fromIndex] <= 0)
        {
            return counts;
        }

        values[fromIndex] -= 1;
        values[toIndex] += 1;
        return new PurityCounts(values[0], values[1], values[2]);
    }

    private static PurityCounts CalculatePurityCounts(int total, PurityDistribution distribution)
    {
        if (total <= 0)
        {
            return new PurityCounts(0, 0, 0);
        }

        var rates = new[]
        {
            Math.Max(0, distribution.ImpurePercent),
            Math.Max(0, distribution.NormalPercent),
            Math.Max(0, distribution.PurePercent)
        };
        var rateTotal = rates.Sum();
        if (rateTotal <= 0)
        {
            rates = [0, 1, 0];
            rateTotal = 1;
        }

        var exact = rates.Select(rate => rate / rateTotal * total).ToArray();
        var counts = exact.Select(value => (int)Math.Floor(value)).ToArray();
        var remaining = total - counts.Sum();
        foreach (var index in exact
            .Select((value, index) => new { Index = index, Remainder = value - Math.Floor(value) })
            .OrderByDescending(item => item.Remainder)
            .ThenBy(item => item.Index)
            .Take(remaining)
            .Select(item => item.Index))
        {
            counts[index] += 1;
        }

        return new PurityCounts(counts[0], counts[1], counts[2]);
    }

    public IReadOnlyList<ShuffleDiagnosticResult> CreateDebugMetrics(IReadOnlyList<ResourceNodeViewModel> nodes)
    {
        var results = new List<ShuffleDiagnosticResult>();
        foreach (var value in new double[] { 0, 25, 50, 75, 100 })
        {
            var copy = nodes.Select(CloneNode).ToArray();
            var shuffler = new ResourceNodeShuffleService(12345);
            _ = shuffler.Shuffle(copy, value);
            var metrics = CalculateMetrics(copy.Where(node => node.NodeKind.Equals("ResourceNode", StringComparison.OrdinalIgnoreCase)).ToArray());
            results.Add(new ShuffleDiagnosticResult(value, metrics.AverageSameResourceNearestDistance, metrics.AverageCentroidDistance, metrics.SameResourceGraphComponents));
        }

        return results;
    }

    private ResourceNodeViewModel[][] BuildBalancedClusters(
        ResourceNodeViewModel[] nodes,
        ResourceGroup[] groups,
        ShuffleWeights weights,
        bool hardMode)
    {
        var clusterSizes = groups.Select(group => group.Assignments.Length).ToArray();
        var graph = BuildKNearestNeighborGraph(nodes, NeighborCount);
        var context = new ShuffleContext(nodes, graph, MaxDistanceSquared(nodes));
        var seedIndexes = ChooseSeedIndexes(nodes, groups, hardMode);
        var clusters = Enumerable.Range(0, clusterSizes.Length)
            .Select(_ => new List<int>())
            .ToArray();
        var clusterByNode = Enumerable.Repeat(-1, nodes.Length).ToArray();
        var assignedCount = 0;

        for (var clusterIndex = 0; clusterIndex < seedIndexes.Length; clusterIndex++)
        {
            if (clusterSizes[clusterIndex] <= 0)
            {
                continue;
            }

            var seedIndex = seedIndexes[clusterIndex];
            clusters[clusterIndex].Add(seedIndex);
            clusterByNode[seedIndex] = clusterIndex;
            assignedCount += 1;
        }

        while (assignedCount < nodes.Length)
        {
            var clusterIndex = ChooseClusterToGrow(clusters, clusterSizes);
            if (clusterIndex < 0)
            {
                break;
            }

            var nextNodeIndex = FindBestUnassignedNode(clusterIndex, clusters, clusterByNode, context, weights);
            if (nextNodeIndex < 0)
            {
                break;
            }

            clusters[clusterIndex].Add(nextNodeIndex);
            clusterByNode[nextNodeIndex] = clusterIndex;
            assignedCount += 1;
        }

        RunConnectivityPreservingLocalSearch(clusters, clusterByNode, context, weights);

        return clusters
            .Select(cluster => SortCluster(cluster.Select(index => nodes[index]).ToArray()))
            .ToArray();
    }

    private int[][] BuildKNearestNeighborGraph(ResourceNodeViewModel[] nodes, int neighborCount)
    {
        var edges = nodes.Select(_ => new HashSet<int>()).ToArray();
        for (var nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            var nearest = Enumerable.Range(0, nodes.Length)
                .Where(candidateIndex => candidateIndex != nodeIndex)
                .OrderBy(candidateIndex => DistanceSquared(nodes[nodeIndex], nodes[candidateIndex]))
                .Take(Math.Min(neighborCount, nodes.Length - 1));

            foreach (var nearestIndex in nearest)
            {
                edges[nodeIndex].Add(nearestIndex);
                edges[nearestIndex].Add(nodeIndex);
            }
        }

        return edges.Select(edgeSet => edgeSet.ToArray()).ToArray();
    }

    private int ChooseClusterToGrow(IReadOnlyList<List<int>> clusters, int[] clusterSizes)
    {
        return Enumerable.Range(0, clusters.Count)
            .Where(index => clusters[index].Count < clusterSizes[index])
            .OrderBy(index => clusters[index].Count / (double)clusterSizes[index])
            .ThenBy(_ => _random.Next())
            .FirstOrDefault(-1);
    }

    private int ChooseClusterWithFrontierToGrow(
        IReadOnlyList<List<int>> clusters,
        int[] clusterSizes,
        int[] clusterByNode,
        int[][] graph)
    {
        return Enumerable.Range(0, clusters.Count)
            .Where(index => clusters[index].Count < clusterSizes[index] && HasUnassignedFrontier(clusters[index], clusterByNode, graph))
            .OrderBy(index => clusters[index].Count / (double)clusterSizes[index])
            .ThenBy(_ => _random.Next())
            .FirstOrDefault(-1);
    }

    private static bool HasUnassignedFrontier(IEnumerable<int> cluster, int[] clusterByNode, int[][] graph) =>
        cluster.Any(nodeIndex => graph[nodeIndex].Any(neighborIndex => clusterByNode[neighborIndex] < 0));

    private int FindNearestFrontierNode(
        int clusterIndex,
        IReadOnlyList<List<int>> clusters,
        int[] clusterByNode,
        int[][] graph,
        ResourceNodeViewModel[] nodes)
    {
        var bestNodeIndex = -1;
        var bestDistance = double.PositiveInfinity;

        foreach (var clusterNodeIndex in clusters[clusterIndex])
        {
            foreach (var neighborIndex in graph[clusterNodeIndex])
            {
                if (clusterByNode[neighborIndex] >= 0)
                {
                    continue;
                }

                var distance = DistanceSquared(nodes[clusterNodeIndex], nodes[neighborIndex]);
                if (distance < bestDistance || (Math.Abs(distance - bestDistance) < 0.0001 && _random.Next(2) == 0))
                {
                    bestDistance = distance;
                    bestNodeIndex = neighborIndex;
                }
            }
        }

        return bestNodeIndex;
    }

    private int FindNearestUnassignedNode(
        int clusterIndex,
        IReadOnlyList<List<int>> clusters,
        int[] clusterByNode,
        ResourceNodeViewModel[] nodes)
    {
        return Enumerable.Range(0, nodes.Length)
            .Where(nodeIndex => clusterByNode[nodeIndex] < 0)
            .OrderBy(nodeIndex => DistanceSquaredToCluster(nodeIndex, clusters[clusterIndex], nodes))
            .ThenBy(_ => _random.Next())
            .FirstOrDefault(-1);
    }

    private int FindBestUnassignedNode(
        int clusterIndex,
        IReadOnlyList<List<int>> clusters,
        int[] clusterByNode,
        ShuffleContext context,
        ShuffleWeights weights)
    {
        return Enumerable.Range(0, context.Nodes.Length)
            .Where(nodeIndex => clusterByNode[nodeIndex] < 0)
            .OrderBy(nodeIndex => CandidateScore(nodeIndex, clusters[clusterIndex], context, weights) + weights.Randomness * _random.NextDouble())
            .ThenBy(_ => _random.Next())
            .FirstOrDefault(-1);
    }

    private void RunConnectivityPreservingLocalSearch(
        List<int>[] clusters,
        int[] clusterByNode,
        ShuffleContext context,
        ShuffleWeights weights)
    {
        for (var pass = 0; pass < LocalSearchPasses; pass++)
        {
            var improved = false;
            var attempts = 0;
            var boundaryNodes = Enumerable.Range(0, context.Nodes.Length)
                .Where(nodeIndex => context.Graph[nodeIndex].Any(neighborIndex => clusterByNode[neighborIndex] != clusterByNode[nodeIndex]))
                .OrderBy(_ => _random.Next())
                .ToArray();

            foreach (var firstNodeIndex in boundaryNodes)
            {
                if (attempts >= MaxLocalSearchAttemptsPerPass)
                {
                    break;
                }

                var firstClusterIndex = clusterByNode[firstNodeIndex];
                var neighbors = context.Graph[firstNodeIndex]
                    .Where(secondNodeIndex => clusterByNode[secondNodeIndex] != firstClusterIndex)
                    .OrderBy(_ => _random.Next());

                foreach (var secondNodeIndex in neighbors)
                {
                    attempts += 1;
                    var secondClusterIndex = clusterByNode[secondNodeIndex];
                    if (!SwapImprovesScore(firstNodeIndex, secondNodeIndex, firstClusterIndex, secondClusterIndex, clusters, context, weights))
                    {
                        continue;
                    }

                    if (!IsConnectedAfterSwap(clusters[firstClusterIndex], firstNodeIndex, secondNodeIndex, context.Graph) ||
                        !IsConnectedAfterSwap(clusters[secondClusterIndex], secondNodeIndex, firstNodeIndex, context.Graph))
                    {
                        continue;
                    }

                    ReplaceNode(clusters[firstClusterIndex], firstNodeIndex, secondNodeIndex);
                    ReplaceNode(clusters[secondClusterIndex], secondNodeIndex, firstNodeIndex);
                    clusterByNode[firstNodeIndex] = secondClusterIndex;
                    clusterByNode[secondNodeIndex] = firstClusterIndex;
                    improved = true;
                    break;
                }
            }

            if (!improved)
            {
                break;
            }
        }
    }

    private static bool SwapImprovesScore(
        int firstNodeIndex,
        int secondNodeIndex,
        int firstClusterIndex,
        int secondClusterIndex,
        IReadOnlyList<List<int>> clusters,
        ShuffleContext context,
        ShuffleWeights weights)
    {
        var firstBeforeCluster = clusters[firstClusterIndex].Where(nodeIndex => nodeIndex != firstNodeIndex).DefaultIfEmpty(firstNodeIndex).ToArray();
        var secondBeforeCluster = clusters[secondClusterIndex].Where(nodeIndex => nodeIndex != secondNodeIndex).DefaultIfEmpty(secondNodeIndex).ToArray();
        var before = CandidateScore(firstNodeIndex, firstBeforeCluster, context, weights) +
            CandidateScore(secondNodeIndex, secondBeforeCluster, context, weights);
        var after = CandidateScore(firstNodeIndex, secondBeforeCluster, context, weights) +
            CandidateScore(secondNodeIndex, firstBeforeCluster, context, weights);

        return after + 0.0001 < before;
    }

    private static double ClusterCompactness(IReadOnlyCollection<int> cluster, ResourceNodeViewModel[] nodes) =>
        ClusterCompactness(cluster, nodeIndex => nodeIndex, nodes);

    private static double ClusterCompactnessAfterSwap(
        IReadOnlyCollection<int> cluster,
        int removedNodeIndex,
        int addedNodeIndex,
        ResourceNodeViewModel[] nodes) =>
        ClusterCompactness(cluster, nodeIndex => nodeIndex == removedNodeIndex ? addedNodeIndex : nodeIndex, nodes);

    private static double ClusterCompactness(
        IReadOnlyCollection<int> cluster,
        Func<int, int> mapNodeIndex,
        ResourceNodeViewModel[] nodes)
    {
        var centerX = cluster.Average(nodeIndex => nodes[mapNodeIndex(nodeIndex)].WorldX);
        var centerY = cluster.Average(nodeIndex => nodes[mapNodeIndex(nodeIndex)].WorldY);
        return cluster.Sum(nodeIndex =>
        {
            var mappedIndex = mapNodeIndex(nodeIndex);
            var dx = nodes[mappedIndex].WorldX - centerX;
            var dy = nodes[mappedIndex].WorldY - centerY;
            return dx * dx + dy * dy;
        });
    }

    private static bool IsConnectedAfterSwap(IReadOnlyCollection<int> cluster, int removedNodeIndex, int addedNodeIndex, int[][] graph)
    {
        if (cluster.Count <= 1)
        {
            return true;
        }

        var clusterSet = cluster.Where(nodeIndex => nodeIndex != removedNodeIndex).Append(addedNodeIndex).ToHashSet();
        var start = clusterSet.First();
        var visited = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in graph[current])
            {
                if (!clusterSet.Contains(neighbor) || !visited.Add(neighbor))
                {
                    continue;
                }

                queue.Enqueue(neighbor);
            }
        }

        return visited.Count == clusterSet.Count;
    }

    private static void ReplaceNode(IList<int> cluster, int oldNodeIndex, int newNodeIndex)
    {
        var index = cluster.IndexOf(oldNodeIndex);
        if (index >= 0)
        {
            cluster[index] = newNodeIndex;
        }
    }

    private static ResourceNodeViewModel[] SortCluster(IReadOnlyCollection<ResourceNodeViewModel> group)
    {
        var centerX = group.Average(item => item.WorldX);
        var centerY = group.Average(item => item.WorldY);
        return group
            .OrderBy(node => Math.Atan2(node.WorldY - centerY, node.WorldX - centerX))
            .ToArray();
    }

    private int[] ChooseSeedIndexes(ResourceNodeViewModel[] nodes, ResourceGroup[] groups, bool hardMode)
    {
        var seedIndexes = Enumerable.Repeat(-1, groups.Length).ToArray();
        var assignedSeeds = new List<int>();

        if (hardMode)
        {
            var hardGroupIndexes = Enumerable.Range(0, groups.Length)
                .Where(index => IsHardModeResource(groups[index].ResourceType))
                .OrderBy(_ => _random.Next())
                .ToArray();
            var hardSeedIndexes = ChooseSeparatedSeedIndexes(nodes, hardGroupIndexes.Length);

            for (var index = 0; index < hardGroupIndexes.Length && index < hardSeedIndexes.Length; index++)
            {
                seedIndexes[hardGroupIndexes[index]] = hardSeedIndexes[index];
                assignedSeeds.Add(hardSeedIndexes[index]);
            }
        }

        if (assignedSeeds.Count == 0)
        {
            var firstSeed = _random.Next(nodes.Length);
            var firstCluster = Array.IndexOf(seedIndexes, -1);
            seedIndexes[firstCluster] = firstSeed;
            assignedSeeds.Add(firstSeed);
        }

        while (seedIndexes.Any(seedIndex => seedIndex < 0))
        {
            var next = Enumerable.Range(0, nodes.Length)
                .Where(nodeIndex => !assignedSeeds.Contains(nodeIndex))
                .OrderByDescending(nodeIndex => assignedSeeds.Min(seedIndex => DistanceSquared(nodes[nodeIndex], nodes[seedIndex])))
                .ThenBy(_ => _random.Next())
                .First();
            var targetCluster = Array.IndexOf(seedIndexes, -1);
            seedIndexes[targetCluster] = next;
            assignedSeeds.Add(next);
        }

        return seedIndexes;
    }

    private int[] ChooseSeparatedSeedIndexes(ResourceNodeViewModel[] nodes, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        if (count == 1)
        {
            return [_random.Next(nodes.Length)];
        }

        var bestFirst = 0;
        var bestSecond = 1;
        var bestDistance = double.NegativeInfinity;
        for (var first = 0; first < nodes.Length; first++)
        {
            for (var second = first + 1; second < nodes.Length; second++)
            {
                var distance = DistanceSquared(nodes[first], nodes[second]);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    bestFirst = first;
                    bestSecond = second;
                }
            }
        }

        var seeds = new List<int> { bestFirst, bestSecond };
        while (seeds.Count < count)
        {
            var next = Enumerable.Range(0, nodes.Length)
                .Where(nodeIndex => !seeds.Contains(nodeIndex))
                .OrderByDescending(nodeIndex => seeds.Min(seedIndex => DistanceSquared(nodes[nodeIndex], nodes[seedIndex])))
                .ThenBy(_ => _random.Next())
                .First();
            seeds.Add(next);
        }

        return seeds.ToArray();
    }

    private static double DistanceSquared(ResourceNodeViewModel first, ResourceNodeViewModel second)
    {
        var dx = first.WorldX - second.WorldX;
        var dy = first.WorldY - second.WorldY;
        return dx * dx + dy * dy;
    }

    private static double DistanceSquaredToCluster(int nodeIndex, IEnumerable<int> cluster, ResourceNodeViewModel[] nodes) =>
        cluster.Min(clusterNodeIndex => DistanceSquared(nodes[nodeIndex], nodes[clusterNodeIndex]));

    private static double CandidateScore(
        int nodeIndex,
        IReadOnlyCollection<int> cluster,
        ShuffleContext context,
        ShuffleWeights weights)
    {
        var nodes = context.Nodes;
        var clusterDistance = DistanceSquaredToCluster(nodeIndex, cluster, nodes) / context.MaxDistanceSquared;
        var centroidDistance = DistanceSquaredToCentroid(nodeIndex, cluster, nodes) / context.MaxDistanceSquared;
        var isFrontier = cluster.Any(clusterNodeIndex => context.Graph[clusterNodeIndex].Contains(nodeIndex));
        var spreadScore = -clusterDistance;
        var clusterScore = clusterDistance;
        var compactnessScore = centroidDistance;
        var frontierScore = isFrontier ? 0 : 1;

        return (weights.Spread * spreadScore) +
            (weights.Cluster * clusterScore) +
            (weights.Compactness * compactnessScore) +
            (weights.Frontier * frontierScore);
    }

    private static double DistanceSquaredToCentroid(int nodeIndex, IReadOnlyCollection<int> cluster, ResourceNodeViewModel[] nodes)
    {
        if (cluster.Count == 0)
        {
            return 0;
        }

        var centerX = cluster.Average(clusterNodeIndex => nodes[clusterNodeIndex].WorldX);
        var centerY = cluster.Average(clusterNodeIndex => nodes[clusterNodeIndex].WorldY);
        var dx = nodes[nodeIndex].WorldX - centerX;
        var dy = nodes[nodeIndex].WorldY - centerY;
        return dx * dx + dy * dy;
    }

    private static double MaxDistanceSquared(ResourceNodeViewModel[] nodes)
    {
        var minX = nodes.Min(node => node.WorldX);
        var maxX = nodes.Max(node => node.WorldX);
        var minY = nodes.Min(node => node.WorldY);
        var maxY = nodes.Max(node => node.WorldY);
        var dx = maxX - minX;
        var dy = maxY - minY;
        return Math.Max(1, (dx * dx) + (dy * dy));
    }

    private static bool IsHardModeResource(string resourceType)
    {
        var normalized = resourceType.Trim().ToLowerInvariant();
        return normalized is "iron" or "iron ore" or "copper" or "copper ore" or "limestone" or "stone";
    }

    private static ShuffleMetrics CalculateMetrics(ResourceNodeViewModel[] nodes)
    {
        var sameResourceNearestDistances = new List<double>();
        var centroidDistances = new List<double>();
        var graph = BuildKNearestNeighborGraphStatic(nodes, NeighborCount);
        var components = 0;

        foreach (var group in nodes.GroupBy(node => node.ResourceType, StringComparer.OrdinalIgnoreCase))
        {
            var groupNodes = group.ToArray();
            if (groupNodes.Length <= 1)
            {
                continue;
            }

            foreach (var node in groupNodes)
            {
                sameResourceNearestDistances.Add(Math.Sqrt(groupNodes
                    .Where(other => !ReferenceEquals(other, node))
                    .Min(other => DistanceSquared(node, other))));
            }

            var centerX = groupNodes.Average(node => node.WorldX);
            var centerY = groupNodes.Average(node => node.WorldY);
            centroidDistances.AddRange(groupNodes.Select(node =>
            {
                var dx = node.WorldX - centerX;
                var dy = node.WorldY - centerY;
                return Math.Sqrt((dx * dx) + (dy * dy));
            }));

            var groupIndexes = groupNodes.Select(node => Array.IndexOf(nodes, node)).ToHashSet();
            components += CountComponents(groupIndexes, graph);
        }

        return new ShuffleMetrics(
            sameResourceNearestDistances.Count == 0 ? 0 : sameResourceNearestDistances.Average(),
            centroidDistances.Count == 0 ? 0 : centroidDistances.Average(),
            components);
    }

    private static int CountComponents(HashSet<int> nodeIndexes, int[][] graph)
    {
        var remaining = new HashSet<int>(nodeIndexes);
        var components = 0;
        while (remaining.Count > 0)
        {
            components += 1;
            var start = remaining.First();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            remaining.Remove(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in graph[current])
                {
                    if (!remaining.Remove(neighbor))
                    {
                        continue;
                    }

                    queue.Enqueue(neighbor);
                }
            }
        }

        return components;
    }

    private static int[][] BuildKNearestNeighborGraphStatic(ResourceNodeViewModel[] nodes, int neighborCount)
    {
        var edges = nodes.Select(_ => new HashSet<int>()).ToArray();
        for (var nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            var nearest = Enumerable.Range(0, nodes.Length)
                .Where(candidateIndex => candidateIndex != nodeIndex)
                .OrderBy(candidateIndex => DistanceSquared(nodes[nodeIndex], nodes[candidateIndex]))
                .Take(Math.Min(neighborCount, nodes.Length - 1));

            foreach (var nearestIndex in nearest)
            {
                edges[nodeIndex].Add(nearestIndex);
                edges[nearestIndex].Add(nodeIndex);
            }
        }

        return edges.Select(edgeSet => edgeSet.ToArray()).ToArray();
    }

    private static ResourceNodeViewModel CloneNode(ResourceNodeViewModel node) => new()
    {
        Id = node.Id,
        NodeKind = node.NodeKind,
        ResourceType = node.ResourceType,
        Purity = node.Purity,
        WorldX = node.WorldX,
        WorldY = node.WorldY,
        WorldZ = node.WorldZ,
        MapX = node.MapX,
        MapY = node.MapY
    };

    private static int PurityRank(string purity) => purity.Trim().ToLowerInvariant() switch
    {
        "impure" or "inpure" or "rp_inpure" => 0,
        "normal" or "rp_normal" => 1,
        "pure" or "rp_pure" => 2,
        _ => 3
    };

    private static string NormalizePurityLabel(string purity) => purity.Trim().ToLowerInvariant() switch
    {
        "impure" or "inpure" or "rp_inpure" => "Impure",
        "pure" or "rp_pure" => "Pure",
        _ => "Normal"
    };

    private sealed record NodeAssignment(string ResourceType, string Purity);

    private sealed record ResourceGroup(string ResourceType, NodeAssignment[] Assignments);

    private sealed record PurityCounts(int Impure, int Normal, int Pure);

    private sealed record ShuffleWeights(
        double Spread,
        double Cluster,
        double Compactness,
        double Frontier,
        double Randomness)
    {
        public static ShuffleWeights From(double clusteringRatio)
        {
            var t = Math.Clamp(clusteringRatio, 0, 1);
            return new ShuffleWeights(
                Spread: 1 - t,
                Cluster: t,
                Compactness: t * t,
                Frontier: 4 * t * t * t * t,
                Randomness: (1 - t) * 0.25);
        }
    }

    private sealed record ShuffleMetrics(
        double AverageSameResourceNearestDistance,
        double AverageCentroidDistance,
        int SameResourceGraphComponents);

    private sealed record ShuffleContext(
        ResourceNodeViewModel[] Nodes,
        int[][] Graph,
        double MaxDistanceSquared);

}

public sealed record ShufflePreviewResult(
    int OrdinaryNodeCount,
    int ClusterCount,
    int NodesChanged,
    string Log);

public sealed record ShuffleDiagnosticResult(
    double ClusteringPercent,
    double AverageSameResourceNearestDistance,
    double AverageCentroidDistance,
    int SameResourceGraphComponents);
