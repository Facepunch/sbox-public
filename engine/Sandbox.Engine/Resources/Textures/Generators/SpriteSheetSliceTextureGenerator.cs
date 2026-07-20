using System.Threading;

namespace Sandbox.Resources;

/// <summary>
/// Produces the texture for one slice of a <see cref="SpriteSheet"/>, by cropping the sheet's source
/// image down to that slice's rect.
/// </summary>
/// <remarks>
/// This is what keeps the sheet the single place a cut is recorded. A sprite built from a sheet holds
/// one of these rather than a baked-out image, so re-slicing reaches everything already using it -
/// the same way <see cref="ImageFileGenerator"/> frames follow edits to the file they came from.
/// </remarks>
[Title( "Sprite Sheet Slice" )]
[Icon( "crop" )]
[ClassName( "spritesheetslice" )]
public sealed class SpriteSheetSliceGenerator : TextureGenerator
{
	/// <summary>
	/// The sheet the slice belongs to.
	/// </summary>
	[KeyProperty]
	public SpriteSheet Sheet { get; set; }

	/// <summary>
	/// Which slice, by its stable id. Names get changed - labelling parts <c>L_Thigh</c> and
	/// <c>R_Calf</c> is a required step of rigging - so the id is what the reference hangs off.
	/// </summary>
	public Guid SliceId { get; set; }

	/// <summary>
	/// The slice's name when this reference was made. Only used to find the slice again if its id
	/// goes missing, and to give the reference something readable to show.
	/// </summary>
	public string SliceName { get; set; }

	[Hide]
	public override bool CacheToDisk => true;

	// Sprites are pixel art as often as not, and block compression would visibly wreck them.
	[Hide]
	public override ImageFormat? FormatOverride => ImageFormat.RGBA8888;

	protected override async ValueTask<Texture> CreateTexture( Options options, CancellationToken ct )
	{
		if ( Sheet is null )
			return null;

		// Id first, name only as a repair path for a reference that lost track of its slice.
		var slice = Sheet.GetSlice( SliceId ) ?? Sheet.GetSlice( SliceName );
		if ( slice is null )
			return Texture.Transparent;

		if ( string.IsNullOrWhiteSpace( Sheet.ImagePath ) )
			return Texture.Transparent;

		//
		// Both the sheet and the image it cuts up are inputs, so a change to either has to be able to
		// bring this back around. Without these the compiler has no idea this texture went stale.
		//
		if ( options.Compiler is not null )
		{
			if ( !string.IsNullOrEmpty( Sheet.ResourcePath ) )
				options.Compiler.Context.AddCompileReference( Sheet.ResourcePath );

			options.Compiler.Context.AddCompileReference( Sheet.ImagePath );
		}

		var path = Sheet.ImagePath.NormalizeFilename();

		if ( !EngineFileSystem.Mounted.FileExists( path ) )
		{
			Log.Warning( $"SpriteSheetSliceGenerator could not find file: {path}" );
			return Texture.Invalid;
		}

		var bytes = await EngineFileSystem.Mounted.ReadAllBytesAsync( path );
		ct.ThrowIfCancellationRequested();

		var bitmap = Bitmap.CreateFromBytes( bytes );
		if ( bitmap is null )
			return Texture.Invalid;

		try
		{
			var rect = ClampToBitmap( slice.Rect, bitmap.Width, bitmap.Height );

			// A slice that ended up outside the image entirely - the source was swapped for a smaller
			// one, most likely. Transparent rather than invalid, so the rest of the sprite still works
			// and the gap is obvious in the editor.
			if ( rect.Width < 1 || rect.Height < 1 )
				return Texture.Transparent;

			// Whole-image slices are common enough (a one-part sheet) to be worth not copying for.
			if ( rect.Width < bitmap.Width || rect.Height < bitmap.Height )
			{
				var cropped = bitmap.Crop( rect.SnapToGrid() );
				bitmap.Dispose();
				bitmap = cropped;
			}

			if ( Sheet.KeyBackground )
			{
				KnockOutBackground( bitmap, Sheet.Background );
			}

			return bitmap?.ToTexture();
		}
		finally
		{
			bitmap?.Dispose();
		}
	}

	/// <summary>
	/// Make the sheet's background colour see-through, so a part cut from a flat white sheet does not
	/// render as a white rectangle.
	/// </summary>
	/// <remarks>
	/// Edges are faded rather than cut hard. Artwork keyed this way is usually a JPEG or a scan, where
	/// the boundary between drawing and background is a gradient several pixels wide - a hard threshold
	/// leaves a bright fringe around every part, which is very obvious once the parts are laid over
	/// each other in a rig.
	/// </remarks>
	static void KnockOutBackground( Bitmap bitmap, SpriteSheet.BackgroundKey key )
	{
		if ( bitmap is null || !key.Enabled ) return;

		var pixels = bitmap.GetPixels32();
		if ( pixels is null || pixels.Length == 0 ) return;

		var output = new Color[pixels.Length];

		// Beyond this the pixel is left alone; between the two it fades.
		var feather = Math.Max( 1, key.Tolerance * 2 );

		for ( int i = 0; i < pixels.Length; i++ )
		{
			var pixel = pixels[i];

			var distance = Math.Max(
				Math.Abs( pixel.r - key.Color.r ),
				Math.Max( Math.Abs( pixel.g - key.Color.g ), Math.Abs( pixel.b - key.Color.b ) ) );

			float alpha = pixel.a / 255f;

			if ( distance <= key.Tolerance )
			{
				alpha = 0f;
			}
			else if ( distance < feather )
			{
				alpha *= (distance - key.Tolerance) / (float)(feather - key.Tolerance);
			}

			output[i] = new Color( pixel.r / 255f, pixel.g / 255f, pixel.b / 255f, alpha );
		}

		bitmap.SetPixels( output );
	}

	static Rect ClampToBitmap( Rect rect, int width, int height )
	{
		var left = Math.Clamp( (int)rect.Left, 0, width );
		var top = Math.Clamp( (int)rect.Top, 0, height );
		var right = Math.Clamp( (int)(rect.Left + rect.Width), 0, width );
		var bottom = Math.Clamp( (int)(rect.Top + rect.Height), 0, height );

		return new Rect( left, top, right - left, bottom - top );
	}
}
