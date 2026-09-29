using Sandbox.Rendering;

namespace Sandbox;

public sealed partial class CameraComponent : Component, Component.ExecuteInEditor
{
	internal PostProcessLayers PostProcess { get; private set; } = new PostProcessLayers();

	/// <summary>
	/// Whether this camera's post processing reads the depth-normals prepass's G-buffer (AO, SSR), so the managed scene
	/// renderer draws it.
	/// </summary>
	bool WantsDepthNormals() => PostProcess.NeedsDepthNormals;

	/// <summary>
	/// Whether post processing at <paramref name="stage"/> can run on the async compute queue (<see cref="BasePostProcess.AsyncCompute"/>).
	/// </summary>
	bool HasAsyncCompute( Stage stage ) => PostProcessSystem.EnablePostProcess && PostProcess.HasAsyncCompute( stage );

	/// <summary>
	/// Run it, under the conditions the stage would (<c>PostProcessSystem.OnRenderStage</c>). On the render thread.
	/// </summary>
	void RenderAsyncCompute( Stage stage )
	{
		if ( !PostProcessSystem.EnablePostProcess || !Graphics.PostProcessEnabled ) return;
		PostProcess.RenderAsyncCompute( stage );
	}

	/// <summary>
	/// Enable or disable post processing for this camera.
	/// </summary>
	[Header( "Post Processing" )]
	[Property]
	public bool EnablePostProcessing { get; set; } = true;

	/// <summary>
	/// If set then we'll trigger post process volumes from this position, instead of the camera's position.
	/// </summary>
	[Property]
	public GameObject PostProcessAnchor { get; set; }


	internal void PrintPostProcessDebugOverlay( ref Vector2 pos, Painter painter )
	{
		var text = $"";

		foreach ( var group in PostProcess.Layers.OrderBy( x => x.Key ) )
		{
			text += $"{group.Key}\n";

			foreach ( var layer in group.Value.OrderBy( x => x.Order ) )
			{
				text += $"\t{layer.Order,6:D}: {layer.Name}\n";
			}
		}

		var textScope = TextRendering.Scope.Default;
		textScope.Text = text;
		textScope.FontSize = 11;
		textScope.FontName = "Roboto Mono";
		textScope.FontWeight = 600;
		textScope.Outline = new TextRendering.Outline { Enabled = true, Color = Color.Black.WithAlpha( 0.9f ), Size = 3 };
		textScope.Shadow = new TextRendering.Shadow { Enabled = true, Color = Color.Black.WithAlpha( 0.5f ), Offset = 0, Size = 2 };

		var drawnRect = Sandbox.DebugOverlay.DrawText( painter, textScope, pos, flags: TextFlag.LeftTop );
		pos.y = drawnRect.Bottom;
	}
}
