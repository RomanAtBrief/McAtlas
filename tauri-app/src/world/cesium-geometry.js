import { readFile, BaseDirectory } from "@tauri-apps/plugin-fs";
import { logToRhino } from "../communication/rhino-logger.js";

// Store reference to current model entity
let currentModel = null;

// Load GLB from disk (saved by Rhino) and add it to Cesium viewer
async function addModelFromRhino(viewer, data) {
  await logToRhino("Step 1: Starting...");
  
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
  await logToRhino("Step 2: glbPath = " + glbPath);
  
  if (!glbPath) {
    await logToRhino("ERROR: No glbPath");
    console.error("No glbPath from Rhino");
    return;
  }

  try {
    await logToRhino("Step 3: Reading file from Documents/McAtlas/...");

    const relativePath = "McAtlas/mcatlas_massing.glb";

    const bytes = await readFile(relativePath, {
      baseDir: BaseDirectory.Document,
    });

    await logToRhino("Step 4: File read! Size = " + bytes.length + " bytes");

    const blob = new Blob([bytes], { type: "model/gltf-binary" });
    const url = URL.createObjectURL(blob);
    
    await logToRhino("Step 5: Blob URL created");

    await logToRhino("Step 6: Creating model entity...");

    // Add model as entity
    const modelEntity = viewer.entities.add({
      position: Cesium.Cartesian3.fromDegrees(
        position.lon,
        position.lat,
        position.height
      ),
      model: {
        uri: url
      }
    });
    
    currentModel = modelEntity;
    
    await logToRhino("Step 7: Model entity created");

    // Fly to the model
    viewer.flyTo(modelEntity);
    
    await logToRhino("Step 8: Flying to model");

    await logToRhino("SUCCESS! Model loaded from Rhino!");
  } catch (error) {
    await logToRhino("ERROR: " + error.message);
    console.error("Error loading model:", error);
    alert("Failed to load model: " + error.message);
  }
}

// Fly to current model (for Target button)
async function flyToCurrentModel(viewer) {
  await logToRhino("TARGET: Button clicked");
  await logToRhino("TARGET: currentModel = " + (currentModel ? "exists" : "null"));
  
  if (currentModel) {
    await logToRhino("TARGET: Flying to model...");
    viewer.flyTo(currentModel);
    await logToRhino("TARGET: Fly command executed");
  } else {
    await logToRhino("TARGET: No model loaded!");
    alert("No model loaded yet. Click Sync first!");
  }
}

export { addModelFromRhino, flyToCurrentModel };