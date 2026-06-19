const fs = require("node:fs");
const path = require("node:path");

const RESOURCE_HINTS = [
  "ResourceNode",
  "ExtractableResource",
  "mResourceType",
  "mResourceClass",
  "mResourceClassOverride",
  "mPurity"
];

const WORLD_BOUNDS = {
  minX: -324600,
  maxX: 425300,
  minY: -375000,
  maxY: 375000
};

const NEIGHBOR_COUNT = 8;
const LOCAL_SEARCH_PASSES = 5;

const RESOURCE_CLASS_PATHS = {
  bauxite: "/Game/FactoryGame/Resource/RawResources/OreBauxite/Desc_OreBauxite.Desc_OreBauxite_C",
  "ore bauxite": "/Game/FactoryGame/Resource/RawResources/OreBauxite/Desc_OreBauxite.Desc_OreBauxite_C",
  iron: "/Game/FactoryGame/Resource/RawResources/OreIron/Desc_OreIron.Desc_OreIron_C",
  "iron ore": "/Game/FactoryGame/Resource/RawResources/OreIron/Desc_OreIron.Desc_OreIron_C",
  copper: "/Game/FactoryGame/Resource/RawResources/OreCopper/Desc_OreCopper.Desc_OreCopper_C",
  "copper ore": "/Game/FactoryGame/Resource/RawResources/OreCopper/Desc_OreCopper.Desc_OreCopper_C",
  limestone: "/Game/FactoryGame/Resource/RawResources/Stone/Desc_Stone.Desc_Stone_C",
  stone: "/Game/FactoryGame/Resource/RawResources/Stone/Desc_Stone.Desc_Stone_C",
  coal: "/Game/FactoryGame/Resource/RawResources/Coal/Desc_Coal.Desc_Coal_C",
  caterium: "/Game/FactoryGame/Resource/RawResources/OreGold/Desc_OreGold.Desc_OreGold_C",
  "caterium ore": "/Game/FactoryGame/Resource/RawResources/OreGold/Desc_OreGold.Desc_OreGold_C",
  "ore gold": "/Game/FactoryGame/Resource/RawResources/OreGold/Desc_OreGold.Desc_OreGold_C",
  sulfur: "/Game/FactoryGame/Resource/RawResources/Sulfur/Desc_Sulfur.Desc_Sulfur_C",
  quartz: "/Game/FactoryGame/Resource/RawResources/RawQuartz/Desc_RawQuartz.Desc_RawQuartz_C",
  "raw quartz": "/Game/FactoryGame/Resource/RawResources/RawQuartz/Desc_RawQuartz.Desc_RawQuartz_C",
  uranium: "/Game/FactoryGame/Resource/RawResources/OreUranium/Desc_OreUranium.Desc_OreUranium_C",
  "uranium ore": "/Game/FactoryGame/Resource/RawResources/OreUranium/Desc_OreUranium.Desc_OreUranium_C",
  sam: "/Game/FactoryGame/Resource/RawResources/SAM/Desc_SAM.Desc_SAM_C",
  "crude oil": "/Game/FactoryGame/Resource/RawResources/CrudeOil/Desc_LiquidOil.Desc_LiquidOil_C",
  oil: "/Game/FactoryGame/Resource/RawResources/CrudeOil/Desc_LiquidOil.Desc_LiquidOil_C",
  water: "/Game/FactoryGame/Resource/RawResources/Water/Desc_Water.Desc_Water_C",
  nitrogen: "/Game/FactoryGame/Resource/RawResources/NitrogenGas/Desc_NitrogenGas.Desc_NitrogenGas_C",
  "nitrogen gas": "/Game/FactoryGame/Resource/RawResources/NitrogenGas/Desc_NitrogenGas.Desc_NitrogenGas_C"
};

main().catch((error) => {
  writeResult({
    success: false,
    candidateNodesFound: 0,
    nodesChanged: 0,
    outputSavePath: inferOutputSavePath(process.argv),
    log: error.stack || String(error),
    errorMessage: error.message || String(error)
  });
  process.exitCode = 1;
});

