// Cesium ion token
const CESIUM_TOKEN = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJqdGkiOiI1NzY1NjEyMy02ZDQ1LTRjMDQtOWZjNC04MDBjY2Q5ZjQ1NDQiLCJpZCI6MzY1MDQwLCJpYXQiOjE3NjQ0MjE3ODV9.qm9PTk2gyKewNkbWkzYsk_Sdf-gVYLJ0T0aX25jcH5M';

async function initCesiumViewer(conteinerId) {

    // Set Cesium ion token
    Cesium.Ion.defaultAccessToken = CESIUM_TOKEN;

    // Create Cesium viewer
    const viewer = new Cesium.Viewer("cesiumContainer", {
        timeline: false,
        animation: false,
        sceneModePicker: false,
        baseLayerPicker: false,
        homeButton: false,
        navigationHelpButton: false,
        geocoder: false,
        //geocoder: Cesium.IonGeocodeProviderType.GOOGLE,
        // The globe does not need to be displayed,
        // since the Photorealistic 3D Tiles include terrain
        globe: false,
    });

    // Hide credits at bottom
    viewer.scene.frameState.creditDisplay.container.style.display = 'none';

    // Enable rendering the sky
    viewer.scene.skyAtmosphere.show = true;

    // Add Google Photorealistic 3D Tiles
    try {
        const tileset = await Cesium.createGooglePhotorealistic3DTileset({
            // Only the Google Geocoder can be used with Google Photorealistic 3D Tiles.  Set the `geocode` property of the viewer constructor options to IonGeocodeProviderType.GOOGLE.
            onlyUsingWithGoogleGeocoder: true,
        });
        viewer.scene.primitives.add(tileset);
    } catch (error) {
        console.log(`Error loading Photorealistic 3D Tiles tileset.
          ${error}`);
    }

    // Fly to Edinburgh
    viewer.camera.setView({
        destination: Cesium.Cartesian3.fromDegrees(-73.9986, 40.6976, 800),
        orientation: {
            heading: Cesium.Math.toRadians(-45),
            pitch: Cesium.Math.toRadians(-25),
            roll: 0.0
        }
    });

    return viewer;
}

export { initCesiumViewer }