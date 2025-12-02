// Import C# namespaces
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

// Import RhinoCommon namespaces
using Rhino;

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
                // For now, return test data (we'll read real geometry later)
                string json = @"{
                    ""type"": ""cube"",
                    ""position"": { ""lat"": 40.7580, ""lon"": -73.9855, ""height"": 0 },
                    ""size"": { ""width"": 20, ""depth"": 30, ""height"": 80 }
                }";
                
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                response.StatusCode = 200;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                
                RhinoApp.WriteLine("Sent geometry to Tauri app");
            }
            else
            {
                // Unknown endpoint
                response.StatusCode = 404;
            }
            
            response.Close();
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