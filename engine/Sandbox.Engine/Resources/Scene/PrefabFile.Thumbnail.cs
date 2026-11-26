namespace Sandbox;

public partial class PrefabFile
{
	private static ulong lastThumbnailFrame;

	/// <summary>
	/// Load a cached icon framed by this prefab's thumbnail camera. A preview model supplies geometry
	/// for prefabs whose renderer is filled at spawn time.
	/// </summary>
	public async Task<Texture> LoadThumbnailAsync( string previewModelPath = null )
	{
		if ( RootObject is null ) return null;

		var camera = ThumbnailCamera.Find( GetScene() );
		var framing = camera.IsValid() ? Json.Serialize( new
		{
			View = camera.PrefabView( this ),
			camera.FieldOfView,
			camera.FitToBounds,
			camera.Padding,
			camera.Zoom,
			camera.Focus
		} ) : null;

		var cache = string.IsNullOrEmpty( ResourcePath ) ? null : new ThumbnailCache( ResourcePath, $"prefab/{previewModelPath}/{framing}" );
		if ( cache?.Load() is { } cached ) return cached;

		Model model = null;
		if ( !string.IsNullOrWhiteSpace( previewModelPath ) )
		{
			if ( Mounting.MountUtility.TryParse( previewModelPath, out var ident ) )
			{
				var mount = await Mounting.Directory.Mount( ident );
				if ( mount?.IsMounted != true ) return null;
			}

			model = await Model.LoadAsync( previewModelPath );
			if ( !model.IsValid() || model.IsError ) return null;
		}

		while ( lastThumbnailFrame == Application.FrameCount )
			await GameTask.DelayRealtime( 1 );

		lastThumbnailFrame = Application.FrameCount;
		using var bitmap = RenderThumbnail( new() { Width = 256, Height = 256 }, camera, model );
		cache?.Save( bitmap );
		return bitmap.ToTexture();
	}

	/// <summary>
	/// Render the prefab through its authored thumbnail camera, or frame the whole prefab automatically.
	/// </summary>
	public override Bitmap RenderThumbnail( ThumbnailOptions options )
	{
		if ( RootObject is null ) return null;

		return RenderThumbnail( options, ThumbnailCamera.Find( GetScene() ), null );
	}

	private Bitmap RenderThumbnail( ThumbnailOptions options, ThumbnailCamera camera, Model previewModel )
	{
		var bitmap = new Bitmap( options.Width, options.Height );
		try
		{
			bitmap.Clear( Color.Transparent );
			if ( camera.IsValid() )
			{
				camera.Render( this, bitmap, previewModel );
			}
			else if ( previewModel is not null )
			{
				ThumbnailCamera.Render( this, bitmap, new Transform( Vector3.Zero, Rotation.From( 20, 225, 0 ) ), 25f, true, 0.1f, 1f, Vector2.Zero, previewModel );
			}
			else
			{
				SceneUtility.RenderGameObjectToBitmap( GetScene(), bitmap );
			}

			return bitmap;
		}
		catch
		{
			bitmap.Dispose();
			throw;
		}
	}
}
