// Import Cesium viewer
import { initCesiumViewer } from './world/cesium-viewer.js';

// Invoke Tauri
const { invoke } = window.__TAURI__.core;

// Initialize Cesium when page loads
const viewer = await initCesiumViewer('cesiumContainer');