async function main() {
  const [, , commandOrInput, maybeInput, maybeOutput, maybeAssignmentPath] = process.argv;
  const isExplicitShuffle = String(commandOrInput || "").toLowerCase() === "shuffle";
  const isApplyAssignments = String(commandOrInput || "").toLowerCase() === "apply-assignments";
  const inputSavePath = isExplicitShuffle || isApplyAssignments ? maybeInput : commandOrInput;
  const outputSavePath = isExplicitShuffle || isApplyAssignments ? maybeOutput : maybeInput;
  const assignmentPath = isApplyAssignments ? maybeAssignmentPath : null;

  if (!inputSavePath || !outputSavePath || (isApplyAssignments && !assignmentPath)) {
    throw new Error("Usage: node mutate-save.js shuffle <inputSavePath> <outputSavePath> OR node mutate-save.js apply-assignments <inputSavePath> <outputSavePath> <assignmentsJsonPath>");
  }

  let Parser;
  try {
    ({ Parser } = require("@etothepii/satisfactory-file-parser"));
  } catch (error) {
    throw new Error(`Could not load @etothepii/satisfactory-file-parser. Run 'npm install' in ${__dirname}. ${error.message}`);
  }

  log(`Reading ${inputSavePath}`);
  const inputBytes = fs.readFileSync(inputSavePath);
  const saveName = path.basename(inputSavePath, path.extname(inputSavePath));
  const save = Parser.ParseSave(saveName, toExactArrayBuffer(inputBytes), {
    throwErrors: false,
    onProgressCallback: (progress, message) => {
      if (message) {
        log(`parse ${Math.round(progress * 100)}% ${message}`);
      }
    }
  });

  const nodeCapabilityStats = collectOrdinaryResourceNodeCapabilityStats(save);
  const ordinaryNodes = collectOrdinaryResourceNodes(save);
  const wellNodes = collectWellNodes(save);
  if (isApplyAssignments && ordinaryNodes.length === 0) {
    const debugPath = path.join(path.dirname(outputSavePath), "save-debug-shape.json");
    fs.mkdirSync(path.dirname(outputSavePath), { recursive: true });
    dumpDebugShape(save, debugPath);
    throw new Error(formatMutationCapabilityError(nodeCapabilityStats, debugPath));
  }

  const mutationResult = isApplyAssignments
    ? applyAssignments(ordinaryNodes, wellNodes, readAssignments(assignmentPath))
    : shuffleResourceAssignments(ordinaryNodes);

  if (mutationResult.removeIds && mutationResult.removeIds.length > 0) {
    const removalResult = removeObjectsById(save, mutationResult.removeIds);
    mutationResult.removedNodes = removalResult.removedNodes;
    mutationResult.destroyedActorRefsAdded = removalResult.destroyedActorRefsAdded;
    mutationResult.nodesChanged += mutationResult.removedNodes;
    mutationResult.log = [
      mutationResult.log,
      `Removed ${removalResult.removedNodes} resource node objects and registered ${removalResult.destroyedActorRefsAdded} destroyed actor references.`
    ].join("\n");
  }

  const outputDirectory = path.dirname(outputSavePath);
  fs.mkdirSync(outputDirectory, { recursive: true });

  if (ordinaryNodes.length === 0) {
    const debugPath = path.join(outputDirectory, "save-debug-shape.json");
    dumpDebugShape(save, debugPath);
    log(`No ordinary resource nodes were changed. Wrote debug shape to ${debugPath}`);
  }

  log(`Writing ${outputSavePath}`);
  let header;
  const chunks = [];
  Parser.WriteSave(
    save,
    (nextHeader) => {
      header = nextHeader;
    },
    (chunk) => {
      chunks.push(chunk);
    }
  );

  if (!header) {
    throw new Error("Parser did not produce a save header.");
  }

  fs.writeFileSync(outputSavePath, Buffer.concat([Buffer.from(header), ...chunks.map((chunk) => Buffer.from(chunk))]));

  writeResult({
    success: true,
    candidateNodesFound: ordinaryNodes.length,
    nodesChanged: mutationResult.nodesChanged,
    outputSavePath,
    log: mutationResult.log,
    warnings: ordinaryNodes.length === 0 ? ["No ordinary resource nodes were found. Inspect save-debug-shape.json."] : []
  });
}

function readAssignments(assignmentPath) {
  const assignments = JSON.parse(fs.readFileSync(assignmentPath, "utf8"));
  return Array.isArray(assignments) ? assignments : [assignments];
}

function inferOutputSavePath(argv) {
  const command = String(argv[2] || "").toLowerCase();
  if (command === "shuffle" || command === "apply-assignments") {
    return argv[4] || "";
  }

  return argv[3] || "";
}

function applyAssignments(nodes, wellNodes, assignments) {
  const byId = new Map(nodes.map((node) => [node.id, node]));
  const wellsById = new Map(wellNodes.map((node) => [node.id, node]));
  let resourcesChanged = 0;
  let puritiesChanged = 0;
  let wellResourcesChanged = 0;
  let wellSatelliteResourcesChanged = 0;
  let wellSatellitePuritiesChanged = 0;
  let nodesChanged = 0;
  let missing = 0;
  let unsupported = 0;
  const removeIds = [];

  for (const assignment of assignments) {
    const assignmentId = assignment.id ?? assignment.Id;
    const assignmentResourceType = assignment.resourceType ?? assignment.ResourceType;
    const assignmentPurity = assignment.purity ?? assignment.Purity;
    const well = wellsById.get(assignmentId);
    if (well) {
      const result = applyWellAssignment(well, assignment);
      if (!result.supported) {
        unsupported += 1;
        continue;
      }

      wellResourcesChanged += result.coreResourceChanged ? 1 : 0;
      wellSatelliteResourcesChanged += result.satelliteResourcesChanged;
      wellSatellitePuritiesChanged += result.satellitePuritiesChanged;
      if (result.coreResourceChanged || result.satelliteResourcesChanged || result.satellitePuritiesChanged) {
        nodesChanged += 1;
      }

      continue;
    }

    const node = byId.get(assignmentId);
    if (!node) {
      missing += 1;
      continue;
    }

    if (isEmptyResource(assignmentResourceType)) {
      removeIds.push(assignmentId);
      continue;
    }

    const resourcePath = resourceTypeToPath(assignmentResourceType);
    const purityValue = purityToSaveValue(assignmentPurity);
    if (!resourcePath || !purityValue) {
      unsupported += 1;
      continue;
    }

    const resourceChanged = setResourcePath(node.obj, resourcePath);
    const purityChanged = setPurityValue(node.obj, purityValue);
    if (resourceChanged) {
      resourcesChanged += 1;
    }

    if (purityChanged) {
      puritiesChanged += 1;
    }

    if (resourceChanged || purityChanged) {
      nodesChanged += 1;
    }
  }

  return {
    nodesChanged,
    removeIds,
    removedNodes: 0,
    log: [
      `Applied ${assignments.length} preview assignments to ${nodes.length} ordinary resource nodes.`,
      `Resources changed on ${resourcesChanged} nodes; purities changed on ${puritiesChanged} nodes.`,
      `Well cores changed on ${wellResourcesChanged} wells; satellite resources changed on ${wellSatelliteResourcesChanged} satellites; satellite purities changed on ${wellSatellitePuritiesChanged} satellites.`,
      `Nodes marked for removal: ${removeIds.length}.`,
      `Missing assignment targets: ${missing}. Unsupported assignments: ${unsupported}.`,
      `Geysers were ignored.`
    ].join("\n")
  };
}

