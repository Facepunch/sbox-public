using Microsoft.Extensions.Caching.Memory;
using Sandbox.Engine;
using Sandbox.Mounting;

namespace Sandbox.TextureLoader;

/// <summary>
/// Loads a thumbnail of an entity or something
/// </summary>
internal static class ThumbLoader
{
	/// <summary>
	/// Entries are cached on a sliding window, they will be released if not used for 10 minutes
	/// </summary>
	static readonly MemoryCache _cache = new( new MemoryCacheOptions() );

	internal static bool IsAppropriate( string url )
	{
		return url.StartsWith( "thumb:" );
	}

	internal static Texture Load( string filename )
	{
		try
		{
			return _cache.GetOrCreate( filename, entry =>
			{
				entry.SetSlidingExpiration( TimeSpan.FromMinutes( 10 ) );

				var placeholder = Texture.Create( 1, 1 )
					.WithName( "thumb" )
					.WithData( new byte[4] { 0, 0, 0, 0 } )
					.Finish();

				placeholder.IsLoaded = false;
				_ = LoadIntoTexture( filename, placeholder );

				return placeholder;
			} );
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"Couldn't Load Thumb {filename} ({e.Message})" );
			return null;
		}
	}

	internal static async Task LoadIntoTexture( string url, Texture placeholder )
	{
		try
		{
			var filename = url[(url.IndexOf( ':' ) + 1)..];

			// One day we'll support things like ?width=512 and ?mode=wide ?mode=tall


			//
			// if it's from a mount then get it from the mount system
			//
			if ( filename.StartsWith( "mount:" ) )
			{
				var t = MountUtility.GetPreviewTexture( filename );
				placeholder.CopyFrom( t );
				return;
			}

			//
			// if it looks like a package, try to load the thumb from there
			//
			if ( filename.Count( x => x == '/' || x == '\\' ) == 0 && filename.Count( '.' ) == 1 && Package.TryParseIdent( filename, out var ident ) )
			{
				var packageInfo = await Package.FetchAsync( $"{ident.org}.{ident.package}", true );
				if ( packageInfo == null ) return;

				var thumb = await ImageUrl.LoadFromUrl( packageInfo.Thumb );
				if ( thumb == null ) return;

				placeholder.CopyFrom( thumb );
				return;
			}

			//
			// if it's a resource, it can generate itself
			//
			{
				// Load it from disk, if it exists!
				{
					var fn = filename.EndsWith( "_c" ) ? filename : $"{filename}_c"; // needs to end in _c
					string imageFile = $"{fn}.t.png";

					if ( FileSystem.Mounted.FileExists( imageFile ) )
					{
						using var bitmap = Bitmap.CreateFromBytes( await FileSystem.Mounted.ReadAllBytesAsync( imageFile ) );
						placeholder.CopyFrom( bitmap.ToTexture() );
						return;
					}
				}

				{
					var bitmap = IToolsDll.Current?.GetThumbnail( filename );
					if ( bitmap != null )
					{
						using var downscaled = bitmap.Resize( 256, 256, true );
						var t = downscaled.ToTexture();
						placeholder.CopyFrom( t );
						return;
					}
				}

				// last resort - generate it!
				{
					using var bitmap = await ResourceLibrary.GetThumbnail( filename, 512, 512 );
					if ( bitmap != null )
					{
						using var downscaled = bitmap.Resize( 256, 256, true );
						var t = downscaled.ToTexture();
						placeholder.CopyFrom( t );
						return;
					}
				}
			}
		}
		finally
		{
			placeholder.IsLoaded = true;
		}
	}
}
