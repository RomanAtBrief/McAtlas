// Map export: captures 2D map region and sends to Rhino
import { logToRhino } from "../communication/rhino-logger.js";
import { getCurrentMode } from "./view-mode.js";

// Get the center coordinates of the current view
function getViewCenter(viewer) {
  // Get screen center
  const windowCenter = new Cesium.Cartesian2(
    viewer.canvas.clientWidth / 2,
    viewer.canvas.clientHeight / 2
  );

  // Pick position on globe
  const ray = viewer.camera.getPickRay(windowCenter);
  const globe = viewer.scene.globe;

  if (globe && globe.show) {
    const position = globe.pick(ray, viewer.scene);
    if (position) {
      const cartographic = Cesium.Cartographic.fromCartesian(position);
      return {
        lon: Cesium.Math.toDegrees(cartographic.longitude),
        lat: Cesium.Math.toDegrees(cartographic.latitude),
        height: cartographic.height
      };
    }
  }

  // Fallback: use camera position
  const cartographic = viewer.camera.positionCartographic;
  return {
    lon: Cesium.Math.toDegrees(cartographic.longitude),
    lat: Cesium.Math.toDegrees(cartographic.latitude),
    height: cartographic.height
  };
}

// Send earth anchor coordinates to Rhino
async function setEarthAnchorInRhino(lat, lon) {
  try {
    const response = await fetch('http://localhost:8080/set-earth-anchor', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ lat, lon })
    });

    if (!response.ok) {
      throw new Error('Failed to set earth anchor');
    }

    const result = await response.json();
    return result;
  } catch (error) {
    await logToRhino(`ERROR setting earth anchor: ${error.message}`);
    return { error: error.message };
  }
}

// Main export function
async function exportMapToRhino(viewer) {
  await logToRhino("=== MAP EXPORT START ===");

  // Only works in 2D mode
  if (getCurrentMode() !== '2D') {
    await logToRhino("ERROR: Please switch to 2D mode first");
    alert("Please switch to 2D mode before exporting the map.");
    return null;
  }

  // Step 1: Get center coordinates
  const center = getViewCenter(viewer);
  await logToRhino(`Center: lat=${center.lat.toFixed(6)}, lon=${center.lon.toFixed(6)}`);

  // Step 2: Set EarthAnchorPoint in Rhino
  await logToRhino("Setting EarthAnchorPoint in Rhino...");
  const anchorResult = await setEarthAnchorInRhino(center.lat, center.lon);

  if (anchorResult.error) {
    await logToRhino(`ERROR: ${anchorResult.error}`);
    return null;
  }
  await logToRhino("EarthAnchorPoint set successfully!");

  // TODO Step 3: Calculate tile bounds for 2km area
  // TODO Step 4: Fetch & stitch tiles
  // TODO Step 5: Send image to Rhino

  await logToRhino("=== MAP EXPORT END ===");

  return center;
}

export { exportMapToRhino, getViewCenter };