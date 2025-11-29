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

// Namespace
namespace rhino_plugin
{
    // 1. Create New class that inherits from Rhino Command
    public class McAtlas : Command
    {
        // 2. Create command constractor
        public McAtlas()
        {
            Instance = this;
        }

        // 3. Create the only instance of this command
        public static McAtlas Instance { get; private set; }

        // 4. The command name as it appears on the Rhino command line
        public override string EnglishName => "McAtlas";

        // 5. Actual command code
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            try
            {
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
                
                RhinoApp.WriteLine("McAtlas launched!");
                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Error: {ex.Message}");
                return Result.Failure;
            }
        }
    }
}