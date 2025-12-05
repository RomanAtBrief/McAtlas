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
  
  // Create a globe for 2D mode
  globe2D = new Cesium.Globe(Cesium.Ellipsoid.WGS84);
  
  // Add Google Maps 2D Satellite imagery (no labels)
  // Using Google2DImageryProvider with asset 3830182
  try {
    const imageryProvider = await Cesium.Google2DImageryProvider.fromIonAssetId({
      assetId: 3830182
    });
    globe2D.imageryLayers.addImageryProvider(imageryProvider);
  } catch (error) {
    console.error('Failed to load Google 2D imagery:', error);
  }
}

// Switch to 2D satellite view
function switchTo2D() {
  if (currentMode === '2D') return;
  
  // Get the point the camera is looking at (center of screen)
  // This is more accurate than camera position for angled views
  const windowCenter = new Cesium.Cartesian2(
    viewer.canvas.clientWidth / 2,
    viewer.canvas.clientHeight / 2
  );
  
  // Pick the position on the globe/tileset at screen center
  const ray = viewer.camera.getPickRay(windowCenter);
  const targetPosition = viewer.scene.globe?.pick(ray, viewer.scene) 
    || viewer.scene.pickPosition(windowCenter);
  
  let lon, lat, height;
  
  if (targetPosition) {
    // Use the look-at target for lon/lat
    const targetCartographic = Cesium.Cartographic.fromCartesian(targetPosition);
    lon = Cesium.Math.toDegrees(targetCartographic.longitude);
    lat = Cesium.Math.toDegrees(targetCartographic.latitude);
    // Keep camera height from current position
    height = viewer.camera.positionCartographic.height;
  } else {
    // Fallback to camera position if pick fails
    const cartographic = viewer.camera.positionCartographic;
    lon = Cesium.Math.toDegrees(cartographic.longitude);
    lat = Cesium.Math.toDegrees(cartographic.latitude);
    height = cartographic.height;
  }
  
  // Hide 3D tileset
  tileset3D.show = false;
  
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
  
  currentMode = '2D';
}

// Switch to 3D photorealistic view
function switchTo3D() {
  if (currentMode === '3D') return;
  
  // In 2D top-down view, camera position IS the target (looking straight down)
  const cartographic = viewer.camera.positionCartographic;
  const lon = Cesium.Math.toDegrees(cartographic.longitude);
  const lat = Cesium.Math.toDegrees(cartographic.latitude);
  const height = cartographic.height;
  
  // Hide globe
  if (globe2D) {
    globe2D.show = false;
  }
  
  // Show 3D tileset
  tileset3D.show = true;
  
  // Calculate range to maintain similar ground coverage
  // In top-down: height = distance to ground
  // In angled view: we need to compensate for the pitch angle
  const pitch = Cesium.Math.toRadians(-25);
  const heading = Cesium.Math.toRadians(-45);
  
  // range * sin(|pitch|) = height, so range = height / sin(|pitch|)
  // This keeps the camera at roughly the same altitude
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

export { initViewMode, toggleViewMode, getCurrentMode, switchTo2D, switchTo3D };