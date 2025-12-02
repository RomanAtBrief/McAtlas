// Fetch geometry from Rhino
async function fetchGeometryFromRhino() {
  try {
    const response = await fetch('http://localhost:8080/export-geometry');
    
    if (!response.ok) {
      throw new Error('Failed to fetch geometry from Rhino');
    }
    
    const data = await response.json();
    return data;
  } catch (error) {
    console.error('Error fetching from Rhino:', error);
    alert('Could not connect to Rhino. Make sure Rhino is running with McAtlas plugin loaded.');
    return null;
  }
}

export { fetchGeometryFromRhino };