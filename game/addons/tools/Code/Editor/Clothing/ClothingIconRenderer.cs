using Sandbox.Resources;
using System.Text.Json.Nodes;

namespace Editor;

/// <summary>
/// Shared rendering for local clothing icons and workshop inventory exports.
/// </summary>
public static class ClothingIconRenderer
{
	/// <summary>
	/// Creates a compiled icon texture alongside its PNG source, which remains available for backend uploads.
	/// </summary>
	public static Asset CompileTexture( Asset imageAsset )
	{
		var texturePath = System.IO.Path.ChangeExtension( imageAsset.GetSourceFile( true ), ".vtex" );
		var definition = new EmbeddedResource
		{
			ResourceCompiler = "texture",
			ResourceGenerator = "imagefile",
			Data = new JsonObject
			{
				// Image assets can share an identity across JPG and PNG sources. Use the exact PNG we saved.
				["FilePath"] = System.IO.Path.ChangeExtension( imageAsset.RelativePath, ".png" ),
				["Lossless"] = true
			}
		};

		System.IO.File.WriteAllText( texturePath, Json.Serialize( definition ) );

		var textureAsset = AssetSystem.RegisterFile( texturePath );
		textureAsset.Compile( false );

		return textureAsset;
	}

	/// <summary>
	/// Renders the installed clothing at inventory icon resolution after settling its pose.
	/// The caller owns the returned bitmap and the preview scene.
	/// </summary>
	public static Bitmap Render( ClothingScene preview )
	{
		if ( !preview.HasRenderableClothing )
			throw new InvalidOperationException( "The clothing has no renderable Human model." );

		preview.Scene.EditorTick( RealTime.Now - 2, 1 );
		preview.Scene.EditorTick( RealTime.Now - 1, 1 );
		preview.Update();

		// Alpha-tested hair needs sample coverage even when the user's MSAA is disabled.
		// The depth target fixes the sample count; color is resolved before readback.
		using var target = Texture.CreateRenderTarget()
			.WithSize( 2048, 2048 )
			.WithFormat( ImageFormat.RGBA8888 )
			.Create();
		using var depth = Texture.CreateRenderTarget()
			.WithSize( 2048, 2048 )
			.WithDepthFormat()
			.WithMSAA( MultisampleAmount.Multisample8x )
			.Create();

		if ( !preview.Scene.Camera.RenderToTexture( target, depth, new Sandbox.Rendering.ViewSetup { EnableUI = false } ) )
			throw new InvalidOperationException( "Could not render the clothing icon." );

		using var bitmap = target.GetBitmap();
		return bitmap.Resize( 512, 512 );
	}
}
