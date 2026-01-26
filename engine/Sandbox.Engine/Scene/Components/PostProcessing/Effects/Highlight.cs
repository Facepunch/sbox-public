using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// This should be added to a camera that you want to outline stuff
/// </summary>
[Title( "Highlight" )]
[Category( "Post Processing" )]
[Icon( "lightbulb_outline" )]
public sealed class Highlight : BasePostProcess<Highlight>
{
	CommandList commands = new();

	private static readonly Material Shader = Material.FromShader( "postprocess/objecthighlight/objecthighlight.shader" );

	public override void Render()
	{
		var outlines = Scene.GetAll<HighlightOutline>();
		if ( !outlines.Any() ) return;

		commands.Reset();

		// Copy the depth buffer once
		commands.Attributes.GrabFrameTexture( "ColorTexture" );

		// Generate a temporary render target to draw the stencil to, also so we don't clash with the main depth buffer
		var rt = commands.GetRenderTarget( "Highlight", 1, ImageFormat.None, ImageFormat.D24S8, msaa: MultisampleAmount.MultisampleScreen );

		commands.SetRenderTarget( rt );
		commands.Clear( Color.Black, false, true, true );

		// Draw the stencil first before drawing the outside outline
		foreach ( var glow in outlines ) { DrawGlow( commands, glow, OutlinePass.Inside ); }
		foreach ( var glow in outlines ) { DrawGlow( commands, glow, OutlinePass.Outside ); }

		commands.ClearRenderTarget();

		InsertCommandList( commands, Stage.AfterTransparent, 1000, "Highlight" );
	}


	enum OutlinePass
	{
		Inside,
		Outside,
	}

	private void DrawGlow( CommandList commands, HighlightOutline glow, OutlinePass pass )
	{
		var attributes = commands.Attributes;

		foreach ( var model in glow.GetOutlineTargets() )
		{
			var shapeMat = glow.Material ?? Shader;

			// Inside glow and stencil
			if ( pass == OutlinePass.Inside )
			{
				attributes.SetCombo( "D_OUTLINE_PASS", (int)OutlinePass.Inside );
				attributes.Set( "Color", glow.InsideColor );
				attributes.Set( "ObscuredColor", glow.InsideObscuredColor );

				commands.DrawRenderer( model, new RendererSetup { Material = shapeMat } );
			}

			// Outside glow
			if ( glow.Width > 0.0f && pass == OutlinePass.Outside && (glow.Color != Color.Transparent || glow.ObscuredColor != Color.Transparent) )
			{
				attributes.SetCombo( "D_OUTLINE_PASS", (int)OutlinePass.Outside );
				attributes.Set( "Color", glow.Color );
				attributes.Set( "ObscuredColor", glow.ObscuredColor );
				attributes.Set( "LineWidth", glow.Width );

				commands.DrawRenderer( model, new RendererSetup { Material = shapeMat } );
			}
		}
	}

}
