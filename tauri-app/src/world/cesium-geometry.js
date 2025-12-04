import { readFile, BaseDirectory } from "@tauri-apps/plugin-fs";
import { logToRhino } from "../communication/rhino-logger.js";

// Load GLB from disk (saved by Rhino) and add it to Cesium viewer
async function addModelFromRhino(viewer, data) {
  await logToRhino("Step 1: Starting...");
  
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
    console.log("Rhino reported GLB path:", glbPath);

    // We know Rhino saves to: $DOCUMENT/McAtlas/mcatlas_massing.glb
    const relativePath = "McAtlas/mcatlas_massing.glb";

    // Tauri FS API - read the GLB file from the Documents directory
    // IMPORTANT: Use 'baseDir' not 'dir' in Tauri v2
    const bytes = await readFile(relativePath, {
      baseDir: BaseDirectory.Document,
    });

    await logToRhino("Step 4: File read! Size = " + bytes.length + " bytes");
    console.log("Read GLB bytes:", bytes.length);

    const blob = new Blob([bytes], { type: "model/gltf-binary" });
    const url = URL.createObjectURL(blob);
    
    await logToRhino("Step 5: Blob URL created");

    // Position in Cesium
    const cartesian = Cesium.Cartesian3.fromDegrees(
      position.lon,
      position.lat,
      position.height
    );

    const modelMatrix = Cesium.Transforms.eastNorthUpToFixedFrame(cartesian);
    
    await logToRhino("Step 6: Cesium position set");

    // Load the model
    await logToRhino("Step 7: Loading model with Cesium...");
    const model = await Cesium.Model.fromGltfAsync({
      url,
      modelMatrix,
    });

    await logToRhino("Step 8: Model loaded!");
    
    viewer.scene.primitives.add(model);
    await logToRhino("Step 9: Model added to scene");
    
    await viewer.flyTo(model);
    await logToRhino("Step 10: Flying to model");

    await logToRhino("SUCCESS! Model loaded from Rhino!");
    console.log("Model loaded from Rhino!");
  } catch (error) {
    await logToRhino("ERROR: " + error.message);
    await logToRhino("ERROR stack: " + (error.stack || "no stack trace"));
    console.error("Error loading model:", error);
    alert("Failed to load model: " + error.message);
  }
}

export { addModelFromRhino };