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

            int defaultMatIndex = GetOrCreateDefaultMaterial(doc);

            var exportDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "McAtlas");
            Directory.CreateDirectory(exportDir);

            var glbPath = Path.Combine(exportDir, "mcatlas_massing.glb");

            doc.Objects.UnselectAll();

            foreach (var obj in objs)
            {
                if (obj.Attributes.MaterialIndex == -1 || obj.Attributes.MaterialSource == ObjectMaterialSource.MaterialFromLayer)
                {
                    obj.Attributes.MaterialIndex = defaultMatIndex;
                    obj.Attributes.MaterialSource = ObjectMaterialSource.MaterialFromObject;
                    obj.CommitChanges();
                }

                obj.Select(true);
            }

            var exportCmd = $"_-Export \"{glbPath}\" _Enter _Enter";
            bool ok = RhinoApp.RunScript(exportCmd, false);

            doc.Objects.UnselectAll();

            if (!ok)
                return @"{""error"":""Export command failed""}";

            const double lat = 40.7063;
            const double lon = -74.0037;
            const double height = 0.0;

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

                RhinoApp.WriteLine($"[McAtlas] Setting EarthAnchorPoint: lat={lat}, lon={lon}");

                var anchor = doc.EarthAnchorPoint;

                anchor.EarthBasepointLatitude = lat;
                anchor.EarthBasepointLongitude = lon;
                anchor.EarthBasepointElevation = 0;
                anchor.ModelBasePoint = Point3d.Origin;
                anchor.ModelNorth = new Vector3d(0, 1, 0);
                anchor.ModelEast = new Vector3d(1, 0, 0);

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