function applyWellAssignment(well, assignment) {
  const assignmentResourceType = assignment.resourceType ?? assignment.ResourceType;
  const resourcePath = wellResourceTypeToPath(assignmentResourceType);
  if (!resourcePath) {
    return { supported: false };
  }

  const coreResourceChanged = setResourcePath(well.obj, resourcePath);
  let satelliteResourcesChanged = 0;
  let satellitePuritiesChanged = 0;
  for (const satellite of well.satellites) {
    if (setResourcePath(satellite.obj, resourcePath)) {
      satelliteResourcesChanged += 1;
    }
  }

  const satelliteAssignments = assignment.satellites ?? assignment.Satellites ?? [];
  const satelliteAssignmentsById = new Map(satelliteAssignments.map((satellite) => [satellite.id ?? satellite.Id, satellite]));
  for (const satellite of well.satellites) {
    const satelliteAssignment = satelliteAssignmentsById.get(satellite.id);
    if (!satelliteAssignment) {
      continue;
    }

    const purityValue = purityToSaveValue(satelliteAssignment.purity ?? satelliteAssignment.Purity);
    if (!purityValue) {
      continue;
    }

    if (setPurityValue(satellite.obj, purityValue)) {
      satellitePuritiesChanged += 1;
    }
  }

  return {
    supported: true,
    coreResourceChanged,
    satelliteResourcesChanged,
    satellitePuritiesChanged
  };
}

function removeObjectsById(save, ids) {
  const idSet = new Set(ids);
  let removedNodes = 0;
  let destroyedActorRefsAdded = 0;
  const levels = Array.isArray(save.levels) ? save.levels : Object.values(save.levels || {});
  for (const level of levels) {
    if (!Array.isArray(level.objects)) {
      continue;
    }

    const remainingObjects = [];
    for (const obj of level.objects) {
      const objectId = obj.instanceName || obj.pathName;
      if (!idSet.has(objectId)) {
        remainingObjects.push(obj);
        continue;
      }

      destroyedActorRefsAdded += registerDestroyedActor(save, level, obj);
      removedNodes += 1;
    }

    level.objects = remainingObjects;
  }

  return { removedNodes, destroyedActorRefsAdded };
}

function registerDestroyedActor(save, level, obj) {
  const pathName = obj.instanceName || obj.pathName;
  if (!pathName) {
    return 0;
  }

  const levelName = level.name || obj.rootObject || "Persistent_Level";
  const persistentLevel = getPersistentLevel(save) || level;
  persistentLevel.destroyedActorsMap = persistentLevel.destroyedActorsMap || {};
  const destroyedActors = persistentLevel.destroyedActorsMap[levelName] || [];
  persistentLevel.destroyedActorsMap[levelName] = destroyedActors;

  if (destroyedActors.some((actor) => actor.levelName === levelName && actor.pathName === pathName)) {
    return 0;
  }

  destroyedActors.push({ levelName, pathName });
  return 1;
}

function getPersistentLevel(save) {
  const levels = save.levels || {};
  if (levels.Persistent_Level) {
    return levels.Persistent_Level;
  }

  const levelValues = Array.isArray(levels) ? levels : Object.values(levels);
  return levelValues.find((level) => level && level.destroyedActorsMap) || levelValues.find((level) => level && level.name === "Persistent_Level") || null;
}

function collectOrdinaryResourceNodes(save) {
  const nodes = [];
  for (const obj of enumerateSaveObjects(save)) {
    if (!looksLikeResourceNode(obj)) {
      continue;
    }

    const resourceProperty = obj.properties && obj.properties.mResourceClassOverride;
    const purityProperty = obj.properties && obj.properties.mPurityOverride;
    const resourcePath = resourceProperty && resourceProperty.value && resourceProperty.value.pathName;
    const purityValue = purityProperty && purityProperty.value && purityProperty.value.value;
    const translation = obj.transform && obj.transform.translation;

    if (!resourcePath || !purityValue || !translation) {
      continue;
    }

    nodes.push({
      obj,
      id: obj.instanceName || obj.pathName || `node_${nodes.length + 1}`,
      x: numberOrZero(translation.x),
      y: numberOrZero(translation.y),
      resourcePath,
      purityValue
    });
  }

  return nodes;
}

