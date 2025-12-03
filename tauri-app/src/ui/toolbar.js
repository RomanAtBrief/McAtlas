import { toggleSidePanel } from "./sidePanel.js";
import { fetchGeometryFromRhino } from "../communication/rhino-bridge.js";
import { addModelFromRhino } from "../world/cesium-geometry.js";

function initToolbar(viewer) {
  // Side panel button
  document.getElementById("btnSide").addEventListener("click", toggleSidePanel);
  
  // Export button - fetch from Rhino and display
  document.getElementById("btnExport").addEventListener("click", async () => {
    const data = await fetchGeometryFromRhino();
    
    if (data) {
      await addModelFromRhino(viewer, data);
    }
  });
}

export { initToolbar };