// Import C# namespaces
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

// Import RhinoCommon namespaces
using Rhino;
using Rhino.Geometry;
using Rhino.Commands;
using Rhino.Input.Custom;
using Rhino.DocObjects;

// Namespace
namespace rhino_plugin
{
    // Create New class that inherits from Rhino Command
    public class McAtlas : Command
    {
        // Create command constructor
        public McAtlas()
        {
            Instance = this;
        }

        // Create the only instance of this command
        public static McAtlas Instance { get; private set; }

        // The command name as it appears on the Rhino command line
        public override string EnglishName => "McAtlas";

        // Actual command code
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            try
            {
                // Create special layers if they don't exist
                CreateLayers(doc);
                
                // Path to the Tauri app in Applications folder
                string appPath = "/Applications/tauri-app.app";

                // Check if app exists
                if (!Directory.Exists(appPath))
                {
                    RhinoApp.WriteLine("McAtlas app not found in Applications folder.");
                    return Result.Failure;
                }

                // Launch the Tauri app
                Process.Start("open", appPath);
                
                RhinoApp.WriteLine("McAtlas launched! Layers created.");
                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error: {ex.Message}");
                return Result.Failure;
            }
        }
        
        // Create cesium layers if they don't exist
        private void CreateLayers(RhinoDoc doc)
        {
            // Create cesium_massing layer (for 3D models)
            if (doc.Layers.FindName("cesium_massing") == null)
            {
                var massingLayer = new Layer();
                massingLayer.Name = "cesium_massing";
                massingLayer.Color = System.Drawing.Color.Blue;
                doc.Layers.Add(massingLayer);
                RhinoApp.WriteLine("Created layer: cesium_massing");
            }
            
            // Create cesium_clip layer (for clipping polyline)
            if (doc.Layers.FindName("cesium_clip") == null)
            {
                var clipLayer = new Layer();
                clipLayer.Name = "cesium_clip";
                clipLayer.Color = System.Drawing.Color.Red;
                doc.Layers.Add(clipLayer);
                RhinoApp.WriteLine("Created layer: cesium_clip");
            }
        }
    }
}