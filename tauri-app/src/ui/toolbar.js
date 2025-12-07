import { toggleSidePanel } from "./sidePanel.js";
import { fetchGeometryFromRhino } from "../communication/rhino-bridge.js";
import { addModelFromRhino, flyToCurrentModel } from "../world/cesium-geometry.js";
import { logToRhino } from "../communication/rhino-logger.js";
import { toggleViewMode, getCurrentMode } from "../world/view-mode.js";
import { exportMapToRhino } from "../world/map-export.js";

// Store tileset reference for clipping
let _tileset = null;

function initToolbar(viewer, tileset) {
  // Store tileset for later use
  _tileset = tileset;
  
  // Settings panel button
  document.getElementById("btnSettings").addEventListener("click", toggleSidePanel);
  
  // 2D/3D Toggle button
  const btnToggleView = document.getElementById("btnToggleView");
  btnToggleView.addEventListener("click", async () => {
    const newMode = toggleViewMode();
    // Update button text to show what clicking will switch TO
    btnToggleView.querySelector("span").textContent = newMode === '3D' ? '2D' : '3D';
    await logToRhino(`VIEW: Switched to ${newMode} mode`);
  });
  
  // Send Map to Rhino button
  document.getElementById("btnGetMap").addEventListener("click", async () => {
    await logToRhino("TOOLBAR: Send Map to Rhino clicked!");
    await exportMapToRhino(viewer);
  });
  
  // Sync button - fetch from Rhino and display
  document.getElementById("btnSync").addEventListener("click", async () => {
    await logToRhino("TOOLBAR: Sync/Export button clicked!");
    const data = await fetchGeometryFromRhino();
    await logToRhino("TOOLBAR: Got data from Rhino");
    if (data) {
      // Pass tileset for clipping polygon support
      await addModelFromRhino(viewer, _tileset, data);
    }
  });

  // Target button - fly back to model
  document.getElementById("btnTarget").addEventListener("click", async () => {
    await flyToCurrentModel(viewer);
  });

  // Search input - geocode and fly to location
  const searchInput = document.getElementById("searchInput");
  searchInput.addEventListener("keypress", async (e) => {
    if (e.key === "Enter") {
      const query = searchInput.value.trim();
      if (query) {
        await searchLocation(viewer, query);
      }
    }
  });
}

// Search and fly to location using Cesium ion Geocoder
async function searchLocation(viewer, query) {
  await logToRhino("SEARCH: Searching for: " + query);
  
  try {
    await logToRhino("SEARCH: Using Cesium ion geocoder...");
    const geocoder = new Cesium.IonGeocoderService({ scene: viewer.scene });
    
    await logToRhino("SEARCH: Calling geocode...");
    const results = await geocoder.geocode(query);
    
    await logToRhino("SEARCH: Results count = " + (results ? results.length : 0));
    
    if (results && results.length > 0) {
      const result = results[0];
      await logToRhino("SEARCH: Found location, flying...");
      
      viewer.camera.flyTo({
        destination: result.destination,
        duration: 2.0
      });
      
      await logToRhino("SEARCH: Success!");
    } else {
      await logToRhino("SEARCH: No results found");
      alert("Location not found");
    }
  } catch (error) {
    await logToRhino("SEARCH ERROR: " + error.message);
    alert("Search failed: " + error.message);
  }
}

export { initToolbar };