function collectWellNodes(save) {
  const cores = [];
  const satellites = [];
  for (const obj of enumerateSaveObjects(save)) {
    if (isFrackingCore(obj)) {
      const resourceProperty = obj.properties && obj.properties.mResourceClassOverride;
      const resourcePath = resourceProperty && resourceProperty.value && resourceProperty.value.pathName;
      const translation = obj.transform && obj.transform.translation;
      if (!resourcePath || !translation) {
        continue;
      }

      cores.push({
        obj,
        id: obj.instanceName || obj.pathName || `well_${cores.length + 1}`,
        x: numberOrZero(translation.x),
        y: numberOrZero(translation.y),
        z: numberOrZero(translation.z),
        resourcePath,
        satellites: []
      });
      continue;
    }

    if (isFrackingSatellite(obj)) {
      const resourceProperty = obj.properties && obj.properties.mResourceClassOverride;
      const purityProperty = obj.properties && obj.properties.mPurityOverride;
      const resourcePath = resourceProperty && resourceProperty.value && resourceProperty.value.pathName;
      const purityValue = purityProperty && purityProperty.value && purityProperty.value.value;
      const translation = obj.transform && obj.transform.translation;
      if (!resourcePath || !purityValue || !translation) {
        continue;
      }

      satellites.push({
        obj,
        id: obj.instanceName || obj.pathName || `satellite_${satellites.length + 1}`,
        x: numberOrZero(translation.x),
        y: numberOrZero(translation.y),
        z: numberOrZero(translation.z),
        resourcePath,
        purityValue
      });
    }
  }

  for (const satellite of satellites) {
    const nearestCore = cores
      .map((core) => ({
        core,
        distanceSquared:
          Math.pow(core.x - satellite.x, 2) +
          Math.pow(core.y - satellite.y, 2) +
          Math.pow(core.z - satellite.z, 2)
      }))
      .sort((left, right) => left.distanceSquared - right.distanceSquared)[0]?.core;
    nearestCore?.satellites.push(satellite);
  }

  return cores;
}

function collectOrdinaryResourceNodeCapabilityStats(save) {
  const stats = {
    ordinaryNodes: 0,
    resourceOverrides: 0,
    purityOverrides: 0,
    bothOverrides: 0
  };

  for (const obj of enumerateSaveObjects(save)) {
    if (!looksLikeResourceNode(obj)) {
      continue;
    }

    const hasResourceOverride = !!(obj.properties && obj.properties.mResourceClassOverride && obj.properties.mResourceClassOverride.value && obj.properties.mResourceClassOverride.value.pathName);
    const hasPurityOverride = !!(obj.properties && obj.properties.mPurityOverride && obj.properties.mPurityOverride.value && obj.properties.mPurityOverride.value.value);
    stats.ordinaryNodes += 1;
    if (hasResourceOverride) {
      stats.resourceOverrides += 1;
    }

    if (hasPurityOverride) {
      stats.purityOverrides += 1;
    }

    if (hasResourceOverride && hasPurityOverride) {
      stats.bothOverrides += 1;
    }
  }

  return stats;
}

function formatMutationCapabilityError(stats, debugPath) {
  const details = `Ordinary nodes: ${stats.ordinaryNodes}. Resource overrides: ${stats.resourceOverrides}. Purity overrides: ${stats.purityOverrides}. Nodes with both overrides: ${stats.bothOverrides}.`;
  let guidance;
  if (stats.ordinaryNodes === 0) {
    guidance = "This save does not contain serialized ordinary resource-node actors, so node assignments cannot be applied.";
  } else if (stats.resourceOverrides === 0 && stats.purityOverrides === 0) {
    guidance = "This save uses default resources and default purities. Satisfactory did not serialize mutable resource or purity overrides for ordinary nodes, so this app cannot safely apply node assignments.";
  } else if (stats.resourceOverrides === 0) {
    guidance = "This save has randomized purities but default resources. Resource overrides are missing, so this app cannot safely apply a full resource shuffle.";
  } else if (stats.purityOverrides === 0) {
    guidance = "This save has randomized resources but default purities. Purity overrides are missing, so this app cannot safely save purity assignments.";
  } else {
    guidance = "This save only has both resource and purity overrides on some ordinary nodes, so this app cannot safely apply a full node assignment set.";
  }

  return [
    guidance,
    details,
    "Create or load a save with both resource randomization and purity randomization enabled.",
    `Wrote debug shape to ${debugPath}.`
  ].join("\n");
}

