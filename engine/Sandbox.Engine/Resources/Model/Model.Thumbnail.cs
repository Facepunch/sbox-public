namespace Sandbox;

/// <summary>
/// Model thumbnail rendering.
/// </summary>
public sealed partial class Model
{
	/// <summary>
	/// Render the model at double resolution and downsample to the spawn menu's icon size.
	/// Bulk bakes release newly loaded mount models after rendering.
	/// </summary>
	internal static async Task<Bitmap> RenderSpawnIconAsync( string path, bool forBake = false )
	{
		var model = forBake && Mounting.MountUtility.FindLoader( path ) is { } loader
			? await loader.GetOrCreate( cacheResult: false ) as Model
			: await LoadAsync( path );

		using var image = model?.RenderThumbnail( new() { Width = 512, Height = 512 } );
		return image?.Resize( 256, 256 );
	}

	/// <summary>
	/// Render this model to a thumbnail with a transparent background.
	/// </summary>
	public override Bitmap RenderThumbnail( ThumbnailOptions options )
	{
		if ( !IsValid || IsError ) return null;

		var bitmap = new Bitmap( options.Width, options.Height );
		try
		{
			SceneUtility.RenderModelBitmap( this, bitmap );
			return bitmap;
		}
		catch
		{
			bitmap.Dispose();
			throw;
		}
	}
}
