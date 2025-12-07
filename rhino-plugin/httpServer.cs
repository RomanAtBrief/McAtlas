// Import C# namespaces
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

// Import RhinoCommon namespaces
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Display;

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

        // Get or create default light gray material
        private int GetOrCreateDefaultMaterial(RhinoDoc doc)
        {
            var materialName = "McAtlas_Default_Gray";

            for (int i = 0; i < doc.Materials.Count; i++)
            {
                if (doc.Materials[i].Name == materialName)
                    return i;
            }

            var material = new Material();
            material.Name = materialName;
            material.DiffuseColor = System.Drawing.Color.FromArgb(245, 245, 245);
            material.SpecularColor = System.Drawing.Color.FromArgb(255, 255, 255);
            material.Shine = 0.8;

            int index = doc.Materials.Add(material);
            return index;
        }

        // Export cesium_massing layer to GLB on disk (UI thread) and return JSON
        private string GetGeometryJsonInternal()
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null)
                return @"{""error"":""No active Rhino document""}";

            var layer = doc.Layers.FindName("cesium_massing");
            if (layer == null)
                return @"{""error"":""Layer 'cesium_massing' not found""}";

            var objs = doc.Objects.FindByLayer(layer);
            if (objs == null || objs.Length == 0)
                return @"{""error"":""No objects on 'cesium_massing'""}";

            // Check if EarthAnchorPoint is set
            var anchor = doc.EarthAnchorPoint;
            if (!anchor.EarthLocationIsSet())
                return @"{""error"":""EarthAnchorPoint not set. Please import a map first.""}";

            // ============ DEBUG LOGGING ============
            RhinoApp.WriteLine("[McAtlas] ========== COORDINATE DEBUG ==========");
            RhinoApp.WriteLine($"[McAtlas] EarthAnchor.Latitude: {anchor.EarthBasepointLatitude:F8}");
            RhinoApp.WriteLine($"[McAtlas] EarthAnchor.Longitude: {anchor.EarthBasepointLongitude:F8}");
            RhinoApp.WriteLine($"[McAtlas] EarthAnchor.ModelBasePoint: ({anchor.ModelBasePoint.X:F3}, {anchor.ModelBasePoint.Y:F3}, {anchor.ModelBasePoint.Z:F3})");
            RhinoApp.WriteLine($"[McAtlas] Doc.ModelUnitSystem: {doc.ModelUnitSystem}");

            // Calculate bounding box of all objects
            BoundingBox bbox = BoundingBox.Empty;
            foreach (var obj in objs)
            {
                bbox.Union(obj.Geometry.GetBoundingBox(true));
            }

            RhinoApp.WriteLine($"[McAtlas] BBox.Min: ({bbox.Min.X:F3}, {bbox.Min.Y:F3}, {bbox.Min.Z:F3})");
            RhinoApp.WriteLine($"[McAtlas] BBox.Max: ({bbox.Max.X:F3}, {bbox.Max.Y:F3}, {bbox.Max.Z:F3})");

            // Get center point at base (Z = bbox.Min.Z)
            Point3d modelCenter = new Point3d(
                (bbox.Min.X + bbox.Max.X) / 2.0,
                (bbox.Min.Y + bbox.Max.Y) / 2.0,
                bbox.Min.Z
            );

            RhinoApp.WriteLine($"[McAtlas] Model center in Rhino: ({modelCenter.X:F3}, {modelCenter.Y:F3}, {modelCenter.Z:F3})");

            // Get transformation from model to earth coordinates
            var modelToEarth = anchor.GetModelToEarthTransform(doc.ModelUnitSystem);

            // Transform the center point to get lat/lon
            Point3d earthPoint = modelCenter;
            earthPoint.Transform(modelToEarth);

            // NOTE: Transform returns X=longitude, Y=latitude, Z=elevation
            double lat = earthPoint.Y;
            double lon = earthPoint.X;
            double height = bbox.Min.Z;

            RhinoApp.WriteLine($"[McAtlas] Transform result: lat={lat:F8}, lon={lon:F8}");

            // ============ COORDINATE VERIFICATION ============
            RhinoApp.WriteLine($"[McAtlas] === COORDINATE VERIFICATION ===");
            RhinoApp.WriteLine($"[McAtlas] Model offset from origin: X={modelCenter.X:F3}m, Y={modelCenter.Y:F3}m");

            // Calculate what the lat/lon offset SHOULD be
            // At this latitude, 1 degree lat ≈ 111,320m, 1 degree lon ≈ 111,320 * cos(lat)
            double latRadians = anchor.EarthBasepointLatitude * Math.PI / 180.0;
            double metersPerDegreeLat = 111320.0;
            double metersPerDegreeLon = 111320.0 * Math.Cos(latRadians);

            double expectedLatOffset = modelCenter.Y / metersPerDegreeLat;
            double expectedLonOffset = modelCenter.X / metersPerDegreeLon;

            double expectedLat = anchor.EarthBasepointLatitude + expectedLatOffset;
            double expectedLon = anchor.EarthBasepointLongitude + expectedLonOffset;

            RhinoApp.WriteLine($"[McAtlas] Meters per degree: lat={metersPerDegreeLat:F1}, lon={metersPerDegreeLon:F1}");
            RhinoApp.WriteLine($"[McAtlas] Expected offset: dLat={expectedLatOffset:F8}, dLon={expectedLonOffset:F8}");
            RhinoApp.WriteLine($"[McAtlas] Expected result: lat={expectedLat:F8}, lon={expectedLon:F8}");
            RhinoApp.WriteLine($"[McAtlas] Transform result: lat={lat:F8}, lon={lon:F8}");
            RhinoApp.WriteLine($"[McAtlas] Difference: dLat={lat - expectedLat:F8}, dLon={lon - expectedLon:F8}");

            // Convert difference to meters
            double latErrorMeters = (lat - expectedLat) * metersPerDegreeLat;
            double lonErrorMeters = (lon - expectedLon) * metersPerDegreeLon;
            RhinoApp.WriteLine($"[McAtlas] Error in meters: X={lonErrorMeters:F2}m, Y={latErrorMeters:F2}m");
            RhinoApp.WriteLine($"[McAtlas] === END VERIFICATION ===");
            // ==================================================

            RhinoApp.WriteLine($"[McAtlas] Height (bbox.Min.Z): {height:F3}");

            // Get or create default material
            int defaultMatIndex = GetOrCreateDefaultMaterial(doc);

            // Export directory: Documents/McAtlas
            var exportDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "McAtlas");
            Directory.CreateDirectory(exportDir);

            var glbPath = Path.Combine(exportDir, "mcatlas_massing.glb");

            // ============ CREATE CENTERED COPIES FOR EXPORT ============
            // We need to export geometry centered at origin so Cesium places it correctly
            var moveToOrigin = Transform.Translation(-modelCenter.X, -modelCenter.Y, -bbox.Min.Z);

            RhinoApp.WriteLine($"[McAtlas] Moving geometry to origin for export...");

            var tempGuids = new List<Guid>();
            foreach (var obj in objs)
            {
                // Duplicate geometry and transform to origin
                var dupGeom = obj.Geometry.Duplicate();
                dupGeom.Transform(moveToOrigin);

                // Copy attributes and apply material
                var attributes = obj.Attributes.Duplicate();
                if (attributes.MaterialIndex == -1 || attributes.MaterialSource == ObjectMaterialSource.MaterialFromLayer)
                {
                    attributes.MaterialIndex = defaultMatIndex;
                    attributes.MaterialSource = ObjectMaterialSource.MaterialFromObject;
                }

                // Add temp object to doc
                var guid = doc.Objects.Add(dupGeom, attributes);
                tempGuids.Add(guid);
            }

            RhinoApp.WriteLine($"[McAtlas] Created {tempGuids.Count} temp objects at origin");

            // Deselect all, then select only temp objects
            doc.Objects.UnselectAll();
            foreach (var guid in tempGuids)
            {
                var tempObj = doc.Objects.FindId(guid);
                if (tempObj != null)
                    tempObj.Select(true);
            }

            // Export selected objects to glTF/GLB
            var exportCmd = $"_-Export \"{glbPath}\" _Enter _Enter";
            bool ok = RhinoApp.RunScript(exportCmd, false);

            // Delete temp objects
            foreach (var guid in tempGuids)
            {
                doc.Objects.Delete(guid, true);
            }

            doc.Objects.UnselectAll();

            RhinoApp.WriteLine($"[McAtlas] Temp objects deleted, export ok={ok}");
            RhinoApp.WriteLine("[McAtlas] ========== END DEBUG ==========");

            if (!ok)
                return @"{""error"":""Export command failed""}";

            // Build JSON with calculated position
            var safePath = glbPath.Replace("\\", "\\\\");
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append(@"""glbPath"":""").Append(safePath).Append("\",");
            sb.Append(@"""position"":{");
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                @"""lat"":{0},""lon"":{1},""height"":{2}", lat, lon, height);
            sb.Append("}");
            sb.Append("}");

            return sb.ToString();
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
                    var newLayer = new Layer();
                    newLayer.Name = layerName;
                    newLayer.Color = System.Drawing.Color.Gray;
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