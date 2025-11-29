const { invoke } = window.__TAURI__.core;

// Cesium ion token
Cesium.Ion.defaultAccessToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJqdGkiOiI1NzY1NjEyMy02ZDQ1LTRjMDQtOWZjNC04MDBjY2Q5ZjQ1NDQiLCJpZCI6MzY1MDQwLCJpYXQiOjE3NjQ0MjE3ODV9.qm9PTk2gyKewNkbWkzYsk_Sdf-gVYLJ0T0aX25jcH5M';

// Create Cesium viewer
const viewer = new Cesium.Viewer("cesiumContainer", {
  timeline: false,
  animation: false,
  sceneModePicker: false,
  baseLayerPicker: false,
  geocoder: Cesium.IonGeocodeProviderType.GOOGLE,
  globe: false,
});

// Enable sky
viewer.scene.skyAtmosphere.show = true;

// Load Google 3D Tiles
Cesium.createGooglePhotorealistic3DTileset({
  onlyUsingWithGoogleGeocoder: true,
}).then(function(tileset) {
  viewer.scene.primitives.add(tileset);
  
  // Fly to Edinburgh
  viewer.camera.flyTo({
    destination: Cesium.Cartesian3.fromDegrees(-3.188, 55.953, 500),
    orientation: {
      heading: Cesium.Math.toRadians(0),
      pitch: Cesium.Math.toRadians(-20),
      roll: 0.0
    }
  });
}).catch(function(error) {
  console.error('Error loading tiles:', error);
});
