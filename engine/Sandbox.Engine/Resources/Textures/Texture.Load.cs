using NativeEngine;
using Sandbox.Engine;
using Sandbox.Mounting;

namespace Sandbox;

public partial class Texture
{
	/// <summary>The material and parameter a texture with no path of its own came from (<see cref="Material.GetTexture"/>).</summary>
	internal readonly record struct MaterialOrigin( string Material, string Parameter );

	/// <summary>Where textures read from materials came from, for the Borealis.Gpu device, which reads them by path.</summary>
	internal static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Texture, object> MaterialOrigins = new();

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
	/// A texture native loaded (takes the strong handle), by its resource name where it has one, so a renderer that reads
	/// textures by path (Borealis.Gpu) can read it: a map native loads for a tool's preview has its probes' textures so.
	/// </summary>
	internal static Texture FromNativeResource( ITexture native )
	{
		if ( native.IsNull ) return null;

		var name = RenderTools.GetTextureResourceName( native );
		if ( string.IsNullOrEmpty( name ) ) return FromNative( native );

		native.DestroyStrongHandle();
		return Load( name, false );
	}

	/// <summary>
	/// A texture something native holds (a material's), knowing the compiled file it was loaded from by that file's path,
	/// as <see cref="Load(string, bool)"/> would: a device that reads textures by path (Borealis.Gpu) can read it.
	/// </summary>
	internal static Texture FromNativeFile( ITexture native )
	{
		var texture = FromNative( native );
		if ( texture is null || !string.IsNullOrEmpty( texture.ResourcePath ) ) return texture;

		var name = RenderTools.GetTextureResourceName( texture.native );
		if ( string.IsNullOrEmpty( name ) ) return texture;
		if ( name.EndsWith( ".vtex_c", StringComparison.OrdinalIgnoreCase ) ) name = name[..^2];
		if ( name.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) ) texture.RegisterWeakResourceId( name, texture.native.GetGuid() );
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

		if ( Sandbox.Mounting.Directory.TryLoad( filepath, out object model ) && model is Texture m )
			return m;

		if ( !TextureLoader.ImageUrl.IsAppropriate( filepath ) && !TextureLoader.ImageDataUri.IsAppropriate( filepath ) )
			filepath = filepath.NormalizeFilename( false );

		if ( filepath.StartsWith( '/' ) )
			filepath = filepath[1..];

		if ( Find( filepath ) is Texture existing )
			return existing;

		var tex = TryToLoad( filesystem, filepath, warnOnMissing );
		if ( tex == null )
			return null;

		if ( !TextureLoader.ImageDataUri.IsAppropriate( filepath ) )
			tex.RegisterWeakResourceId( filepath, tex.native.GetGuid() );

		return tex;
	}


	/// <summary>
	/// Try to load a texture.
	/// </summary>
	public static Texture Load( string path_or_url, bool warnOnMissing = true ) => LoadInternal( GlobalContext.Current.FileMount, path_or_url, warnOnMissing );

	internal static Texture Load( ResourceId id, bool warnOnMissing = true )
	{
		ThreadSafe.AssertIsMainThread();

		if ( id.Guid is Guid guid )
		{
			if ( Game.Resources.TryGet<Texture>( guid, out var resource ) )
				return resource;

			var textureHandle = NativeGlue.Resources.GetTexture( id.Path, guid );
			var t = FromNative( textureHandle );
			t?.RegisterWeakResourceId( id.Path, guid );
			return t;
		}

		return LoadInternal( GlobalContext.Current.FileMount, id.Path, warnOnMissing );
	}

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
		if ( TextureLoader.SvgLoader.IsAppropriate( filepath ) )
		{
			// SVGs can have query parameters appended to them, find the ones
			// that match and reload with the same parameters
			var svgPath = filepath.TrimStart( '/' );
			foreach ( var svgTarget in Game.Resources.FindWeakByPathPrefix<Texture>( svgPath ).ToArray() )
			{
				svgTarget.TryReload( filesystem, svgTarget.ResourcePath );
			}

			return;
		}

		if ( Game.Resources.Get<Texture>( filepath ) is { } existing )
			existing.TryReload( filesystem, existing.ResourcePath );
	}

	internal static Texture TryToLoad( BaseFileSystem filesystem, string filepath, bool warnOnMissing = true )
	{
		if ( Application.IsUnitTest && !Graphics.IsAvailable )
			return null;

		// if no filesystem provided
		filesystem ??= GlobalContext.Current.FileMount;

		// Project menu artwork is available before the game's assets are mounted. Keep the
		// qualified path as the texture's identity, but read the image from that project's folder.
		if ( filepath.StartsWith( "project:" ) )
		{
			var separator = filepath.IndexOf( '/' );
			if ( separator < 0 ) return null;

			var project = Project.FindByIdent( filepath[8..separator] );
			var image = filepath[(separator + 1)..];
			if ( project?.FileSystem is null || image.Contains( ':' ) || image.Split( '/' ).Contains( ".." ) ) return null;

			if ( TextureLoader.SvgLoader.IsAppropriate( image ) )
				return TextureLoader.SvgLoader.Load( project.FileSystem, image, warnOnMissing );

			return TextureLoader.Image.IsAppropriate( image )
				? TextureLoader.Image.Load( project.FileSystem, image, warnOnMissing )
				: null;
		}

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
		// Thumb loader - before the image loader, which goes by extension and would take a
		// thumb: of a .png for a file path
		//
		if ( TextureLoader.ThumbLoader.IsAppropriate( filepath ) )
		{
			return TextureLoader.ThumbLoader.Load( filepath );
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

		//Precache.Add( filename );

		//
		// Try to load from engine, which will worst case give us an error texture
		//
		ThreadSafe.AssertIsMainThread();
		var textureHandle = NativeGlue.Resources.GetTexture( filepath, Guid.Empty );
		return FromNative( textureHandle );
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
		// Capture the context's cancellation token now, before awaiting.
		// GlobalContext.Current is AsyncLocal and flows correctly here
		// (before the first await), but may not be correct after awaiting
		// if the scope was disposed by the caller.
		var ct = GlobalContext.Current.CancellationTokenSource?.Token ?? default;

		var t = LoadInternal( filesystem, filepath, warnOnMissing );
		if ( t == null ) return null;

		while ( !t.IsLoaded )
		{
			await Task.Delay( 10, ct );
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

		if ( !TextureLoader.ImageUrl.IsAppropriate( filepath ) && !TextureLoader.ImageDataUri.IsAppropriate( filepath ) )
			filepath = filepath.NormalizeFilename( false );

		return Game.Resources.Get<Texture>( filepath );
	}
}
