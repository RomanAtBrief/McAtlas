// Import Cesium viewer
import { initCesiumViewer } from './world/cesium-viewer.js';
import { initToolbar } from "./ui/toolbar.js";
import { initViewMode } from "./world/view-mode.js";

// Invoke Tauri
const { invoke } = window.__TAURI__.core;

// Initialize Cesium when page loads
// Returns { viewer, tileset }
const { viewer, tileset } = await initCesiumViewer('cesiumContainer');

// Initialize view mode (2D/3D toggle) - this creates the globe with World Terrain
await initViewMode(viewer, tileset);

// Initialize toolbar with tileset for clipping support
initToolbar(viewer, tileset);