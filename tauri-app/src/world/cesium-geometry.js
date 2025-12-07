// Cesium geometry management - loads models from Rhino and applies clipping polygons
// Uses World Terrain (from view-mode's globe) for ground elevation

import { readFile, BaseDirectory } from "@tauri-apps/plugin-fs";
import { logToRhino } from "../communication/rhino-logger.js";
import { getGlobe2D } from "../world/view-mode.js";

// Store references to current entities
let currentModel = null;
let currentClippingPolygons = null;

// Load GLB from disk and add to Cesium viewer with clipping
async function addModelFromRhino(viewer, tileset, data) {
  await logToRhino("========== CESIUM SYNC START ==========");
  
  // Remove old model if exists
  if (currentModel) {
    viewer.entities.remove(currentModel);
    await logToRhino("Removed previous model");
  }
  
  // Check for errors
  if (!data || data.error) {
    await logToRhino("ERROR: " + (data && data.error));
    console.error("Rhino error:", data && data.error);
    alert("Error from Rhino: " + (data && data.error));
    return;
  }

  const { glbPath, position, clippingPolygons } = data;
  
  await logToRhino("RECEIVED FROM RHINO:");
  await logToRhino(`  position.lat: ${position.lat}`);
  await logToRhino(`  position.lon: ${position.lon}`);
  await logToRhino(`  position.height: ${position.height}`);
  await logToRhino(`  clippingPolygons: ${clippingPolygons ? clippingPolygons.length : 0}`);
  
  if (!glbPath) {
    await logToRhino("ERROR: No glbPath");
    return;
  }

  try {
    // ============ APPLY CLIPPING POLYGONS ============
    if (clippingPolygons && clippingPolygons.length > 0 && tileset) {
      await applyClippingPolygons(viewer, tileset, clippingPolygons);
    } else {
      // Remove existing clipping if no polygons provided
      await removeClipping(viewer, tileset);
    }

    // ============ READ GLB FILE ============
    await logToRhino("Reading GLB file...");
    const relativePath = "McAtlas/mcatlas_massing.glb";
    const bytes = await readFile(relativePath, {
      baseDir: BaseDirectory.Document,
    });
    await logToRhino(`GLB file read: ${bytes.length} bytes`);

    const blob = new Blob([bytes], { type: "model/gltf-binary" });
    const url = URL.createObjectURL(blob);

    // ============ SAMPLE TERRAIN ELEVATION ============
    // Use the 2D globe's terrain (World Terrain) for ground elevation
    // This gives us true ground level, not building roofs from 3D tiles
    await logToRhino("Sampling terrain elevation...");
    
    const cartographic = Cesium.Cartographic.fromDegrees(position.lon, position.lat);
    let terrainHeight = 0;
    
    const globe = getGlobe2D();
    if (globe && globe.terrainProvider) {
      try {
        const positions = await Cesium.sampleTerrainMostDetailed(globe.terrainProvider, [cartographic]);
        terrainHeight = positions[0].height || 0;
        await logToRhino(`  Terrain elevation: ${terrainHeight.toFixed(2)}m`);
      } catch (terrainError) {
        await logToRhino(`  WARNING: Terrain sampling failed: ${terrainError.message}`);
        terrainHeight = 0;
      }
    } else {
      await logToRhino("  WARNING: Globe/terrain not ready, using height 0");
    }
    
    // Final height = terrain ground level + model's Z offset from Rhino
    const finalHeight = terrainHeight + position.height;
    await logToRhino(`  Final height: ${finalHeight.toFixed(2)}m (terrain: ${terrainHeight.toFixed(2)} + model Z: ${position.height})`);

    // ============ CREATE MODEL ENTITY ============
    const positionCartesian = Cesium.Cartesian3.fromDegrees(
      position.lon,
      position.lat,
      finalHeight
    );

    // Orientation - 90° heading to align Rhino Y-North with Cesium
    const heading = Cesium.Math.toRadians(90);
    const pitch = 0;
    const roll = 0;
    const hpr = new Cesium.HeadingPitchRoll(heading, pitch, roll);
    const orientation = Cesium.Transforms.headingPitchRollQuaternion(positionCartesian, hpr);

    await logToRhino("Creating model entity...");

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
    await logToRhino("========== CESIUM SYNC COMPLETE ==========");

  } catch (error) {
    await logToRhino("ERROR: " + error.message);
    console.error("Error loading model:", error);
    alert("Failed to load model: " + error.message);
  }
}

// Apply clipping polygons to cut holes in 3D tiles
async function applyClippingPolygons(viewer, tileset, polygons) {
  await logToRhino(`Applying ${polygons.length} clipping polygon(s)...`);
  
  // Remove existing clipping first
  if (currentClippingPolygons) {
    tileset.clippingPolygons = undefined;
    if (viewer.scene.globe) {
      viewer.scene.globe.clippingPolygons = undefined;
    }
  }
  
  // Create Cesium ClippingPolygon objects
  const cesiumPolygons = [];
  
  for (let i = 0; i < polygons.length; i++) {
    const polygon = polygons[i];
    await logToRhino(`  Polygon ${i + 1}: ${polygon.length / 2} points`);
    
    try {
      // polygon is array of [lon, lat, lon, lat, ...] in degrees
      const positions = Cesium.Cartesian3.fromDegreesArray(polygon);
      
      const clippingPolygon = new Cesium.ClippingPolygon({
        positions: positions
      });
      
      cesiumPolygons.push(clippingPolygon);
    } catch (error) {
      await logToRhino(`  ERROR creating polygon ${i + 1}: ${error.message}`);
    }
  }
  
  if (cesiumPolygons.length > 0) {
    // Apply to 3D tileset
    const clippingCollection = new Cesium.ClippingPolygonCollection({
      polygons: cesiumPolygons
    });
    
    tileset.clippingPolygons = clippingCollection;
    
    // Also apply to globe (for terrain mode)
    if (viewer.scene.globe) {
      viewer.scene.globe.clippingPolygons = new Cesium.ClippingPolygonCollection({
        polygons: cesiumPolygons.map(p => new Cesium.ClippingPolygon({ positions: p.positions }))
      });
    }
    
    currentClippingPolygons = clippingCollection;
    await logToRhino(`Applied ${cesiumPolygons.length} clipping polygon(s) to 3D tiles`);
  }
}

// Remove all clipping
async function removeClipping(viewer, tileset) {
  if (currentClippingPolygons) {
    await logToRhino("Removing existing clipping polygons...");
    
    if (tileset) {
      tileset.clippingPolygons = undefined;
    }
    if (viewer.scene.globe) {
      viewer.scene.globe.clippingPolygons = undefined;
    }
    
    currentClippingPolygons = null;
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