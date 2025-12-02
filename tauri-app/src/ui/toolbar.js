import { toggleSidePanel } from "./sidePanel.js";

function initToolbar() {
  document.getElementById("btnSide").addEventListener("click", toggleSidePanel);
}

export { initToolbar };