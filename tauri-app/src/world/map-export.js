// Map export: captures 2D map region and sends to Rhino
import { logToRhino } from "../communication/rhino-logger.js";
import { getCurrentMode } from "./view-mode.js";

// Configuration
const EXPORT_SIZE_METERS = 2000; // 2km x 2km area
const TILE_SIZE = 256;           // Google tiles are 256x256
const ZOOM_LEVEL = 18;           // Good balance: ~0.6m/pixel

// Get the center coordinates of the current view
function getViewCenter(viewer) {
  const windowCenter = new Cesium.Cartesian2(
    viewer.canvas.clientWidth / 2,
    viewer.canvas.clientHeight / 2
  );

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
  
  const centerTile = latLonToTile(centerLat, centerLon, zoom);
  
  const halfTiles = Math.floor(tilesNeeded / 2);
  const startX = centerTile.x - halfTiles;
  const startY = centerTile.y - halfTiles;
  const endX = centerTile.x + halfTiles;
  const endY = centerTile.y + halfTiles;
  
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

// Fetch and stitch tiles into a single canvas
async function fetchAndStitchTiles(viewer, tileBounds) {
  const { startX, startY, endX, endY, zoom, tilesX, tilesY, pixelWidth, pixelHeight } = tileBounds;
  
  // Create output canvas
  const canvas = document.createElement('canvas');
  canvas.width = pixelWidth;
  canvas.height = pixelHeight;
  const ctx = canvas.getContext('2d');
  
  // Get the imagery provider from the globe
  const globe = viewer.scene.globe;
  if (!globe || !globe.imageryLayers || globe.imageryLayers.length === 0) {
    throw new Error('No imagery layers available');
  }
  
  const imageryLayer = globe.imageryLayers.get(0);
  const imageryProvider = imageryLayer.imageryProvider;
  
  await logToRhino(`Fetching ${tileBounds.totalTiles} tiles...`);
  
  let fetchedCount = 0;
  const totalTiles = tileBounds.totalTiles;
  
  // Fetch all tiles
  for (let y = startY; y <= endY; y++) {
    for (let x = startX; x <= endX; x++) {
      try {
        // Request tile from imagery provider
        const image = await imageryProvider.requestImage(x, y, zoom);
        
        if (image) {
          // Calculate position on canvas
          const canvasX = (x - startX) * TILE_SIZE;
          const canvasY = (y - startY) * TILE_SIZE;
          
          // Draw tile to canvas
          ctx.drawImage(image, canvasX, canvasY, TILE_SIZE, TILE_SIZE);
        }
        
        fetchedCount++;
        
        // Log progress every 10%
        if (fetchedCount % Math.ceil(totalTiles / 10) === 0) {
          const pct = Math.round(fetchedCount / totalTiles * 100);
          await logToRhino(`Progress: ${pct}% (${fetchedCount}/${totalTiles} tiles)`);
        }
      } catch (error) {
        await logToRhino(`Warning: Failed to fetch tile (${x}, ${y}): ${error.message}`);
      }
    }
  }
  
  await logToRhino(`Fetched ${fetchedCount}/${totalTiles} tiles`);
  
  return canvas;
}

// Convert canvas to base64 JPEG
function canvasToBase64(canvas, quality = 0.9) {
  return canvas.toDataURL('image/jpeg', quality).split(',')[1];
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

    return await response.json();
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

  // Step 4: Fetch & stitch tiles
  try {
    const canvas = await fetchAndStitchTiles(viewer, tileBounds);
    await logToRhino(`Stitched image: ${canvas.width} x ${canvas.height} pixels`);
    
    // Convert to base64 JPEG
    const base64Image = canvasToBase64(canvas);
    await logToRhino(`Image encoded: ${Math.round(base64Image.length / 1024)} KB`);
    
    // TODO Step 5: Send image to Rhino
    await logToRhino("Image ready for Rhino (Step 5 TODO)");
    
  } catch (error) {
    await logToRhino(`ERROR fetching tiles: ${error.message}`);
    return null;
  }

  await logToRhino("=== MAP EXPORT END ===");

  return { center, tileBounds };
}

export { exportMapToRhino, getViewCenter };