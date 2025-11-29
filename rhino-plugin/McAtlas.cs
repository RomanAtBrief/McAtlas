// Import C# namespaces
using System;
using System.Collections.Generic;

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
            // Write to Rhino Console
            RhinoApp.WriteLine("3d Maps will be soon");
            
            // Return success
            return Result.Success;
        }
    }
}