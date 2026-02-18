namespace Sandbox;

internal static partial class DebugOverlay
{
	public partial class ToolsVisualization
	{
		[ConVar( "mat_toolsvis", Help = "Set the render debug visualization mode" )]
		internal static SceneCameraDebugMode mat_toolsvis
		{
			get => Application.GetActiveScene()?.Camera?.DebugMode ?? SceneCameraDebugMode.Normal;
			set
			{
				if ( Application.GetActiveScene()?.Camera is CameraComponent cam )
					cam.DebugMode = value;
			}
		}

		internal static void Draw( ref Vector2 pos )
		{
			var debugMode = mat_toolsvis;

			var title = debugMode.GetAttributeOfType<TitleAttribute>()?.Value ?? debugMode.ToString();
			var icon = debugMode.GetAttributeOfType<IconAttribute>()?.Value ?? "image";

			var iconScope = new TextRendering.Scope( icon, Color.White.WithAlpha( 0.8f ), 14, "Material Icons", 400 )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Size = 3, Enabled = true }
			};
			var iconRect = Hud.DrawText( iconScope, pos, TextFlag.LeftTop );

			var labelScope = new TextRendering.Scope( $"Tools Visualization Mode: {title}", Color.White, 12, "Roboto Mono", 700 )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Size = 3, Enabled = true }
			};
			Hud.DrawText( labelScope, pos with { x = pos.x + iconRect.Width + 4 }, TextFlag.LeftTop );

			pos.y += 20;
		}
	}
}
