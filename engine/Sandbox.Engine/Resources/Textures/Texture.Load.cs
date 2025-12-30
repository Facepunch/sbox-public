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

		IntPtr targetPointer = native.GetBindingPtr();

		lock ( LoadedByPointer )
		{
			if ( LoadedByPointer.TryGetValue( targetPointer, out var reference ) && reference.TryGetTarget( out var target ) )
			{
				native.DestroyStrongHandle(); // target contains a strong handle already!
				return target;
			}
		}

		return new Texture( native );
	}

	static Dictionary<string, WeakReference<Texture>> Loaded = new();
	static Dictionary<IntPtr, WeakReference<Texture>> LoadedByPointer = new();

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

		if ( Find( normalizedFilename ) is Texture existing )
			return existing;

		var tex = TryToLoad( filesystem, filepath, warnOnMissing );
		if ( tex == null )
			return null;

		tex.SetIdFromResourcePath( normalizedFilename );

		Loaded[normalizedFilename] = new WeakReference<Texture>( tex );

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
		if ( Loaded.TryGetValue( filepath, out var texture ) && texture.TryGetTarget( out var target ) )
		{
			target.TryReload( filesystem, filepath );
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
			var svgTargets = Loaded.Where( x => x.Key.TrimStart( '/' ).StartsWith( svgPath ) );

			foreach ( var kvp in svgTargets )
			{
				if ( kvp.Value.TryGetTarget( out var svgTarget ) )
					svgTarget.TryReload( filesystem, kvp.Key );
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
		return new Texture( textureHandle );
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
		//if ( Host.IsUnitTest ) return null;
		if ( string.IsNullOrWhiteSpace( filepath ) ) return null;

		filepath = filepath.NormalizeFilename( false );

		if ( Loaded.TryGetValue( filepath, out var val ) )
		{
			if ( val.TryGetTarget( out var target ) )
			{
				return target;
			}
		}

		return null;
	}
}
