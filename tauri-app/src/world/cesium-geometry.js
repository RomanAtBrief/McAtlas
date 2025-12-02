// Add a cube to Cesium viewer
function addCubeToViewer(viewer, data) {
  const { position, size } = data;
  
  // Create a box entity
  const entity = viewer.entities.add({
    name: 'Rhino Cube',
    position: Cesium.Cartesian3.fromDegrees(
      position.lon,
      position.lat,
      position.height + size.height / 2 // Center the box
    ),
    box: {
      dimensions: new Cesium.Cartesian3(size.width, size.depth, size.height),
      material: Cesium.Color.BLUE.withAlpha(0.7),
      outline: true,
      outlineColor: Cesium.Color.BLACK
    }
  });
  
  // Fly camera to the cube
  viewer.flyTo(entity);
  
  return entity;
}

export { addCubeToViewer };