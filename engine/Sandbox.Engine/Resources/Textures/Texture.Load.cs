using NativeEngine;
using Sandbox.Engine;
using Sandbox.Mounting;

namespace Sandbox;

public partial class Texture
{
	internal static Texture FromNative( ITexture native )
	{
		if ( !native.IsStrongHandleValid() )
			return default;

		if ( native.IsNull )
			return default;

		var instanceId = native.GetBindingPtr().ToInt64();
		if ( NativeResourceCache.TryGetValue<Texture>( instanceId, out var cached ) )
		{
			// If the cached texture was explicitly disposed, evict it and create a fresh wrapper.
			if ( !cached.IsValid )
			{
				NativeResourceCache.Remove( instanceId );
			}
			else
			{
				native.DestroyStrongHandle();
				return cached;
			}
		}

		var texture = new Texture( native );
		NativeResourceCache.Add( instanceId, texture );
		return texture;
	}

	/// <summary>
	/// Try to load a texture from given filesystem, by filename.
	/// </summary>
	[System.Obsolete( "Use Texture.Load or Texture.LoadFromFileSystem" )]
	public static Texture Load( BaseFileSystem filesystem, string filepath, bool warnOnMissing = true )
	{
		return LoadInternal( filesystem, filepath, warnOnMissing );
	}

	/// <summary>
	/// Try to load a texture from given filesystem, by filename.
	/// </summary>
	public static Texture LoadFromFileSystem( string filepath, BaseFileSystem filesystem, bool warnOnMissing = true )
	{
		return LoadInternal( filesystem, filepath, warnOnMissing );
	}

	/// <summary>
	/// All the helpers should flow through this to actually load
	/// </summary>
	static Texture LoadInternal( BaseFileSystem filesystem, string filepath, bool warnOnMissing = true )
	{
		//if ( Host.IsUnitTest ) return null;
		if ( string.IsNullOrWhiteSpace( filepath ) ) return null;

		filepath = filepath.Replace( ".vtex_c", ".vtex" );

		if ( Sandbox.Mounting.Directory.TryLoad( filepath, ResourceType.Texture, out object model ) && model is Texture m )
			return m;

		var normalizedFilename = filepath.NormalizeFilename( false );

		if ( normalizedFilename.StartsWith( '/' ) )
			normalizedFilename = normalizedFilename[1..];

		if ( Find( normalizedFilename ) is Texture existing )
			return existing;

		var tex = TryToLoad( filesystem, normalizedFilename, warnOnMissing );
		if ( tex == null )
			return null;

		return tex;
	}


	/// <summary>
	/// Try to load a texture.
	/// </summary>
	public static Texture Load( string path_or_url, bool warnOnMissing = true ) => LoadInternal( GlobalContext.Current.FileMount, path_or_url, warnOnMissing );

	/// <summary>
	/// Load avatar image of a Steam user (with a certain size if supplied).
	/// </summary>
	/// <param name="steamid">The SteamID of the user to load the avatar of.</param>
	/// <param name="size">The size of the avatar (Can be 32, 64, or 128. Defaults to 64 and rounds input to nearest of the three).</param>
	/// <returns>The avatar texture</returns>
	public static Texture LoadAvatar( long steamid, int size = 64 )
	{
		// Small Avatar (32x32)
		if ( size < 48 )
		{
			return Load( $"avatarsmall:{steamid}", false );
		}
		if ( size < 96 )
		{
			return Load( $"avatar:{steamid}", false );
		}

		return Load( $"avatarbig:{steamid}", false );
	}

	/// <inheritdoc cref="LoadAvatar(long, int)"/>
	public static Texture LoadAvatar( string steamid, int size ) => LoadAvatar( long.Parse( steamid ), size );



	internal static void Hotload( BaseFileSystem filesystem, string filepath )
	{
		var existing = Game.Resources.Get<Texture>( filepath );
		if ( existing is not null )
		{
			existing.TryReload( filesystem, filepath );
		}
		else if ( filepath.StartsWith( "/" ) && TextureLoader.Image.IsAppropriate( filepath ) )
		{
			// Image might have been loaded without '/' so try again without it
			Hotload( filesystem, filepath[1..] );
		}
		else if ( TextureLoader.SvgLoader.IsAppropriate( filepath ) )
		{
			// SVGs can have query parameters appended to them, find the ones
			// that match and reload with the same parameters
			var svgPath = filepath.TrimStart( '/' );
			foreach ( var svgTarget in Game.Resources.FindWeakByPathPrefix<Texture>( svgPath ) )
			{
				svgTarget.TryReload( filesystem, svgTarget.ResourcePath );
			}
		}
	}

