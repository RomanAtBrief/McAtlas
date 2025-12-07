// View mode manager: handles 2D/3D toggle
// Switches between Google Photorealistic 3D Tiles and 2D satellite imagery

let currentMode = '3D';
let viewer = null;
let tileset3D = null;
let globe2D = null;
let globeInitialized = false;

// Initialize view mode manager with Cesium viewer and 3D tileset reference
async function initViewMode(cesiumViewer, googleTileset) {
  viewer = cesiumViewer;
  tileset3D = googleTileset;
  
  // Create a separate globe for 2D mode (like original)
  globe2D = new Cesium.Globe(Cesium.Ellipsoid.WGS84);
  
  // Add Cesium World Terrain for elevation sampling
  try {
    const worldTerrain = await Cesium.CesiumTerrainProvider.fromIonAssetId(1);
    globe2D.terrainProvider = worldTerrain;
    console.log('[McAtlas] World Terrain loaded for 2D globe');
  } catch (error) {
    console.error('[McAtlas] Failed to load World Terrain:', error);
  }
  
  // Add Google Maps 2D Satellite imagery (no labels)
  try {
    const imageryProvider = await Cesium.IonImageryProvider.fromAssetId(3830182);
    globe2D.imageryLayers.addImageryProvider(imageryProvider);
    console.log('[McAtlas] Google 2D imagery loaded');
  } catch (error) {
    console.error('[McAtlas] Failed to load Google 2D imagery:', error);
  }
}

// Switch to 2D satellite view
function switchTo2D() {
  if (currentMode === '2D') return;
  
  // Get the point the camera is looking at (center of screen)
  const windowCenter = new Cesium.Cartesian2(
    viewer.canvas.clientWidth / 2,
    viewer.canvas.clientHeight / 2
  );
  
  // Pick the position on the tileset at screen center
  const targetPosition = viewer.scene.pickPosition(windowCenter);
  
  let lon, lat, height;
  
  if (targetPosition) {
    const targetCartographic = Cesium.Cartographic.fromCartesian(targetPosition);
    lon = Cesium.Math.toDegrees(targetCartographic.longitude);
    lat = Cesium.Math.toDegrees(targetCartographic.latitude);
    height = viewer.camera.positionCartographic.height;
  } else {
    // Fallback to camera position if pick fails
    const cartographic = viewer.camera.positionCartographic;
    lon = Cesium.Math.toDegrees(cartographic.longitude);
    lat = Cesium.Math.toDegrees(cartographic.latitude);
    height = cartographic.height;
  }
  
  // Hide 3D tileset
  if (tileset3D) {
    tileset3D.show = false;
  }
  
  // Attach globe to scene (only once) and show it
  if (!globeInitialized) {
    viewer.scene.globe = globe2D;
    globeInitialized = true;
  }
  globe2D.show = true;
  
  // Set camera to top-down view centered on target
  viewer.camera.setView({
    destination: Cesium.Cartesian3.fromDegrees(lon, lat, height),
    orientation: {
      heading: 0,
      pitch: Cesium.Math.toRadians(-90),
      roll: 0
    }
  });
  
  // ============ LOCK CAMERA IN 2D MODE ============
  // Disable tilt only - enableRotate is needed for panning in top-down view
  viewer.scene.screenSpaceCameraController.enableTilt = false;
  viewer.scene.screenSpaceCameraController.enableLook = false;
  
  currentMode = '2D';
}

// Switch to 3D photorealistic view
function switchTo3D() {
  if (currentMode === '3D') return;
  
  // In 2D top-down view, camera position IS the target
  const cartographic = viewer.camera.positionCartographic;
  const lon = Cesium.Math.toDegrees(cartographic.longitude);
  const lat = Cesium.Math.toDegrees(cartographic.latitude);
  const height = cartographic.height;
  
  // Hide globe
  if (globe2D) {
    globe2D.show = false;
  }
  
  // Show 3D tileset
  if (tileset3D) {
    tileset3D.show = true;
  }
  
  // ============ UNLOCK CAMERA IN 3D MODE ============
  // Re-enable full camera control
  viewer.scene.screenSpaceCameraController.enableTilt = true;
  viewer.scene.screenSpaceCameraController.enableLook = true;
  
  // Calculate range to maintain similar ground coverage
  const pitch = Cesium.Math.toRadians(-25);
  const heading = Cesium.Math.toRadians(-45);
  const range = height / Math.sin(Math.abs(pitch));
  
  const target = Cesium.Cartesian3.fromDegrees(lon, lat, 0);
  
  viewer.camera.lookAt(
    target,
    new Cesium.HeadingPitchRange(heading, pitch, range)
  );
  
  // Unlock camera from lookAt constraint
  viewer.camera.lookAtTransform(Cesium.Matrix4.IDENTITY);
  
  currentMode = '3D';
}

// Toggle between modes, returns new mode
function toggleViewMode() {
  if (currentMode === '3D') {
    switchTo2D();
  } else {
    switchTo3D();
  }
  return currentMode;
}

// Get current mode
function getCurrentMode() {
  return currentMode;
}

// Get the 2D globe (for terrain sampling)
function getGlobe2D() {
  return globe2D;
}

export { initViewMode, toggleViewMode, getCurrentMode, switchTo2D, switchTo3D, getGlobe2D };