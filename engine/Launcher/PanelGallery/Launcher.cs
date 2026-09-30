global using Sandbox;
global using Sandbox.UI;
global using Sandbox.UI.Construct;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using Editor;
global using Microsoft.AspNetCore.Components;
global using Microsoft.AspNetCore.Components.Rendering;
global using static Sandbox.Internal.GlobalToolsNamespace;

namespace Sandbox;

public static class Launcher
{
	public static int Main()
	{
		var appSystem = new PanelGallery.PanelGalleryAppSystem();
		appSystem.Run();

		return 0;
	}
}
