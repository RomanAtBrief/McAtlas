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

            // Export directory: Documents/McAtlas
            var exportDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "McAtlas");
            Directory.CreateDirectory(exportDir);

            var glbPath = Path.Combine(exportDir, "mcatlas_massing.glb");

            // Deselect all
            doc.Objects.UnselectAll();

            // Select objects on cesium_massing layer
            foreach (var obj in objs)
            {
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

            // Hard-coded position for prototype (Manhattan)
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