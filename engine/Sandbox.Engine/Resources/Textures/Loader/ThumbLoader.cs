using Sandbox.Mounting;
using System.Threading;

namespace Sandbox.TextureLoader;

/// <summary>
/// Loads runtime resource previews for <c>thumb:</c> texture requests.
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
			var placeholder = Texture.Create( 1, 1 )
				.WithName( "thumb" )
				.WithData( new byte[4] { 0, 0, 0, 0 } )
				.Finish();

			placeholder.IsLoaded = false;

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
				var loader = MountUtility.FindLoader( filename );
				if ( loader is null ) return;

				// check to see if there's a pre-generated thumb in the mount, compiled when the mount was built and named by a hash of its path
				var thumbHash = loader.RelativePath.Replace( '\\', '/' ).ToLowerInvariant().Md5()[..16];
				if ( loader.Mount.ThumbsFileSystem is { } fs && fs.FileExists( $"{thumbHash}.vtex_c" ) )
				{
					var compiled = await Texture.LoadAsync( $"{loader.Mount.Ident}/thumbs/{thumbHash}.vtex", false );
					if ( ct.IsCancellationRequested ) return;

					placeholder.CopyFrom( compiled );
					return;
				}

				if ( loader.Type == ResourceType.PrefabFile )
				{
					if ( await Mounting.Directory.TryLoadAsync( filename ) is not PrefabFile prefab || ct.IsCancellationRequested ) return;
					using var icon = await prefab.LoadThumbnailAsync( prefab.GetMetadata( "PreviewModel" ) );
					if ( icon is not null && !ct.IsCancellationRequested ) placeholder.CopyFrom( icon );
					return;
				}

				// Map previews are baked by mount_generatethumb.
				if ( loader.Type != ResourceType.Model ) return;

				var t = MountUtility.GetPreviewTexture( loader );
				if ( !t.IsValid() ) return;

				placeholder.CopyFrom( t );
				return;
			}

			//
			// if it looks like a package, try to load the thumb from there
			//
			var compiledPath = filename.EndsWith( "_c" ) ? filename : $"{filename}_c";
			if ( filename.Count( x => x == '/' || x == '\\' ) == 0 && filename.Count( '.' ) == 1
				&& !(compiledPath.EndsWith( ".vmdl_c", StringComparison.OrdinalIgnoreCase ) && FileSystem.Mounted.FileExists( compiledPath ))
				&& Package.TryParseIdentParts( filename, out var ident ) )
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

			var imagePath = $"{compiledPath}.t.png";
			if ( FileSystem.Mounted.FileExists( imagePath ) )
			{
				using var bitmap = Bitmap.CreateFromBytes( await FileSystem.Mounted.ReadAllBytesAsync( imagePath ) );
				if ( ct.IsCancellationRequested ) return;

				using var texture = bitmap.ToTexture();
				placeholder.CopyFrom( texture );
				return;
			}

			var cache = new ThumbnailCache( filename );
			using var cached = await cache.LoadAsync();
			if ( ct.IsCancellationRequested ) return;

			if ( cached is not null )
			{
				using var texture = cached.ToTexture();
				placeholder.CopyFrom( texture );
				return;
			}

			using var rendered = await ResourceLibrary.GetThumbnail( filename, 512, 512 );
			if ( rendered is null || ct.IsCancellationRequested ) return;

			using var thumbnail = rendered.Resize( 256, 256 );
			using var generated = thumbnail.ToTexture();
			placeholder.CopyFrom( generated );
			cache.Save( thumbnail );
		}
		catch ( Exception error )
		{
			placeholder.FailLoading( error );
			throw;
		}
		finally
		{
			placeholder.IsLoaded = true;
		}
	}
}
