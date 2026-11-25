namespace Sandbox;

internal static partial class DebugOverlay
{
	public partial class ToolsVisualization
	{
		[ConVar( "mat_toolsvis", Help = "Set the render debug visualization mode", Flags = ConVarFlags.Protected | ConVarFlags.Cheat )]
		internal static SceneCameraDebugMode mat_toolsvis { get; set; }

		internal static void Draw( Painter painter, ref Vector2 pos )
		{
			var debugMode = mat_toolsvis;

			var title = debugMode.GetAttributeOfType<TitleAttribute>()?.Value ?? debugMode.ToString();
			var icon = debugMode.GetAttributeOfType<IconAttribute>()?.Value ?? "image";

			var iconScope = new TextRendering.Scope( icon, Color.White.WithAlpha( 0.8f ), 14, "Material Symbols Outlined", 400 )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Size = 3, Enabled = true }
			};
			var iconRect = DebugOverlay.DrawText( painter, iconScope, pos, TextFlag.LeftTop );

			var labelScope = new TextRendering.Scope( $"Tools Visualization Mode: {title}", Color.White, 12, "Roboto Mono", 700 )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Size = 3, Enabled = true }
			};
			DebugOverlay.DrawText( painter, labelScope, pos with { x = pos.x + iconRect.Width + 4 }, TextFlag.LeftTop );

			pos.y += 20;
		}

		/// <summary>
		/// Why a visibility ids view (<see cref="SceneCameraDebugMode.VisibilityTriangles"/> and the two after it) shows no ids,
		/// or null while it draws them or <paramref name="mode"/> is another view. From the last ids view Borealis drew
		/// (<see cref="Borealis.IdsViewLayer.LastResult"/>), so it says the same in every view in that mode.
		/// </summary>
		internal static string IdsViewNotice( int mode )
		{
			if ( mode is not ((int)SceneCameraDebugMode.VisibilityTriangles or (int)SceneCameraDebugMode.VisibilityInstances or (int)SceneCameraDebugMode.VisibilityDraws) )
				return null;

			var at = Borealis.IdsViewLayer.LastAt;
			if ( at == 0 || Environment.TickCount64 - at > 1000 )
				return "No visibility ids: this view isn't drawn on Borealis.Gpu's GPU-driven path";

			return Borealis.IdsViewLayer.LastResult switch
			{
				Borealis.IdsViewResult.Drawn => null,
				Borealis.IdsViewResult.NoInstances => "No instance ids yet: the view's ids are being made with instance records",
				_ => "No visibility ids in this view: it draws no GPU-driven depth prepass with visibility",
			};
		}

		/// <summary>
		/// Over a camera's frame after its UI (<see cref="SceneCamera"/>'s <c>AfterUI</c> stage), in the game and the editor's
		/// viewports: <see cref="IdsViewNotice"/>, where the view's mode is an ids view that has nothing to show.
		/// </summary>
		internal static void DrawIdsViewNotice()
		{
			if ( IdsViewNotice( Graphics.ToolsVisMode ) is not { } notice )
				return;

			var viewport = Graphics.Viewport;
			var scope = new TextRendering.Scope( notice, Color.White, 14, "Roboto Mono", 700 )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Size = 3, Enabled = true }
			};
			Graphics.DrawText( new Rect( viewport.Left, viewport.Top + viewport.Height * 0.5f - 20, viewport.Width, 40 ), scope, TextFlag.Center );
		}
	}
}
