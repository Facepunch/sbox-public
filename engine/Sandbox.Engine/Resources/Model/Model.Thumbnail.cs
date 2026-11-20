namespace Sandbox;

/// <summary>
/// Model thumbnail rendering.
/// </summary>
public sealed partial class Model
{
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
