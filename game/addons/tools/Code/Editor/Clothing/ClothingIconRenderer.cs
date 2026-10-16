namespace Editor;

/// <summary>
/// Shared rendering for local clothing icons and workshop inventory exports.
/// </summary>
public static class ClothingIconRenderer
{
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

		using var bitmap = new Bitmap( 2048, 2048 );
		preview.Scene.Camera.RenderToBitmap( bitmap );
		return bitmap.Resize( 512, 512 );
	}
}
