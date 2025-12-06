// Map export: captures 2D map region and sends to Rhino
import { logToRhino } from "../communication/rhino-logger.js";
import { getCurrentMode } from "./view-mode.js";

// Configuration
const EXPORT_SIZE_METERS = 2000; // 2km x 2km area
const TILE_SIZE = 256;           // Google tiles are 256x256
const ZOOM_LEVEL = 18;           // Good balance: ~0.6m/pixel

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

// Convert lat/lon to tile coordinates at a given zoom level
function latLonToTile(lat, lon, zoom) {
  const n = Math.pow(2, zoom);
  const x = Math.floor((lon + 180) / 360 * n);
  const latRad = lat * Math.PI / 180;
  const y = Math.floor((1 - Math.log(Math.tan(latRad) + 1 / Math.cos(latRad)) / Math.PI) / 2 * n);
  return { x, y };
}

// Convert tile coordinates to lat/lon (top-left corner of tile)
function tileToLatLon(x, y, zoom) {
  const n = Math.pow(2, zoom);
  const lon = x / n * 360 - 180;
  const latRad = Math.atan(Math.sinh(Math.PI * (1 - 2 * y / n)));
  const lat = latRad * 180 / Math.PI;
  return { lat, lon };
}

// Calculate meters per pixel at a given latitude and zoom
function metersPerPixel(lat, zoom) {
  return 156543.03392 * Math.cos(lat * Math.PI / 180) / Math.pow(2, zoom);
}

// Calculate tile bounds for export area
function calculateTileBounds(centerLat, centerLon, sizeMeters, zoom) {
  const mpp = metersPerPixel(centerLat, zoom);
  const pixelsNeeded = sizeMeters / mpp;
  const tilesNeeded = Math.ceil(pixelsNeeded / TILE_SIZE);
  
  // Get center tile
  const centerTile = latLonToTile(centerLat, centerLon, zoom);
  
  // Calculate tile range (centered on center tile)
  const halfTiles = Math.floor(tilesNeeded / 2);
  const startX = centerTile.x - halfTiles;
  const startY = centerTile.y - halfTiles;
  const endX = centerTile.x + halfTiles;
  const endY = centerTile.y + halfTiles;
  
  // Calculate actual bounds in lat/lon
  const topLeft = tileToLatLon(startX, startY, zoom);
  const bottomRight = tileToLatLon(endX + 1, endY + 1, zoom);
  
  return {
    zoom,
    startX,
    startY,
    endX,
    endY,
    tilesX: endX - startX + 1,
    tilesY: endY - startY + 1,
    totalTiles: (endX - startX + 1) * (endY - startY + 1),
    pixelWidth: (endX - startX + 1) * TILE_SIZE,
    pixelHeight: (endY - startY + 1) * TILE_SIZE,
    bounds: {
      north: topLeft.lat,
      south: bottomRight.lat,
      west: topLeft.lon,
      east: bottomRight.lon
    },
    metersPerPixel: mpp,
    actualSizeMeters: (endX - startX + 1) * TILE_SIZE * mpp
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

  // Step 3: Calculate tile bounds for 2km area
  await logToRhino(`Calculating tiles for ${EXPORT_SIZE_METERS}m area at zoom ${ZOOM_LEVEL}...`);
  const tileBounds = calculateTileBounds(center.lat, center.lon, EXPORT_SIZE_METERS, ZOOM_LEVEL);
  
  await logToRhino(`Tiles: ${tileBounds.tilesX} x ${tileBounds.tilesY} = ${tileBounds.totalTiles} total`);
  await logToRhino(`Output: ${tileBounds.pixelWidth} x ${tileBounds.pixelHeight} pixels`);
  await logToRhino(`Actual size: ${tileBounds.actualSizeMeters.toFixed(0)}m x ${tileBounds.actualSizeMeters.toFixed(0)}m`);
  await logToRhino(`Resolution: ${tileBounds.metersPerPixel.toFixed(2)} m/pixel`);

  // TODO Step 4: Fetch & stitch tiles
  // TODO Step 5: Send image to Rhino

  await logToRhino("=== MAP EXPORT END ===");

  return { center, tileBounds };
}

export { exportMapToRhino, getViewCenter };
