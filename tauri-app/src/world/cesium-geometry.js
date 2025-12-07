import { readFile, BaseDirectory } from "@tauri-apps/plugin-fs";
import { logToRhino } from "../communication/rhino-logger.js";

// Store reference to current model entity
let currentModel = null;

// Load GLB from disk (saved by Rhino) and add it to Cesium viewer
async function addModelFromRhino(viewer, data) {
  await logToRhino("========== CESIUM COORDINATE DEBUG ==========");
  
  // Remove old model if exists
  if (currentModel) {
    viewer.entities.remove(currentModel);
    await logToRhino("Removed previous model");
  }
  
  if (!data || data.error) {
    await logToRhino("ERROR: " + (data && data.error));
    console.error("Rhino error:", data && data.error);
    alert("Error from Rhino: " + (data && data.error));
    return;
  }

  const { glbPath, position } = data;
  
  await logToRhino("RECEIVED FROM RHINO:");
  await logToRhino(`  position.lat: ${position.lat}`);
  await logToRhino(`  position.lon: ${position.lon}`);
  await logToRhino(`  position.height: ${position.height}`);
  
  if (!glbPath) {
    await logToRhino("ERROR: No glbPath");
    return;
  }

  try {
    await logToRhino("Reading GLB file...");

    const relativePath = "McAtlas/mcatlas_massing.glb";
    const bytes = await readFile(relativePath, {
      baseDir: BaseDirectory.Document,
    });

    await logToRhino(`GLB file read: ${bytes.length} bytes`);

    const blob = new Blob([bytes], { type: "model/gltf-binary" });
    const url = URL.createObjectURL(blob);

    // Sample terrain height at model position
    const cartographic = Cesium.Cartographic.fromDegrees(position.lon, position.lat);
    
    await logToRhino("Sampling terrain height...");
    const terrainHeight = viewer.scene.sampleHeight(cartographic);
    await logToRhino(`  Sampled terrain height: ${terrainHeight}`);
    
    const finalHeight = (terrainHeight || 0) + position.height;
    await logToRhino(`  Final height: ${finalHeight}`);

    // Create position
    const positionCartesian = Cesium.Cartesian3.fromDegrees(
      position.lon,
      position.lat,
      finalHeight
    );

    // Create orientation - rotate to align with Rhino coordinates
    // Rhino: +Y = North, +X = East
    // Cesium heading: 0 = North, 90 = East
    // We may need to adjust this value (try 0, 90, 180, 270)
    const heading = Cesium.Math.toRadians(90); // Rotate 180° - adjust if needed
    const pitch = 0;
    const roll = 0;
    const hpr = new Cesium.HeadingPitchRoll(heading, pitch, roll);
    const orientation = Cesium.Transforms.headingPitchRollQuaternion(positionCartesian, hpr);

    await logToRhino(`Creating model with heading: 180°`);

    const modelEntity = viewer.entities.add({
      position: positionCartesian,
      orientation: orientation,
      model: {
        uri: url
      }
    });
    
    currentModel = modelEntity;
    
    await logToRhino("Model entity created successfully");
    viewer.flyTo(modelEntity);
    await logToRhino("========== END DEBUG ==========");

  } catch (error) {
    await logToRhino("ERROR: " + error.message);
    console.error("Error loading model:", error);
    alert("Failed to load model: " + error.message);
  }
}

// Fly to current model (for Target button)
async function flyToCurrentModel(viewer) {
  if (currentModel) {
    viewer.flyTo(currentModel);
  } else {
    alert("No model loaded yet. Click Sync first!");
  }
}

export { addModelFromRhino, flyToCurrentModel };