	internal static Texture TryToLoad( BaseFileSystem filesystem, string filepath, bool warnOnMissing = true )
	{
		if ( Application.IsUnitTest )
			return null;

		// if no filesystem provided
		filesystem ??= GlobalContext.Current.FileMount;

		//
		// Svg Loader
		//
		if ( TextureLoader.SvgLoader.IsAppropriate( filepath ) )
		{
			return TextureLoader.SvgLoader.Load( filesystem, filepath, warnOnMissing );
		}

		//
		// Video Texture loader
		//
		if ( TextureLoader.VideoTextureLoader.IsAppropriate( filepath ) )
		{
			return TextureLoader.VideoTextureLoader.Load( filesystem, filepath, warnOnMissing );
		}

		//
		// Image Url loader
		//
		if ( TextureLoader.ImageUrl.IsAppropriate( filepath ) )
		{
			return TextureLoader.ImageUrl.Load( filepath, warnOnMissing );
		}

		//
		// Data URI loader
		//
		if ( TextureLoader.ImageDataUri.IsAppropriate( filepath ) )
		{
			return TextureLoader.ImageDataUri.Load( filepath, warnOnMissing );
		}

		//
		// Image loader
		//
		if ( TextureLoader.Image.IsAppropriate( filepath ) )
		{
			return TextureLoader.Image.Load( filesystem, filepath, warnOnMissing );
		}

		//
		// Avatar loader
		//
		if ( TextureLoader.Avatar.IsAppropriate( filepath ) )
		{
			return TextureLoader.Avatar.Load( filepath );
		}

		//
		// Thumb loader
		//
		if ( TextureLoader.ThumbLoader.IsAppropriate( filepath ) )
		{
			return TextureLoader.ThumbLoader.Load( filepath );
		}

		//Precache.Add( filename );

		//
		// Try to load from engine, which will worst case give us an error texture
		//
		ThreadSafe.AssertIsMainThread();
		var textureHandle = NativeGlue.Resources.GetTexture( filepath );
		var t = FromNative( textureHandle );
		t?.RegisterWeakResourceId( filepath );
		return t;
	}

	/// <summary>
	/// Load a texture asynchronously. Will return when the texture is loaded and valid.
	/// This is useful when loading textures from the web.
	/// </summary>
	[Obsolete( "Use LoadAsync or LoadFromFileSystemAsync" )]
	public static Task<Texture> LoadAsync( BaseFileSystem filesystem, string filepath, bool warnOnMissing = true )
	{
		return LoadFromFileSystemAsync( filepath, filesystem, warnOnMissing );
	}

	/// <summary>
	/// Load a texture asynchronously. Will return when the texture is loaded and valid.
	/// This is useful when loading textures from the web, or without any big loading hitches.
	/// </summary>
	public static Task<Texture> LoadAsync( string filepath, bool warnOnMissing = true )
	{
		return LoadFromFileSystemAsync( filepath, GlobalContext.Current.FileMount, warnOnMissing );
	}

	/// <summary>
	/// Load a texture asynchronously. Will return when the texture is loaded and valid.
	/// This is useful when loading textures from the web, or without any big loading hitches.
	/// </summary>
	public static async Task<Texture> LoadFromFileSystemAsync( string filepath, BaseFileSystem filesystem, bool warnOnMissing = true )
	{
		var t = LoadInternal( filesystem, filepath, warnOnMissing );
		if ( t == null ) return null;

		while ( !t.IsLoaded )
		{
			await Task.Delay( 10 );
		}

		return t;
	}

	/// <summary>
	/// Try to get an already loaded texture.
	/// </summary>
	/// <param name="filepath">The filename of the texture.</param>
	/// <returns>The already loaded texture, or null if it was not yet loaded.</returns>
	public static Texture Find( string filepath )
	{
		if ( string.IsNullOrWhiteSpace( filepath ) ) return null;

		filepath = filepath.NormalizeFilename( false );

		return Game.Resources.Get<Texture>( filepath );
	}
}
