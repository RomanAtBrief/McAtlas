import { toggleSidePanel } from "./sidePanel.js";
import { fetchGeometryFromRhino } from "../communication/rhino-bridge.js";
import { addModelFromRhino, flyToCurrentModel } from "../world/cesium-geometry.js";
import { logToRhino } from "../communication/rhino-logger.js";

function initToolbar(viewer) {
  // Settings panel button
  document.getElementById("btnSettings").addEventListener("click", toggleSidePanel);
  
  // Sync button - fetch from Rhino and display
  document.getElementById("btnSync").addEventListener("click", async () => {
    await logToRhino("TOOLBAR: Sync/Export button clicked!");
    const data = await fetchGeometryFromRhino();
    await logToRhino("TOOLBAR: Got data from Rhino");
    if (data) {
      await addModelFromRhino(viewer, data);
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