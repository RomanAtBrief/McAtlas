
// Toggle side panel
function toggleSidePanel() {
  const panel = document.getElementById("sidePanel");
  const isOpen = panel.style.width === "16rem"; // w-3xs tailwind
  panel.style.width = isOpen ? "0" : "16rem";
}

export { toggleSidePanel };