// Import C# namespaces
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

// Import RhinoCommon namespaces
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Display;
using Rhino.FileIO;          // <-- for FileGltfWriteOptions
using Rhino.Collections;     // <-- for ArchivableDictionary

// Namespace
namespace rhino_plugin
{
    // Simple HTTP server for Tauri communication
    public class SimpleHttpServer
    {
        private HttpListener _listener;
        private bool _isRunning;

        // Start the HTTP server
        public void Start()
        {
            if (_isRunning) return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://localhost:8080/");
                _listener.Start();
                _isRunning = true;

                // Start listening in background
                Task.Run(() => Listen());

                RhinoApp.WriteLine("McAtlas server started on http://localhost:8080");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Failed to start server: {ex.Message}");
            }
        }

        // Listen for incoming requests
        private async void Listen()
        {
            while (_isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    await HandleRequest(context);
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        RhinoApp.WriteLine($"Server error: {ex.Message}");
                    }
                }
            }
        }

        // Handle incoming HTTP requests
        private async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // Enable CORS (allow Tauri to connect)
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            // Handle preflight OPTIONS request
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            // Handle export geometry request
            if (request.Url.AbsolutePath == "/export-geometry")
            {
                string json = GetGeometryJson();

                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                response.StatusCode = 200;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);

                RhinoApp.WriteLine("Sent geometry to Tauri app");
            }
            // Handle log messages from Tauri
            else if (request.Url.AbsolutePath == "/log")
            {
                using (var reader = new StreamReader(request.InputStream))
                {
                    string message = await reader.ReadToEndAsync();
                    RhinoApp.WriteLine($"[Tauri] {message}");
                }
                response.StatusCode = 200;
            }
            // Handle set earth anchor request
            else if (request.Url.AbsolutePath == "/set-earth-anchor" && request.HttpMethod == "POST")
            {
                using (var reader = new StreamReader(request.InputStream))
                {
                    string json = await reader.ReadToEndAsync();
                    string result = SetEarthAnchor(json);

                    byte[] buffer = Encoding.UTF8.GetBytes(result);
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    response.StatusCode = 200;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
            }
            // Handle import map image request
            else if (request.Url.AbsolutePath == "/import-map-image" && request.HttpMethod == "POST")
            {
                using (var reader = new StreamReader(request.InputStream))
                {
                    string json = await reader.ReadToEndAsync();
                    string result = ImportMapImage(json);

                    byte[] buffer = Encoding.UTF8.GetBytes(result);
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    response.StatusCode = 200;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
            }
            else
            {
                // Unknown endpoint
                response.StatusCode = 404;
            }

            response.Close();
        }

        // Wrapper: run export on Rhino UI thread and return JSON
        private string GetGeometryJson()
        {
            string result = null;

            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                result = GetGeometryJsonInternal();
            }));

            if (string.IsNullOrEmpty(result))
                result = @"{""error"":""Failed to generate geometry""}";

            return result;
        }

        // Get or create default PBR material for GLB export
        private int GetOrCreateDefaultMaterial(RhinoDoc doc)
        {
            var materialName = "McAtlas_Default_White";

            // Check if already exists
            for (int i = 0; i < doc.Materials.Count; i++)
            {
                if (doc.Materials[i].Name == materialName)
                    return i;
            }

            // Create a Physically Based material (required for proper glTF export)
            var material = new Material
            {
                Name = materialName
            };

            // Initialize PBR mode first
            material.ToPhysicallyBased();

            // Enable PBR and set properties
            var pbr = material.PhysicallyBased;
            pbr.BaseColor = Color4f.White;  // Pure white
            pbr.Metallic = 0.0;             // Non-metallic (important!)
            pbr.Roughness = 0.8;            // Mid roughness (0=shiny, 1=matte)
            pbr.Opacity = 1.0;
            pbr.OpacityIOR = 1.5;

            // Sync to legacy properties for display
            material.DiffuseColor = System.Drawing.Color.White;

            int index = doc.Materials.Add(material);
            RhinoApp.WriteLine($"[McAtlas] Created PBR material '{materialName}' at index {index}");

            return index;
        }

        // Export cesium_massing layer to GLB and cesium_clip layer to clipping polygons
        private string GetGeometryJsonInternal()
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null)
                return @"{""error"":""No active Rhino document""}";

            var layer = doc.Layers.FindName("cesium_massing");
            if (layer == null)
                return @"{""error"":""Layer 'cesium_massing' not found""}";

            // Check if layer is locked - temporarily unlock for export
            bool wasLocked = layer.IsLocked;
            if (wasLocked)
            {
                RhinoApp.WriteLine("[McAtlas] Layer was locked - temporarily unlocking for export");
                layer.IsLocked = false;
            }

            var objs = doc.Objects.FindByLayer(layer);
            if (objs == null || objs.Length == 0)
            {
                if (wasLocked) layer.IsLocked = true;  // Restore lock state
                return @"{""error"":""No objects on 'cesium_massing'""}";
            }

            // Check if EarthAnchorPoint is set
            var anchor = doc.EarthAnchorPoint;
            if (!anchor.EarthLocationIsSet())
                return @"{""error"":""EarthAnchorPoint not set. Please import a map first.""}";

            // ============ DEBUG LOGGING ============
            RhinoApp.WriteLine("[McAtlas] ========== EXPORT START ==========");
            RhinoApp.WriteLine($"[McAtlas] EarthAnchor: lat={anchor.EarthBasepointLatitude:F8}, lon={anchor.EarthBasepointLongitude:F8}");
            RhinoApp.WriteLine($"[McAtlas] Found {objs.Length} objects on cesium_massing layer");

            // Get transformation from model to earth coordinates
            var modelToEarth = anchor.GetModelToEarthTransform(doc.ModelUnitSystem);

            // Calculate lat/lon of Rhino origin (0,0,0)
            // This is where Cesium will place the GLB's origin
            Point3d rhinoOrigin = Point3d.Origin;
            rhinoOrigin.Transform(modelToEarth);

            double lat = rhinoOrigin.Y;  // Transform returns X=lon, Y=lat
            double lon = rhinoOrigin.X;
            double height = 0;           // Origin at ground level

            RhinoApp.WriteLine($"[McAtlas] Rhino origin maps to: lat={lat:F8}, lon={lon:F8}");

            // ============ CLIPPING POLYGONS ============
            var clippingPolygonsJson = GetClippingPolygonsJson(doc, anchor, modelToEarth);

            // ============ DEFAULT MATERIAL PASS ============
            int defaultMatIndex = GetOrCreateDefaultMaterial(doc);
            int fallbackCount = 0;

            foreach (var obj in objs)
            {
                var atts = obj.Attributes.Duplicate();

                bool hasObjectMaterial =
                    atts.MaterialSource == ObjectMaterialSource.MaterialFromObject &&
                    atts.MaterialIndex >= 0;

                bool hasLayerMaterial = false;
                if (atts.LayerIndex >= 0 && atts.LayerIndex < doc.Layers.Count)
                {
                    var objLayer = doc.Layers[atts.LayerIndex];
                    // -1 means no render material assigned
                    hasLayerMaterial = objLayer.RenderMaterialIndex >= 0;
                }

                bool needsFallbackMaterial = !(hasObjectMaterial || hasLayerMaterial);

                if (needsFallbackMaterial)
                {
                    atts.MaterialIndex = defaultMatIndex;
                    atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;

                    // Force a neutral light grey as object colour
                    atts.ColorSource = ObjectColorSource.ColorFromObject;
                    atts.ObjectColor = System.Drawing.Color.FromArgb(245, 245, 245);

                    doc.Objects.ModifyAttributes(obj, atts, true);
                    fallbackCount++;
                }
            }

            RhinoApp.WriteLine($"[McAtlas] Default material applied to {fallbackCount} object(s) with no explicit material");

            // ============ EXPORT GLB ============
            // Export directory: Documents/McAtlas
            var exportDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "McAtlas");
            Directory.CreateDirectory(exportDir);

            var glbPath = Path.Combine(exportDir, "mcatlas_massing.glb");
            RhinoApp.WriteLine($"[McAtlas] Export path: {glbPath}");

            // Select objects on cesium_massing layer
            doc.Objects.UnselectAll();
            foreach (var obj in objs)
                obj.Select(true);

            RhinoApp.WriteLine($"[McAtlas] Selected {objs.Length} objects for export");

            // Build explicit glTF export options (double-sided, no display-color fallback)
            var gltfOptions = new FileGltfWriteOptions
            {
                // Coordinate system
                MapZToY = true,

                // Geometry / shading
                ExportMaterials = true,
                ExportTextureCoordinates = true,
                ExportVertexNormals = true,
                ExportOpenMeshes = true,
                ExportLayers = false,

                // Key flags:
                // false = glTF doubleSided = true (no backface culling)
                CullBackfaces = false,
                // Prevent exporter from inventing materials from display/layer colours
                UseDisplayColorForUnsetMaterials = false,

                // Keep it simple for now
                UseDracoCompression = false
            };

            ArchivableDictionary dict = gltfOptions.ToDictionary();

            RhinoApp.WriteLine("[McAtlas] Exporting GLB with double-sided materials...");
            bool ok = doc.ExportSelected(glbPath, dict);

            // Cleanup selection
            doc.Objects.UnselectAll();

            // Restore layer lock state
            if (wasLocked)
            {
                layer.IsLocked = true;
                RhinoApp.WriteLine("[McAtlas] Restored layer lock state");
            }

            // ============ FILE SIZE CHECK ============
            if (File.Exists(glbPath))
            {
                var fileInfo = new FileInfo(glbPath);
                RhinoApp.WriteLine($"[McAtlas] GLB file size: {fileInfo.Length} bytes ({fileInfo.Length / 1024.0:F1} KB)");
                if (fileInfo.Length < 1000)
                {
                    RhinoApp.WriteLine("[McAtlas] WARNING: GLB file is suspiciously small - export may have failed!");
                }
            }
            else
            {
                RhinoApp.WriteLine("[McAtlas] ERROR: GLB file does not exist after export!");
                ok = false;
            }

            RhinoApp.WriteLine($"[McAtlas] GLB export (double-sided): {(ok ? "SUCCESS" : "FAILED")}");
            RhinoApp.WriteLine("[McAtlas] ========== EXPORT COMPLETE ==========");

            if (!ok)
                return @"{""error"":""Export command failed""}";

            // Build JSON response with position and clipping polygons
            var safePath = glbPath.Replace("\\", "\\\\");
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append(@"""glbPath"":""").Append(safePath).Append("\",");
            sb.Append(@"""position"":{");
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                @"""lat"":{0},""lon"":{1},""height"":{2}", lat, lon, height);
            sb.Append("},");
            sb.Append(@"""clippingPolygons"":").Append(clippingPolygonsJson);
            sb.Append("}");

            return sb.ToString();
        }

        // Get clipping polygons from cesium_clip layer as JSON array
        private string GetClippingPolygonsJson(RhinoDoc doc, EarthAnchorPoint anchor, Transform modelToEarth)
        {
            var clipLayer = doc.Layers.FindName("cesium_clip");
            if (clipLayer == null)
            {
                RhinoApp.WriteLine("[McAtlas] No cesium_clip layer found");
                return "[]";
            }

            var clipObjs = doc.Objects.FindByLayer(clipLayer);
            if (clipObjs == null || clipObjs.Length == 0)
            {
                RhinoApp.WriteLine("[McAtlas] No objects on cesium_clip layer");
                return "[]";
            }

            RhinoApp.WriteLine($"[McAtlas] Found {clipObjs.Length} object(s) on cesium_clip layer");

            var polygons = new List<string>();

            foreach (var obj in clipObjs)
            {
                Curve curve = null;

                // Get curve from object
                if (obj.Geometry is Curve c)
                {
                    curve = c;
                }
                else if (obj.Geometry is Extrusion ext)
                {
                    // Get the base curve of extrusion
                    curve = ext.ToBrep()?.Faces[0]?.OuterLoop?.To3dCurve();
                }
                else if (obj.Geometry is Brep brep)
                {
                    // Get outer loop of first face
                    if (brep.Faces.Count > 0)
                    {
                        curve = brep.Faces[0].OuterLoop?.To3dCurve();
                    }
                }

                if (curve == null)
                {
                    RhinoApp.WriteLine($"[McAtlas] Skipping non-curve object: {obj.Geometry.GetType().Name}");
                    continue;
                }

                if (!curve.IsClosed)
                {
                    RhinoApp.WriteLine("[McAtlas] Skipping open curve (must be closed for clipping)");
                    continue;
                }

                // Get polyline points from curve
                Polyline polyline;
                if (!curve.TryGetPolyline(out polyline))
                {
                    // Convert to polyline with tolerance
                    var nurbs = curve.ToNurbsCurve();
                    if (nurbs != null)
                    {
                        var pline = new PolylineCurve(curve.DivideByCount(64, true)
                            .Select(t => curve.PointAt(t)).ToArray());
                        pline.TryGetPolyline(out polyline);
                    }
                }

                if (polyline == null || polyline.Count < 3)
                {
                    RhinoApp.WriteLine("[McAtlas] Could not convert curve to polyline");
                    continue;
                }

                // Convert points to lat/lon
                var coordList = new List<string>();

                // Remove duplicate closing point if present
                int count = polyline.IsClosed ? polyline.Count - 1 : polyline.Count;

                for (int i = 0; i < count; i++)
                {
                    Point3d pt = polyline[i];
                    Point3d earthPt = pt;
                    earthPt.Transform(modelToEarth);

                    // X = longitude, Y = latitude
                    double ptLon = earthPt.X;
                    double ptLat = earthPt.Y;

                    coordList.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0},{1}", ptLon, ptLat));
                }

                if (coordList.Count >= 3)
                {
                    // Format: [lon1, lat1, lon2, lat2, ...] as flat array for Cesium
                    var flatCoords = string.Join(",", coordList);
                    polygons.Add($"[{flatCoords}]");

                    RhinoApp.WriteLine($"[McAtlas] Clipping polygon: {coordList.Count} vertices");
                }
            }

            if (polygons.Count == 0)
            {
                RhinoApp.WriteLine("[McAtlas] No valid clipping polygons found");
                return "[]";
            }

            RhinoApp.WriteLine($"[McAtlas] Total clipping polygons: {polygons.Count}");
            return "[" + string.Join(",", polygons) + "]";
        }

        // Set EarthAnchorPoint from Tauri coordinates
        private string SetEarthAnchor(string json)
        {
            string result = null;

            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                result = SetEarthAnchorInternal(json);
            }));

            return result ?? @"{""error"":""Failed to set earth anchor""}";
        }

        private string SetEarthAnchorInternal(string json)
        {
            try
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return @"{""error"":""No active Rhino document""}";

                double lat = 0, lon = 0;

                int latIndex = json.IndexOf("\"lat\"");
                if (latIndex >= 0)
                {
                    int colonIndex = json.IndexOf(":", latIndex);
                    int commaIndex = json.IndexOf(",", colonIndex);
                    if (commaIndex < 0) commaIndex = json.IndexOf("}", colonIndex);
                    string latStr = json.Substring(colonIndex + 1, commaIndex - colonIndex - 1).Trim();
                    double.TryParse(latStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out lat);
                }

                int lonIndex = json.IndexOf("\"lon\"");
                if (lonIndex >= 0)
                {
                    int colonIndex = json.IndexOf(":", lonIndex);
                    int commaIndex = json.IndexOf(",", colonIndex);
                    if (commaIndex < 0) commaIndex = json.IndexOf("}", colonIndex);
                    string lonStr = json.Substring(colonIndex + 1, commaIndex - colonIndex - 1).Trim();
                    double.TryParse(lonStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out lon);
                }

                RhinoApp.WriteLine($"[McAtlas] Setting EarthAnchorPoint: lat={lat:F8}, lon={lon:F8}");

                var anchor = doc.EarthAnchorPoint;

                anchor.EarthBasepointLatitude = lat;
                anchor.EarthBasepointLongitude = lon;
                anchor.EarthBasepointElevation = 0;
                anchor.ModelBasePoint = Point3d.Origin;
                anchor.ModelNorth = new Vector3d(0, 1, 0);
                anchor.ModelEast = new Vector3d(1, 0, 0);

                doc.EarthAnchorPoint = anchor;

                RhinoApp.WriteLine($"[McAtlas] EarthAnchorPoint set successfully!");

                return @"{""success"":true}";
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[McAtlas] Error setting EarthAnchorPoint: {ex.Message}");
                return $@"{{""error"":""{ex.Message}""}}";
            }
        }

        // Import map image from Tauri
        private string ImportMapImage(string json)
        {
            string result = null;

            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                result = ImportMapImageInternal(json);
            }));

            return result ?? @"{""error"":""Failed to import map image""}";
        }

        private string ImportMapImageInternal(string json)
        {
            try
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return @"{""error"":""No active Rhino document""}";

                string imageBase64 = ExtractJsonString(json, "imageBase64");
                double sizeMeters = ExtractJsonDouble(json, "sizeMeters");
                int pixelWidth = (int)ExtractJsonDouble(json, "pixelWidth");
                int pixelHeight = (int)ExtractJsonDouble(json, "pixelHeight");

                if (string.IsNullOrEmpty(imageBase64))
                    return @"{""error"":""No image data received""}";

                RhinoApp.WriteLine($"[McAtlas] Receiving map image: {pixelWidth}x{pixelHeight}, {sizeMeters:F0}m");

                var exportDir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                    "McAtlas");
                Directory.CreateDirectory(exportDir);

                var imagePath = Path.Combine(exportDir, "mcatlas_map.jpg");

                byte[] imageBytes = Convert.FromBase64String(imageBase64);
                File.WriteAllBytes(imagePath, imageBytes);

                RhinoApp.WriteLine($"[McAtlas] Image saved: {imagePath}");

                double halfSize = sizeMeters / 2.0;

                var plane = Plane.WorldXY;

                var pictureFrame = doc.Objects.AddPictureFrame(
                    plane,
                    imagePath,
                    false,
                    sizeMeters,
                    sizeMeters,
                    false,
                    false
                );

                if (pictureFrame == Guid.Empty)
                    return @"{""error"":""Failed to create PictureFrame""}";

                var xform = Transform.Translation(-halfSize, -halfSize, 0);
                doc.Objects.Transform(pictureFrame, xform, true);

                var layerName = "mcatlas_map";
                var layer = doc.Layers.FindName(layerName);
                if (layer == null)
                {
                    var newLayer = new Layer
                    {
                        Name = layerName,
                        Color = System.Drawing.Color.Gray
                    };
                    doc.Layers.Add(newLayer);
                    layer = doc.Layers.FindName(layerName);
                }

                var obj = doc.Objects.FindId(pictureFrame);
                if (obj != null)
                {
                    obj.Attributes.LayerIndex = layer.Index;
                    obj.CommitChanges();
                }

                doc.Views.Redraw();

                RhinoApp.WriteLine($"[McAtlas] PictureFrame created: {sizeMeters:F0}m x {sizeMeters:F0}m centered at origin");

                var safePath = imagePath.Replace("\\", "\\\\");
                return $@"{{""success"":true,""imagePath"":""{safePath}""}}";
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[McAtlas] Error importing map image: {ex.Message}");
                return $@"{{""error"":""{ex.Message}""}}";
            }
        }

        // Helper: Extract string value from JSON
        private string ExtractJsonString(string json, string key)
        {
            var searchKey = $"\"{key}\":\"";
            int startIndex = json.IndexOf(searchKey);
            if (startIndex < 0) return null;

            startIndex += searchKey.Length;
            int endIndex = json.IndexOf("\"", startIndex);
            if (endIndex < 0) return null;

            return json.Substring(startIndex, endIndex - startIndex);
        }

        // Helper: Extract double value from JSON
        private double ExtractJsonDouble(string json, string key)
        {
            var searchKey = $"\"{key}\":";
            int startIndex = json.IndexOf(searchKey);
            if (startIndex < 0) return 0;

            startIndex += searchKey.Length;
            int endIndex = json.IndexOfAny(new char[] { ',', '}' }, startIndex);
            if (endIndex < 0) return 0;

            string valueStr = json.Substring(startIndex, endIndex - startIndex).Trim().Trim('"');
            double.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value);

            return value;
        }

        // Stop the HTTP server
        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _listener?.Stop();
            _listener?.Close();

            RhinoApp.WriteLine("McAtlas server stopped");
        }
    }
}