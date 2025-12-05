// Import Cesium viewer
import { initCesiumViewer } from './world/cesium-viewer.js';
import { initViewMode } from './world/view-mode.js';
import { initToolbar } from "./ui/toolbar.js";

// Invoke Tauri
const { invoke } = window.__TAURI__.core;

// Initialize Cesium when page loads (now returns { viewer, tileset })
const { viewer, tileset } = await initCesiumViewer('cesiumContainer');

// Initialize view mode manager (async - loads 2D imagery)
await initViewMode(viewer, tileset);

// Initialize toolbar
initToolbar(viewer);