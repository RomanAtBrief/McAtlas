// Import C# namespaces
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

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
                    if (_isRunning) // Only log if we didn't intentionally stop
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
            // Check if default material already exists
            var materialName = "McAtlas_Default_Gray";

            for (int i = 0; i < doc.Materials.Count; i++)
            {
                if (doc.Materials[i].Name == materialName)
                    return i;
            }

            // Create new simple material (not PBR to avoid compatibility issues)
            var material = new Material();
            material.Name = materialName;

            // Light gray color
            material.DiffuseColor = System.Drawing.Color.FromArgb(245, 245, 245);
            material.SpecularColor = System.Drawing.Color.FromArgb(255, 255, 255);
            material.Shine = 0.8;

            // Add to document
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

            // Get or create default material
            int defaultMatIndex = GetOrCreateDefaultMaterial(doc);

            // Export directory: Documents/McAtlas
            var exportDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "McAtlas");
            Directory.CreateDirectory(exportDir);

            var glbPath = Path.Combine(exportDir, "mcatlas_massing.glb");

            // Deselect all
            doc.Objects.UnselectAll();

            // Assign default material to objects without materials, then select
            foreach (var obj in objs)
            {
                // If object has no material (using layer material), assign default
                if (obj.Attributes.MaterialIndex == -1 || obj.Attributes.MaterialSource == ObjectMaterialSource.MaterialFromLayer)
                {
                    obj.Attributes.MaterialIndex = defaultMatIndex;
                    obj.Attributes.MaterialSource = ObjectMaterialSource.MaterialFromObject;
                    obj.CommitChanges();
                }

                obj.Select(true);
            }

            // Export selected objects to glTF/GLB using Rhino's command-line exporter
            // .glb extension forces the glTF exporter. Extra _Enter for default options.
            var exportCmd = $"_-Export \"{glbPath}\" _Enter _Enter";
            bool ok = RhinoApp.RunScript(exportCmd, false);

            // Deselect all
            doc.Objects.UnselectAll();

            if (!ok)
                return @"{""error"":""Export command failed""}";

            // Hard-coded position for prototype (Freedom Tower)
            const double lat = 40.7063;
            const double lon = -74.0037;
            const double height = 0.0;

            // Build JSON with escaped path
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

                // Parse JSON manually (simple parsing for lat/lon)
                // Expected format: {"lat": 40.123, "lon": -74.456}
                double lat = 0, lon = 0;

                // Extract lat
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

                // Extract lon
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

                RhinoApp.WriteLine($"[McAtlas] Setting EarthAnchorPoint: lat={lat}, lon={lon}");

                // Get EarthAnchorPoint
                var anchor = doc.EarthAnchorPoint;

                // Set the anchor point
                // EarthAnchorPoint maps a model point to a geographic location
                // We want origin (0,0,0) to map to our lat/lon
                anchor.EarthBasepointLatitude = lat;
                anchor.EarthBasepointLongitude = lon;
                anchor.EarthBasepointElevation = 0;
                anchor.ModelBasePoint = Point3d.Origin;
                anchor.ModelNorth = new Vector3d(0, 1, 0); // Y-axis is North
                anchor.ModelEast = new Vector3d(1, 0, 0);  // X-axis is East

                // Apply to document
                doc.EarthAnchorPoint = anchor;

                RhinoApp.WriteLine($"[McAtlas] EarthAnchorPoint set successfully!");
                RhinoApp.WriteLine($"[McAtlas] Origin (0,0,0) = lat:{lat}, lon:{lon}");

                return @"{""success"":true}";
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[McAtlas] Error setting EarthAnchorPoint: {ex.Message}");
                return $@"{{""error"":""{ex.Message}""}}";
            }
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