using Sandbox.Engine;
using Sandbox.Mounting;
using System.Threading;

namespace Sandbox.TextureLoader;

/// <summary>
/// Loads a thumbnail of an entity or something
/// </summary>
internal static class ThumbLoader
{
	internal static bool IsAppropriate( string url )
	{
		return url.StartsWith( "thumb:" );
	}

	internal static Texture Load( string filename )
	{
		try
		{
			if ( Game.Resources.Get<Texture>( $"{filename}.png" ) is { } cached )
				return cached;

			var placeholder = Texture.Create( 1, 1 )
				.WithName( "thumb" )
				.WithData( new byte[4] { 0, 0, 0, 0 } )
				.Finish();

			placeholder.IsLoaded = false;
			placeholder.RegisterWeakResourceId( $"{filename}.png" );

			_ = LoadIntoTexture( filename, placeholder );

			return placeholder;
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"Couldn't Load Thumb {filename} ({e.Message})" );
			return null;
		}
	}

	internal static async Task LoadIntoTexture( string url, Texture placeholder, CancellationToken ct = default )
	{
		try
		{
			var filename = url[(url.IndexOf( ':' ) + 1)..];

			// One day we'll support things like ?width=512 and ?mode=wide ?mode=tall

			if ( ct.IsCancellationRequested ) return;

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
				if ( packageInfo == null || ct.IsCancellationRequested ) return;

				var thumb = await ImageUrl.LoadFromUrl( packageInfo.Thumb, ct );
				if ( thumb == null || ct.IsCancellationRequested )
				{
					thumb?.Dispose();
					return;
				}

				placeholder.CopyFrom( thumb );
				thumb.Dispose();
				return;
			}

			if ( ct.IsCancellationRequested ) return;

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
						if ( ct.IsCancellationRequested ) return;

						using var texture = bitmap.ToTexture();
						placeholder.CopyFrom( texture );
						return;
					}
				}

				if ( ct.IsCancellationRequested ) return;

				{
					var bitmap = IToolsDll.Current?.GetThumbnail( filename );
					if ( bitmap != null )
					{
						using var downscaled = bitmap.Resize( 256, 256, true );
						using var texture = downscaled.ToTexture();
						placeholder.CopyFrom( texture );
						return;
					}
				}

				if ( ct.IsCancellationRequested ) return;

				// last resort - generate it!
				{
					using var bitmap = await ResourceLibrary.GetThumbnail( filename, 512, 512 );
					if ( bitmap != null && !ct.IsCancellationRequested )
					{
						using var downscaled = bitmap.Resize( 256, 256, true );
						using var texture = downscaled.ToTexture();
						placeholder.CopyFrom( texture );
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
