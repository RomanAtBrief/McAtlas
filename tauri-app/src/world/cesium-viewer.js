// Cesium ion token
const CESIUM_TOKEN = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJqdGkiOiI1NzY1NjEyMy02ZDQ1LTRjMDQtOWZjNC04MDBjY2Q5ZjQ1NDQiLCJpZCI6MzY1MDQwLCJpYXQiOjE3NjQ0MjE3ODV9.qm9PTk2gyKewNkbWkzYsk_Sdf-gVYLJ0T0aX25jcH5M';

async function initCesiumViewer(containerId) {

    // Set Cesium ion token
    Cesium.Ion.defaultAccessToken = CESIUM_TOKEN;

    // Create Cesium viewer
    // globe: false is REQUIRED for Google Photorealistic 3D Tiles
    const viewer = new Cesium.Viewer(containerId, {
        timeline: false,
        animation: false,
        sceneModePicker: false,
        baseLayerPicker: false,
        homeButton: false,
        navigationHelpButton: false,
        geocoder: false,
        selectionIndicator: false,
        infoBox: false,
        // The globe does not need to be displayed,
        // since the Photorealistic 3D Tiles include terrain
        globe: false,
    });

    // Hide credits at bottom
    viewer.scene.frameState.creditDisplay.container.style.display = 'none';

    // Enable rendering the sky
    viewer.scene.skyAtmosphere.show = true;

    // Enable ambient occlusion (from Cesium example)
    // TODO: Find better settings for big scale buildings
    if (Cesium.PostProcessStageLibrary.isAmbientOcclusionSupported(viewer.scene)) {
        const ambientOcclusion = viewer.scene.postProcessStages.ambientOcclusion;
        ambientOcclusion.enabled = true;
        ambientOcclusion.uniforms.ambientOcclusionOnly = false;
        ambientOcclusion.uniforms.intensity = 3.0;
        ambientOcclusion.uniforms.bias = 0.1;
        ambientOcclusion.uniforms.lengthCap = 0.26;
        ambientOcclusion.uniforms.stepSize = 1.0;
    }

    // Add Google Photorealistic 3D Tiles
    let tileset = null;
    try {
        tileset = await Cesium.createGooglePhotorealistic3DTileset({
            onlyUsingWithGoogleGeocoder: true,
        });
        viewer.scene.primitives.add(tileset);
    } catch (error) {
        console.log(`Error loading Photorealistic 3D Tiles tileset.
          ${error}`);
    }

    // Fly to New York
    viewer.camera.setView({
        destination: Cesium.Cartesian3.fromDegrees(-73.9986, 40.6976, 800),
        orientation: {
            heading: Cesium.Math.toRadians(-45),
            pitch: Cesium.Math.toRadians(-25),
            roll: 0.0
        }
    });

    // Return both viewer and tileset for view-mode manager
    return { viewer, tileset };
}

export { initCesiumViewer }