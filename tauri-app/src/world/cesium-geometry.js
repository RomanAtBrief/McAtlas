import { readFile } from "@tauri-apps/plugin-fs";

// Load GLB from disk (saved by Rhino) and add it to Cesium viewer
async function addModelFromRhino(viewer, data) {
  if (!data || data.error) {
    console.error("Rhino error:", data && data.error);
    alert("Error from Rhino: " + (data && data.error));
    return;
  }

  const { glbPath, position } = data;
  if (!glbPath) {
    console.error("No glbPath from Rhino");
    return;
  }

  try {
    // Tauri FS API - read the GLB file
    const bytes = await readFile(glbPath);

    // Read GLB bytes from disk
    const blob = new Blob([bytes], { type: "model/gltf-binary" });
    const url = URL.createObjectURL(blob);

    // Position in Cesium
    const cartesian = Cesium.Cartesian3.fromDegrees(
      position.lon,
      position.lat,
      position.height
    );

    const modelMatrix = Cesium.Transforms.eastNorthUpToFixedFrame(cartesian);

    // Load the model
    const model = await Cesium.Model.fromGltfAsync({
      url,
      modelMatrix,
    });

    viewer.scene.primitives.add(model);
    viewer.flyTo(model);

    console.log("Model loaded from Rhino!");
  } catch (error) {
    console.error("Error loading model:", error);
    alert("Failed to load model: " + error.message);
  }
}

export { addModelFromRhino };