function shuffleResourceAssignments(nodes) {
  const resourceGroups = new Map();
  const resourceCounts = new Map();
  const purityCounts = new Map();

  for (const node of nodes) {
    if (!resourceGroups.has(node.resourcePath)) {
      resourceGroups.set(node.resourcePath, []);
    }

    const assignment = {
      resourcePath: node.resourcePath,
      purityValue: node.purityValue
    };
    resourceGroups.get(node.resourcePath).push(assignment);
    increment(resourceCounts, resourceLabel(node.resourcePath));
    increment(purityCounts, node.purityValue);
  }

  const groups = [...resourceGroups.entries()]
    .map(([resourcePath, assignments]) => ({
      resourcePath,
      assignments: assignments.sort((a, b) => purityRank(a.purityValue) - purityRank(b.purityValue))
    }))
    .sort(() => Math.random() - 0.5);

  const clusterPositions = buildSpatialClusters(nodes, groups.map((group) => group.assignments.length));
  let resourcesChanged = 0;
  let puritiesChanged = 0;
  let nodesChanged = 0;

  for (let groupIndex = 0; groupIndex < groups.length; groupIndex += 1) {
    const group = groups[groupIndex];
    const positions = clusterPositions[groupIndex];
    for (let assignmentIndex = 0; assignmentIndex < group.assignments.length; assignmentIndex += 1) {
      const assignment = group.assignments[assignmentIndex];
      const node = positions[assignmentIndex];
      if (!node) {
        throw new Error("Internal shuffle error: not enough node positions for assignments.");
      }

      const resourceChanged = setResourcePath(node.obj, assignment.resourcePath);
      const purityChanged = setPurityValue(node.obj, assignment.purityValue);
      if (resourceChanged) {
        resourcesChanged += 1;
      }

      if (purityChanged) {
        puritiesChanged += 1;
      }

      if (resourceChanged || purityChanged) {
        nodesChanged += 1;
      }
    }
  }

  return {
    clusterCount: groups.length,
    nodesChanged,
    resourcesChanged,
    puritiesChanged,
    resourceCounts,
    purityCounts,
    log: [
      `Shuffled ${nodes.length} ordinary resource nodes into ${groups.length} resource clusters.`,
      `Resources changed on ${resourcesChanged} nodes; purities changed on ${puritiesChanged} nodes.`,
      `Wells and geysers were ignored.`,
      `Resource composition: ${formatCounts(resourceCounts)}.`,
      `Purity composition: ${formatCounts(purityCounts)}.`
    ].join("\n")
  };
}

function buildSpatialClusters(nodes, clusterSizes) {
  const graph = buildKNearestNeighborGraph(nodes, NEIGHBOR_COUNT);
  const seedIndexes = chooseSeedIndexes(nodes, clusterSizes.length);
  const clusters = clusterSizes.map(() => []);
  const clusterByNode = Array(nodes.length).fill(-1);
  let assignedCount = 0;

  for (let clusterIndex = 0; clusterIndex < seedIndexes.length; clusterIndex += 1) {
    if (clusterSizes[clusterIndex] <= 0) {
      continue;
    }

    const seedIndex = seedIndexes[clusterIndex];
    clusters[clusterIndex].push(seedIndex);
    clusterByNode[seedIndex] = clusterIndex;
    assignedCount += 1;
  }

  while (assignedCount < nodes.length) {
    let clusterIndex = chooseClusterWithFrontierToGrow(clusters, clusterSizes, clusterByNode, graph);
    if (clusterIndex < 0) {
      clusterIndex = chooseClusterToGrow(clusters, clusterSizes);
    }

    if (clusterIndex < 0) {
      break;
    }

    let nextNodeIndex = findNearestFrontierNode(clusterIndex, clusters, clusterByNode, graph, nodes);
    if (nextNodeIndex < 0) {
      nextNodeIndex = findNearestUnassignedNode(clusterIndex, clusters, clusterByNode, nodes);
    }

    if (nextNodeIndex < 0) {
      break;
    }

    clusters[clusterIndex].push(nextNodeIndex);
    clusterByNode[nextNodeIndex] = clusterIndex;
    assignedCount += 1;
  }

  runConnectivityPreservingLocalSearch(clusters, clusterByNode, graph, nodes);
  return clusters.map((cluster) => sortCluster(cluster, nodes).map((nodeIndex) => nodes[nodeIndex]));
}

function buildKNearestNeighborGraph(nodes, neighborCount) {
  const edges = nodes.map(() => new Set());
  for (let nodeIndex = 0; nodeIndex < nodes.length; nodeIndex += 1) {
    const nearest = nodes
      .map((node, candidateIndex) => ({ candidateIndex, distance: distanceSquared(nodes[nodeIndex], node) }))
      .filter((entry) => entry.candidateIndex !== nodeIndex)
      .sort((a, b) => a.distance - b.distance)
      .slice(0, Math.min(neighborCount, nodes.length - 1));

    for (const entry of nearest) {
      edges[nodeIndex].add(entry.candidateIndex);
      edges[entry.candidateIndex].add(nodeIndex);
    }
  }

  return edges.map((edgeSet) => [...edgeSet]);
}

function chooseClusterToGrow(clusters, clusterSizes) {
  return clusters
    .map((cluster, index) => ({ index, fillRatio: cluster.length / clusterSizes[index] }))
    .filter((entry) => clusters[entry.index].length < clusterSizes[entry.index])
    .sort((a, b) => a.fillRatio - b.fillRatio || Math.random() - 0.5)[0]?.index ?? -1;
}

