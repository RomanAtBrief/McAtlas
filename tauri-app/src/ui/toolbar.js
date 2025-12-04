import { toggleSidePanel } from "./sidePanel.js";
import { fetchGeometryFromRhino } from "../communication/rhino-bridge.js";
import { addModelFromRhino } from "../world/cesium-geometry.js";
import { logToRhino } from "../communication/rhino-logger.js";

function initToolbar(viewer) {
  // Side panel button
  document.getElementById("btnSettings").addEventListener("click", toggleSidePanel);
  
  // Export button - fetch from Rhino and display
  document.getElementById("btnSync").addEventListener("click", async () => {
    await logToRhino("TOOLBAR: Sync/Export button clicked!");
    
    const data = await fetchGeometryFromRhino();
    
    await logToRhino("TOOLBAR: Got data from Rhino");
    
    if (data) {
      await addModelFromRhino(viewer, data);
    }
  });
}

export { initToolbar };