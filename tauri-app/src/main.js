// Import Cesium viewer
import { initCesiumViewer } from './world/cesium-viewer.js';
import { initToolbar } from "./ui/toolbar.js";

// Invoke Tauri
const { invoke } = window.__TAURI__.core;

// Initialize Cesium when page loads
const viewer = await initCesiumViewer('cesiumContainer');

// Initialize toolbar
initToolbar();