function chooseClusterWithFrontierToGrow(clusters, clusterSizes, clusterByNode, graph) {
  return clusters
    .map((cluster, index) => ({ index, fillRatio: cluster.length / clusterSizes[index] }))
    .filter((entry) => clusters[entry.index].length < clusterSizes[entry.index] && hasUnassignedFrontier(clusters[entry.index], clusterByNode, graph))
    .sort((a, b) => a.fillRatio - b.fillRatio || Math.random() - 0.5)[0]?.index ?? -1;
}

function hasUnassignedFrontier(cluster, clusterByNode, graph) {
  return cluster.some((nodeIndex) => graph[nodeIndex].some((neighborIndex) => clusterByNode[neighborIndex] < 0));
}

function findNearestFrontierNode(clusterIndex, clusters, clusterByNode, graph, nodes) {
  let bestNodeIndex = -1;
  let bestDistance = Number.POSITIVE_INFINITY;
  for (const clusterNodeIndex of clusters[clusterIndex]) {
    for (const neighborIndex of graph[clusterNodeIndex]) {
      if (clusterByNode[neighborIndex] >= 0) {
        continue;
      }

      const distance = distanceSquared(nodes[clusterNodeIndex], nodes[neighborIndex]);
      if (distance < bestDistance || (Math.abs(distance - bestDistance) < 0.0001 && Math.random() < 0.5)) {
        bestDistance = distance;
        bestNodeIndex = neighborIndex;
      }
    }
  }

  return bestNodeIndex;
}

function findNearestUnassignedNode(clusterIndex, clusters, clusterByNode, nodes) {
  return nodes
    .map((node, nodeIndex) => ({ nodeIndex, distance: distanceSquaredToCluster(nodeIndex, clusters[clusterIndex], nodes) }))
    .filter((entry) => clusterByNode[entry.nodeIndex] < 0)
    .sort((a, b) => a.distance - b.distance || Math.random() - 0.5)[0]?.nodeIndex ?? -1;
}

function runConnectivityPreservingLocalSearch(clusters, clusterByNode, graph, nodes) {
  for (let pass = 0; pass < LOCAL_SEARCH_PASSES; pass += 1) {
    let improved = false;
    const boundaryNodes = nodes
      .map((node, nodeIndex) => nodeIndex)
      .filter((nodeIndex) => graph[nodeIndex].some((neighborIndex) => clusterByNode[neighborIndex] !== clusterByNode[nodeIndex]))
      .sort(() => Math.random() - 0.5);

    for (const firstNodeIndex of boundaryNodes) {
      const firstClusterIndex = clusterByNode[firstNodeIndex];
      const neighbors = graph[firstNodeIndex]
        .filter((secondNodeIndex) => clusterByNode[secondNodeIndex] !== firstClusterIndex)
        .sort(() => Math.random() - 0.5);

      for (const secondNodeIndex of neighbors) {
        const secondClusterIndex = clusterByNode[secondNodeIndex];
        if (!swapImprovesCompactness(firstNodeIndex, secondNodeIndex, firstClusterIndex, secondClusterIndex, clusters, nodes)) {
          continue;
        }

        if (!isConnectedAfterSwap(clusters[firstClusterIndex], firstNodeIndex, secondNodeIndex, graph) ||
            !isConnectedAfterSwap(clusters[secondClusterIndex], secondNodeIndex, firstNodeIndex, graph)) {
          continue;
        }

        replaceNode(clusters[firstClusterIndex], firstNodeIndex, secondNodeIndex);
        replaceNode(clusters[secondClusterIndex], secondNodeIndex, firstNodeIndex);
        clusterByNode[firstNodeIndex] = secondClusterIndex;
        clusterByNode[secondNodeIndex] = firstClusterIndex;
        improved = true;
        break;
      }
    }

    if (!improved) {
      break;
    }
  }
}

function swapImprovesCompactness(firstNodeIndex, secondNodeIndex, firstClusterIndex, secondClusterIndex, clusters, nodes) {
  const before = clusterCompactness(clusters[firstClusterIndex], nodes) + clusterCompactness(clusters[secondClusterIndex], nodes);
  const after = clusterCompactnessAfterSwap(clusters[firstClusterIndex], firstNodeIndex, secondNodeIndex, nodes) +
    clusterCompactnessAfterSwap(clusters[secondClusterIndex], secondNodeIndex, firstNodeIndex, nodes);
  return after + 0.0001 < before;
}

function clusterCompactness(cluster, nodes) {
  return clusterCompactnessMapped(cluster, (nodeIndex) => nodeIndex, nodes);
}

function clusterCompactnessAfterSwap(cluster, removedNodeIndex, addedNodeIndex, nodes) {
  return clusterCompactnessMapped(cluster, (nodeIndex) => nodeIndex === removedNodeIndex ? addedNodeIndex : nodeIndex, nodes);
}

function clusterCompactnessMapped(cluster, mapNodeIndex, nodes) {
  const centerX = cluster.reduce((total, nodeIndex) => total + nodes[mapNodeIndex(nodeIndex)].x, 0) / cluster.length;
  const centerY = cluster.reduce((total, nodeIndex) => total + nodes[mapNodeIndex(nodeIndex)].y, 0) / cluster.length;
  return cluster.reduce((total, nodeIndex) => {
    const mappedIndex = mapNodeIndex(nodeIndex);
    const dx = nodes[mappedIndex].x - centerX;
    const dy = nodes[mappedIndex].y - centerY;
    return total + dx * dx + dy * dy;
  }, 0);
}

