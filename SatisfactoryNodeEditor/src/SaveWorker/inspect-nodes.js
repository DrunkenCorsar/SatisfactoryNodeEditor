const fs = require("node:fs");
const path = require("node:path");

const RESOURCE_PATH_LABELS = [
  ["OreBauxite", "Bauxite"],
  ["OreIron", "Iron"],
  ["OreCopper", "Copper"],
  ["Stone", "Limestone"],
  ["Coal", "Coal"],
  ["OreGold", "Caterium"],
  ["Sulfur", "Sulfur"],
  ["RawQuartz", "Raw Quartz"],
  ["OreUranium", "Uranium"],
  ["SAM", "SAM"],
  ["CrudeOil", "Crude Oil"],
  ["LiquidOil", "Crude Oil"],
  ["Water", "Water"],
  ["NitrogenGas", "Nitrogen"]
];

main().catch((error) => {
  process.stderr.write(`${error.stack || error}\n`);
  process.exitCode = 1;
});

async function main() {
  const [, , inputSavePath, outputJsonPath] = process.argv;
  if (!inputSavePath || !outputJsonPath) {
    throw new Error("Usage: node inspect-nodes.js <inputSavePath> <outputJsonPath>");
  }

  let Parser;
  try {
    ({ Parser } = require("@etothepii/satisfactory-file-parser"));
  } catch (error) {
    throw new Error(`Could not load @etothepii/satisfactory-file-parser. Run 'npm install' in ${__dirname}. ${error.message}`);
  }

  log(`Inspecting ${inputSavePath}`);
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

  let candidates = 0;
  let missingCoordinates = 0;
  const nodes = [];
  const wellCores = [];
  const wellSatellites = [];

  for (const obj of enumerateSaveObjects(save)) {
    if (!looksLikeInspectableResourceNode(obj)) {
      continue;
    }

    candidates += 1;
    const translation = obj.transform && obj.transform.translation;
    if (!translation) {
      missingCoordinates += 1;
    }

    const record = {
      id: obj.instanceName || obj.pathName || `unknown_${candidates}`,
      nodeKind: getNodeKind(obj),
      resourceType: getResourceType(obj),
      purity: getPurity(obj),
      worldX: numberOrZero(translation && translation.x),
      worldY: numberOrZero(translation && translation.y),
      worldZ: numberOrZero(translation && translation.z)
    };

    if (isFrackingCore(obj)) {
      wellCores.push(record);
      continue;
    }

    if (isFrackingSatellite(obj)) {
      wellSatellites.push(record);
      continue;
    }

    nodes.push(record);
  }

  for (const core of wellCores) {
    core.satellites = [];
    nodes.push(core);
  }

  assignSatellitesToNearestCores(wellCores, wellSatellites);

  fs.mkdirSync(path.dirname(outputJsonPath), { recursive: true });
  fs.writeFileSync(outputJsonPath, JSON.stringify(nodes, null, 2));
  log(`Wrote ${nodes.length} resource node records to ${outputJsonPath}. Candidates: ${candidates}. Missing coordinates: ${missingCoordinates}.`);
}

function* enumerateSaveObjects(save) {
  const levels = Array.isArray(save.levels) ? save.levels : Object.values(save.levels || {});
  for (const level of levels) {
    for (const obj of level.objects || []) {
      yield obj;
    }
  }
}

function looksLikeInspectableResourceNode(obj) {
  const properties = obj.properties || {};
  if (properties.mResourceClassOverride) {
    return true;
  }

  const directText = [obj.typePath, obj.instanceName].filter(Boolean).join(" ");
  return /BP_ResourceNode|BP_FrackingSatellite|BP_FrackingCore/i.test(directText);
}

function getNodeKind(obj) {
  const text = [obj.typePath, obj.instanceName].filter(Boolean).join(" ");
  if (/BP_FrackingSatellite/i.test(text)) {
    return "WellSatellite";
  }

  if (/BP_FrackingCore/i.test(text)) {
    return "Well";
  }

  if (/BP_ResourceNodeGeyser/i.test(text)) {
    return "Geyser";
  }

  return "ResourceNode";
}

function isFrackingCore(obj) {
  return /BP_FrackingCore/i.test([obj.typePath, obj.instanceName].filter(Boolean).join(" "));
}

function isFrackingSatellite(obj) {
  return /BP_FrackingSatellite/i.test([obj.typePath, obj.instanceName].filter(Boolean).join(" "));
}

function assignSatellitesToNearestCores(wellCores, wellSatellites) {
  if (!wellCores.length) {
    return;
  }

  for (const satellite of wellSatellites) {
    const nearestCore = wellCores
      .map((core) => ({
        core,
        distanceSquared:
          Math.pow(core.worldX - satellite.worldX, 2) +
          Math.pow(core.worldY - satellite.worldY, 2) +
          Math.pow(core.worldZ - satellite.worldZ, 2)
      }))
      .sort((left, right) => left.distanceSquared - right.distanceSquared)[0].core;
    nearestCore.satellites.push({
      id: satellite.id,
      displayName: `Satellite ${nearestCore.satellites.length + 1}`,
      purity: satellite.purity
    });
  }
}

function getResourceType(obj) {
  if (getNodeKind(obj) === "Geyser") {
    return "Geothermal";
  }

  const resourcePath = obj.properties && obj.properties.mResourceClassOverride && obj.properties.mResourceClassOverride.value && obj.properties.mResourceClassOverride.value.pathName;
  if (!resourcePath) {
    return "Unknown";
  }

  const match = RESOURCE_PATH_LABELS.find(([needle]) => resourcePath.includes(needle));
  return match ? match[1] : "Unknown";
}

function getPurity(obj) {
  if (getNodeKind(obj) === "Geyser") {
    return "Not applicable";
  }

  const value = obj.properties && obj.properties.mPurityOverride && obj.properties.mPurityOverride.value && obj.properties.mPurityOverride.value.value;
  switch (String(value || "").toLowerCase()) {
    case "rp_inpure":
      return "Impure";
    case "rp_normal":
      return "Normal";
    case "rp_pure":
      return "Pure";
    default:
      return "Unknown";
  }
}

function numberOrZero(value) {
  return Number.isFinite(value) ? value : 0;
}

function toExactArrayBuffer(buffer) {
  return buffer.buffer.slice(buffer.byteOffset, buffer.byteOffset + buffer.byteLength);
}

function log(message) {
  process.stderr.write(`${message}\n`);
}