function isConnectedAfterSwap(cluster, removedNodeIndex, addedNodeIndex, graph) {
  if (cluster.length <= 1) {
    return true;
  }

  const clusterSet = new Set(cluster.filter((nodeIndex) => nodeIndex !== removedNodeIndex));
  clusterSet.add(addedNodeIndex);
  const start = clusterSet.values().next().value;
  const visited = new Set([start]);
  const queue = [start];

  while (queue.length > 0) {
    const current = queue.shift();
    for (const neighbor of graph[current]) {
      if (!clusterSet.has(neighbor) || visited.has(neighbor)) {
        continue;
      }

      visited.add(neighbor);
      queue.push(neighbor);
    }
  }

  return visited.size === clusterSet.size;
}

function replaceNode(cluster, oldNodeIndex, newNodeIndex) {
  const index = cluster.indexOf(oldNodeIndex);
  if (index >= 0) {
    cluster[index] = newNodeIndex;
  }
}

function sortCluster(cluster, nodes) {
  const centerX = cluster.reduce((total, nodeIndex) => total + nodes[nodeIndex].x, 0) / cluster.length;
  const centerY = cluster.reduce((total, nodeIndex) => total + nodes[nodeIndex].y, 0) / cluster.length;
  return cluster.sort((a, b) => Math.atan2(nodes[a].y - centerY, nodes[a].x - centerX) - Math.atan2(nodes[b].y - centerY, nodes[b].x - centerX));
}

function chooseSeedIndexes(nodes, count) {
  const seedIndexes = [Math.floor(Math.random() * nodes.length)];
  while (seedIndexes.length < count) {
    const next = nodes
      .map((node, nodeIndex) => nodeIndex)
      .filter((nodeIndex) => !seedIndexes.includes(nodeIndex))
      .sort((a, b) => nearestSeedDistanceSquared(b, seedIndexes, nodes) - nearestSeedDistanceSquared(a, seedIndexes, nodes) || Math.random() - 0.5)[0];

    if (next === undefined) {
      break;
    }

    seedIndexes.push(next);
  }

  return seedIndexes;
}

function nearestSeedDistanceSquared(nodeIndex, seedIndexes, nodes) {
  return Math.min(...seedIndexes.map((seedIndex) => distanceSquared(nodes[nodeIndex], nodes[seedIndex])));
}

function distanceSquaredToCluster(nodeIndex, cluster, nodes) {
  return Math.min(...cluster.map((clusterNodeIndex) => distanceSquared(nodes[nodeIndex], nodes[clusterNodeIndex])));
}

function distanceSquared(first, second) {
  const dx = first.x - second.x;
  const dy = first.y - second.y;
  return dx * dx + dy * dy;
}

function* enumerateSaveObjects(save) {
  const levels = Array.isArray(save.levels) ? save.levels : Object.values(save.levels || {});
  for (const level of levels) {
    for (const obj of level.objects || []) {
      yield obj;
    }
  }
}

function looksLikeResourceNode(obj) {
  if (looksLikeWellOrGeyser(obj)) {
    return false;
  }

  const directText = [
    obj.typePath,
    obj.instanceName,
    obj.pathName,
    obj.rootObject,
    obj.parentObjectName,
    obj.parentObjectRoot
  ].filter(Boolean).join(" ");

  if (/ResourceNode|ExtractableResource/i.test(directText)) {
    return true;
  }

  const properties = obj.properties || {};
  const propertyNames = Object.keys(properties);
  if (propertyNames.some((name) => /^m(Resource(Type|Class)(Override)?|Purity(Override)?)$/i.test(name))) {
    return true;
  }

  return RESOURCE_HINTS.some((hint) => JSON.stringify(properties).includes(hint));
}

function looksLikeWellOrGeyser(obj) {
  const directText = [
    obj.typePath,
    obj.instanceName
  ].filter(Boolean).join(" ");

  return /BP_FrackingSatellite|BP_FrackingCore|BP_ResourceNodeGeyser/i.test(directText);
}

function isFrackingCore(obj) {
  return /BP_FrackingCore/i.test([obj.typePath, obj.instanceName].filter(Boolean).join(" "));
}

function isFrackingSatellite(obj) {
  return /BP_FrackingSatellite/i.test([obj.typePath, obj.instanceName].filter(Boolean).join(" "));
}

function setResourcePath(obj, resourcePath) {
  const current = obj.properties && obj.properties.mResourceClassOverride && obj.properties.mResourceClassOverride.value && obj.properties.mResourceClassOverride.value.pathName;
  if (!current || current === resourcePath) {
    return false;
  }

  obj.properties.mResourceClassOverride.value.pathName = resourcePath;
  return true;
}

function setPurityValue(obj, purityValue) {
  const current = obj.properties && obj.properties.mPurityOverride && obj.properties.mPurityOverride.value && obj.properties.mPurityOverride.value.value;
  if (!current || current === purityValue) {
    return false;
  }

  obj.properties.mPurityOverride.value.value = purityValue;
  return true;
}

function mortonOrder(worldX, worldY) {
  const normalizedX = clamp01((worldX - WORLD_BOUNDS.minX) / (WORLD_BOUNDS.maxX - WORLD_BOUNDS.minX));
  const normalizedY = clamp01((worldY - WORLD_BOUNDS.minY) / (WORLD_BOUNDS.maxY - WORLD_BOUNDS.minY));
  const x = Math.round(normalizedX * 65535);
  const y = Math.round(normalizedY * 65535);
  return interleave16(x, y);
}

function interleave16(x, y) {
  let result = 0;
  for (let bit = 0; bit < 16; bit += 1) {
    result += ((x >> bit) & 1) * (2 ** (2 * bit));
    result += ((y >> bit) & 1) * (2 ** (2 * bit + 1));
  }

  return result;
}

function clamp01(value) {
  return Math.max(0, Math.min(1, value));
}

function purityRank(purity) {
  switch (String(purity || "").toLowerCase()) {
    case "rp_inpure":
      return 0;
    case "rp_normal":
      return 1;
    case "rp_pure":
      return 2;
    default:
      return 3;
  }
}

function resourceLabel(resourcePath) {
  const last = String(resourcePath || "").split("/").at(-1) || resourcePath;
  return last.replace(/^Desc_/, "").replace(/\.Desc_.+$/, "");
}

function resourceTypeToPath(resourceType) {
  return RESOURCE_CLASS_PATHS[String(resourceType || "").trim().toLowerCase()];
}

function wellResourceTypeToPath(resourceType) {
  const resourcePath = resourceTypeToPath(resourceType);
  if (!resourcePath || !/Desc_(Water|NitrogenGas|LiquidOil)\./i.test(resourcePath)) {
    return null;
  }

  return resourcePath;
}

function isEmptyResource(resourceType) {
  const normalized = String(resourceType || "").trim().toLowerCase();
  return normalized === "empty" || normalized === "none" || normalized === "removed";
}

function purityToSaveValue(purity) {
  switch (String(purity || "").trim().toLowerCase()) {
    case "impure":
    case "inpure":
    case "rp_inpure":
      return "RP_Inpure";
    case "normal":
    case "rp_normal":
      return "RP_Normal";
    case "pure":
    case "rp_pure":
      return "RP_Pure";
    default:
      return null;
  }
}

function increment(map, key) {
  map.set(key, (map.get(key) || 0) + 1);
}

function formatCounts(map) {
  return [...map.entries()]
    .sort((a, b) => b[1] - a[1] || String(a[0]).localeCompare(String(b[0])))
    .map(([key, count]) => `${key}=${count}`)
    .join(", ");
}

function numberOrZero(value) {
  return Number.isFinite(value) ? value : 0;
}

function dumpDebugShape(save, outputPath) {
  const highValueSamples = [];
  const fallbackSamples = [];
  for (const obj of enumerateSaveObjects(save)) {
    const searchable = JSON.stringify({
      typePath: obj.typePath,
      instanceName: obj.instanceName,
      properties: obj.properties,
      specialProperties: obj.specialProperties
    });

    if (/resource|purity|node|extractor/i.test(searchable)) {
      const sample = sanitizeSample(obj);
      if (/mResource(Type|Class)(Override)?|mPurity(Override)?/i.test(searchable)) {
        highValueSamples.push(sample);
      } else {
        fallbackSamples.push(sample);
      }
    }

    if (highValueSamples.length >= 30) {
      break;
    }
  }

  const samples = [...highValueSamples, ...fallbackSamples].slice(0, 30);
  fs.writeFileSync(outputPath, JSON.stringify({
    notes: "Small sample of objects containing resource, purity, node, or extractor text. This is not the whole save.",
    sampleCount: samples.length,
    samples
  }, null, 2));
}

function sanitizeSample(obj) {
  return truncate({
    typePath: obj.typePath,
    rootObject: obj.rootObject,
    instanceName: obj.instanceName,
    parentObjectName: obj.parentObjectName,
    transform: obj.transform,
    propertyNames: Object.keys(obj.properties || {}),
    properties: obj.properties,
    specialPropertyType: obj.specialProperties && obj.specialProperties.type
  }, 4);
}

function truncate(value, depth) {
  if (depth <= 0) {
    return Array.isArray(value) ? `[Array(${value.length})]` : typeof value === "object" && value !== null ? "{...}" : value;
  }

  if (Array.isArray(value)) {
    return value.slice(0, 8).map((item) => truncate(item, depth - 1));
  }

  if (!value || typeof value !== "object") {
    return typeof value === "string" && value.length > 500 ? `${value.slice(0, 500)}...` : value;
  }

  const result = {};
  for (const key of Object.keys(value).slice(0, 25)) {
    result[key] = truncate(value[key], depth - 1);
  }

  return result;
}

function toExactArrayBuffer(buffer) {
  return buffer.buffer.slice(buffer.byteOffset, buffer.byteOffset + buffer.byteLength);
}

function writeResult(result) {
  process.stdout.write(JSON.stringify({
    success: Boolean(result.success),
    candidateNodesFound: result.candidateNodesFound || 0,
    nodesChanged: result.nodesChanged || 0,
    outputSavePath: result.outputSavePath || "",
    log: result.log || "",
    errorMessage: result.errorMessage || null,
    warnings: result.warnings || []
  }));
}

function log(message) {
  process.stderr.write(`${message}\n`);
}
