using System;
using System.Diagnostics;
using System.Reflection;

namespace Editor;

public class AssetType
{
	internal static Dictionary<int, AssetType> AssetTypeCache = new Dictionary<int, AssetType>();

	TypeDescription _typeDescription;
	AssetTypeAttribute _assetTypeAttribute;

	/// <summary>
	/// All currently registered asset types, including the base types such as models, etc.
	/// </summary>
	public static IReadOnlyCollection<AssetType> All => AssetTypeCache.Values;

	/// <summary>
	/// Model (.vmdl) asset type.
	/// </summary>
	public static AssetType Model { get; protected set; }

	/// <summary>
	/// Animation (.vanim) asset type.
	/// </summary>
	public static AssetType Animation { get; protected set; }

	/// <summary>
	/// Animation Graph (.vanmgrph) asset type.
	/// </summary>
	public static AssetType AnimationGraph { get; protected set; }

	/// <summary>
	/// Texture (.vtex) asset type.
	/// </summary>
	public static AssetType Texture { get; protected set; }

	/// <summary>
	/// Material (.vmat) asset type.
	/// </summary>
	public static AssetType Material { get; protected set; }

	/// <summary>
	/// Sound (.wav, .ogg or .mp3) asset type.
	/// </summary>
	public static AssetType SoundFile { get; protected set; }

	/// <summary>
	/// A sound event
	/// </summary>
	public static AssetType SoundEvent { get; protected set; }

	/// <summary>
	/// A soundscape
	/// </summary>
	public static AssetType Soundscape { get; protected set; }

	/// <summary>
	/// Image source (.png or .jpg) asset type.
	/// </summary>
	public static AssetType ImageFile { get; protected set; }

	/// <summary>
	/// Shader (.shader) asset type.
	/// </summary>
	public static AssetType Shader { get; protected set; }

	/// <summary>
	/// A map (.vmap) asset type.
	/// </summary>
	public static AssetType MapFile { get; protected set; }

	/// <summary>
	/// Name of the asset type for UI purposes.
	/// </summary>
	public string FriendlyName { get; internal set; } = "";

	/// <summary>
	/// Primary file extension for this asset type.
	/// </summary>
	public string FileExtension { get; internal set; } = "";

	/// <summary>
	/// All file extensions for this asset type.
	/// </summary>
	public IReadOnlyList<string> FileExtensions => AllFileExtensions;

	/// <summary>
	/// This asset type is hidden by default from asset browser, etc.
	/// </summary>
	public bool HiddenByDefault { get; internal set; }

	/// <summary>
	/// A simple asset is used by something else. It never exists in the game on its own.
	/// </summary>
	public bool IsSimpleAsset { get; internal set; }

	/// <summary>
	/// This asset type can have dependencies
	/// </summary>
	public bool HasDependencies { get; internal set; }

	/// <summary>
	/// Use asset type icon, over any preview image.
	/// </summary>
	public bool PrefersIconThumbnail { get; internal set; }

	/// <summary>
	/// 16x16 icon for this asset type.
	/// </summary>
	public Pixmap Icon16 { get; internal set; }

	/// <summary>
	/// 64x64 icon for this asset type.
	/// </summary>
	public Pixmap Icon64 { get; internal set; }

	/// <summary>
	/// 128x128 icon for this asset type.
	/// </summary>
	public Pixmap Icon128 { get; internal set; }

	/// <summary>
	/// 256x256 icon for this asset type.
	/// </summary>
	public Pixmap Icon256 { get; internal set; }

	/// <summary>
	/// Whether this asset type is a custom game resource or not.
	/// </summary>
	public bool IsGameResource { get; internal set; }

	/// <summary>
	/// Type that will be returned by <see cref="Asset.LoadResource()"/>.
	/// </summary>
	public Type ResourceType { get; internal set; }

	/// <summary>
	/// Category of this asset type, for grouping in UI.
	/// </summary>
	public string Category { get; internal set; } = "Other";

	/// <summary>
	/// Color that represents this asset, for use in the asset browser.
	/// </summary>
	public Color Color { get; internal set; } = Color.Magenta;

	/// <summary>
	/// Flags for this asset type
	/// </summary>
	public AssetTypeFlags Flags => _assetTypeAttribute?.Flags ?? default;

	public override string ToString() => FriendlyName;

	internal string IconPathSmall { get; set; }
	internal string IconPathLarge { get; set; }

	internal List<string> AllFileExtensions { get; set; } = new();

	private void GenerateGlyphs( AssetTypeAttribute gr )
	{
		var temp = Activator.CreateInstance( gr.TargetType ) as Resource;
		if ( temp is not null )
		{
			Icon256 = Pixmap.FromBitmap( temp.GetAssetTypeIcon( 256, 256 ) );
			Icon128 = Pixmap.FromBitmap( temp.GetAssetTypeIcon( 128, 128 ) );
			Icon64 = Pixmap.FromBitmap( temp.GetAssetTypeIcon( 64, 64 ) );
			Icon16 = Pixmap.FromBitmap( temp.GetAssetTypeIcon( 16, 16 ) );
			return;
		}

		var missing_svg = "<svg viewBox=\"0 0 16 16\" xmlns=\"http://www.w3.org/2000/svg\" fill=\"#000000\"><g id=\"SVGRepo_bgCarrier\" stroke-width=\"0\"></g><g id=\"SVGRepo_tracerCarrier\" stroke-linecap=\"round\" stroke-linejoin=\"round\"></g><g id=\"SVGRepo_iconCarrier\"> <g fill=\"#66b8ff\"> <path d=\"m 4 0 c -2 0 -2 2 -2 2 v 12 c 0 2 2 2 2 2 h 8 s 2 0 2 -2 v -11 l -3 -3 z m 3.753906 3 c 0.019532 0 0.039063 0.011719 0.058594 0.011719 c 2.160156 -0.089844 4.0625 1.5625 4.183594 3.746093 l 0.003906 0.027344 v 0.027344 c 0 0.046875 -0.003906 0.085938 -0.007812 0.132812 c 0.003906 0.011719 0.007812 0.054688 0.007812 0.054688 l -0.003906 1 h -0.121094 c -0.074219 0.328125 -0.1875 0.636719 -0.355469 0.917969 c -0.015625 0.03125 -0.039062 0.054687 -0.054687 0.082031 h 0.53125 v 2 h 0.003906 v 2 c 0 1 -1 1 -1 1 l -1.003906 -0.003906 v -0.984375 c -0.007813 1.089843 -0.910156 1.988281 -1.996094 1.988281 c -1.09375 0 -2 -0.90625 -2 -2 c 0 -0.382812 0.132812 -0.710938 0.335938 -1 h -0.335938 v -1 h -2.003906 v -2 h 2.003906 v -1 h -0.003906 v 0.007812 h -2 l 0.003906 -1.007812 s 0.003906 -0.054688 0.007812 -0.09375 c -0.03125 -2.109375 1.671876 -3.789062 3.746094 -3.902344 z m 4.242188 8 h -1.996094 v 0.003906 h 1.996094 z m -3.332032 -5.867188 c 0.71875 0.261719 1.273438 0.929688 1.324219 1.730469 c 0 -0.003906 0.003907 -0.003906 0.003907 -0.011719 c -0.054688 -0.796874 -0.601563 -1.457031 -1.328126 -1.722656 z m 1 6.867188 c 0.199219 0.285156 0.332032 0.613281 0.332032 0.988281 v -0.988281 z m -5.667968 0.003906 h 2 v 2 l -0.996094 -0.003906 c -1 0 -1 -1 -1 -1 z m 0 0\" fill-opacity=\"0.34902\"></path> <path d=\"m 8.152344 4.007812 c -0.4375 -0.023437 -0.882813 0.046876 -1.300782 0.222657 c -1.117187 0.460937 -1.851562 1.558593 -1.851562 2.769531 h 2 c 0 -0.40625 0.242188 -0.769531 0.617188 -0.921875 c 0.375 -0.15625 0.800781 -0.074219 1.089843 0.214844 c 0.289063 0.289062 0.371094 0.714843 0.214844 1.089843 s -0.515625 0.617188 -0.921875 0.617188 c -0.550781 0 -1 0.449219 -1 1 v 2 h 2 v -1.179688 c 0.785156 -0.28125 1.441406 -0.875 1.769531 -1.671874 c 0.464844 -1.117188 0.207031 -2.414063 -0.648437 -3.269532 c -0.535156 -0.535156 -1.242188 -0.835937 -1.96875 -0.871094 z m -0.152344 7.992188 c -0.550781 0 -1 0.449219 -1 1 s 0.449219 1 1 1 s 1 -0.449219 1 -1 s -0.449219 -1 -1 -1 z m 0 0\"></path> </g> </g></svg>";
		var bitmap = Bitmap.CreateFromSvgString( missing_svg, 256, 256 );

		Icon256 = Pixmap.FromBitmap( bitmap );
		Icon128 = Pixmap.FromBitmap( bitmap.Resize( 128, 128 ) );
		Icon64 = Pixmap.FromBitmap( bitmap.Resize( 64, 64 ) );
		Icon16 = Pixmap.FromBitmap( bitmap.Resize( 16, 16 ) );
	}

	internal void Init()
	{
		if ( FriendlyName == "Model" )
		{
			Model = this;
			ResourceType = typeof( Model );
		}

		if ( FriendlyName == "Material" )
		{
			Material = this;
			ResourceType = typeof( Material );
		}

		if ( FileExtension == "sound" )
		{
			SoundEvent = this;
			ResourceType = typeof( SoundEvent );
		}

		if ( FileExtension == "sndscape" )
		{
			Soundscape = this;
			ResourceType = typeof( Soundscape );
		}

		if ( FileExtension == "vmap" )
		{
			MapFile = this;
		}

		if ( FileExtension == "shader" )
		{
			Shader = this;
			ResourceType = typeof( Shader );
		}

		if ( FriendlyName == "Texture" )
		{
			Texture = this;
			ResourceType = typeof( Texture );
		}

		if ( FriendlyName == "Animation" ) Animation = this;
		if ( FriendlyName == "Animation Graph" )
		{
			AnimationGraph = this;
			ResourceType = typeof( AnimationGraph );
		}

		if ( FriendlyName == "Sound File" )
		{
			SoundFile = this;
			ResourceType = typeof( SoundFile );
		}

		if ( FriendlyName == "Image" )
		{
			ImageFile = this;
			ResourceType = typeof( Texture );
		}

		IconPathLarge ??= "assettypes/dmx_lg.png";
		IconPathSmall ??= "assettypes/dmx_sm.png";
		Icon256 = Pixmap.FromFile( IconPathLarge ).Resize( 256, 256 );
		Icon16 = Pixmap.FromFile( IconPathSmall ).Resize( 16, 16 );
		Icon128 = Icon256.Resize( 128, 128 );
		Icon64 = Icon128.Resize( 64, 64 );
	}

	private void Init( TypeDescription type, AssetTypeAttribute attribute )
	{
		IsGameResource = true;

		_typeDescription = type;
		_assetTypeAttribute = attribute;

		ResourceType = type.TargetType;

		Category = attribute.Category;
		FriendlyName = attribute.Name;
		FileExtension = attribute.Extension;

		GenerateGlyphs( attribute );

		// For game resources, use the background color specified in the attribute
		Color = "#67ac5c";
	}

	/// <summary>
	/// Return true if this extension matches
	/// </summary>
	bool HasExtension( string extension )
	{
		if ( extension.StartsWith( '.' ) )
			extension = extension.Trim( '.' );

		if ( extension.EndsWith( "_c" ) )
			extension = extension.Substring( 0, extension.Length - 2 );

		return string.Equals( extension, FileExtension, StringComparison.InvariantCultureIgnoreCase );
	}

	internal bool CouldBeIdentifiedAs( string name, bool fuzzy = false )
	{
		if ( string.Equals( FriendlyName, name, System.StringComparison.OrdinalIgnoreCase ) ) return true;
		if ( string.Equals( FileExtension, name, System.StringComparison.OrdinalIgnoreCase ) ) return true;

		if ( fuzzy )
		{
			if ( FriendlyName.Contains( name, System.StringComparison.OrdinalIgnoreCase ) ) return true;
			if ( FileExtension.Contains( name, System.StringComparison.OrdinalIgnoreCase ) ) return true;
		}

		return false;
	}

	/// <summary>
	/// Find an asset type by name or extension match.
	/// </summary>
	/// <param name="name">Name or extension of an asset type to search for.</param>
	/// <param name="allowPartials">Whether partial matches for the name are allowed.</param>
	public static AssetType Find( string name, bool allowPartials = false )
	{
		// find exact first
		var v = AssetTypeCache.Values.FirstOrDefault( x => x.CouldBeIdentifiedAs( name ) );
		if ( v != null ) return v;
		if ( !allowPartials ) return null;

		var topFriendlyName = AssetTypeCache.Values.Where( x => x.FriendlyName.Contains( name, System.StringComparison.OrdinalIgnoreCase ) )
								.OrderBy( x => x.FriendlyName.Length + (x.HiddenByDefault ? 30 : 0) )
								.FirstOrDefault();

		if ( topFriendlyName != null )
			return topFriendlyName;

		var topExtension = AssetTypeCache.Values
								.Where( x => x.FileExtension.Contains( name, System.StringComparison.OrdinalIgnoreCase ) )
								.OrderBy( x => x.FileExtension.Length + (x.HiddenByDefault ? 30 : 0) )
								.FirstOrDefault();
		return topExtension;
	}

	/// <summary>
	/// Called to insert the asset types for types defined in Engine/Game
	/// </summary>
	internal static bool UpdateCustomTypes()
	{
		bool bChanged = false;
		foreach ( var t in EditorTypeLibrary.GetTypesWithAttribute<AssetTypeAttribute>() )
		{
			bChanged = UpdateType( t.Type, t.Attribute ) || bChanged;
		}

		return bChanged;
	}

	private static bool UpdateType( TypeDescription type, AssetTypeAttribute attribute )
	{
		var old = AssetTypeCache.Values.FirstOrDefault( x => x.FileExtension == attribute.Extension );

		bool created = false;

		if ( old is null )
		{
			created = true;

			// create it in the engine
			IAssetSystem.UpdateGameResourceType( attribute.Name, attribute.Extension );

			old = AssetTypeCache.Values.FirstOrDefault( x => x.FileExtension == attribute.Extension );

			// should exist now we registered. This engine authoritive shit should die in a fire.
			Assert.NotNull( old );

			if ( old is null )
				return false;
		}

		old.Init( type, attribute );

		return created;
	}

	internal static void ImportCustomTypeFiles()
	{
		var sw = Stopwatch.StartNew();

		foreach ( var file in FileSystem.Content.FindFile( "/", "*", true ) )
		{
			var ext = System.IO.Path.GetExtension( file );
			var t = FromExtension( ext );
			if ( t is null ) continue;
			if ( !t.IsGameResource ) continue;

			AssetSystem.RegisterFile( FileSystem.Content.GetFullPath( file ) );
		}

		if ( sw.Elapsed.TotalSeconds > 1 )
		{
			Log.Warning( $"ImportCustomTypeFiles took {sw.Elapsed.TotalSeconds:0.00} seconds" );
		}
	}

	/// <summary>
	/// For a type (ie Texture, Material, Surface) return the appropriate AssetType.
	/// Returns null if can't resolve.
	/// </summary>
	public static AssetType FromType( System.Type t )
	{
		if ( t == typeof( Texture ) ) return AssetType.Texture;
		if ( t == typeof( Material ) ) return AssetType.Material;
		if ( t == typeof( Model ) ) return AssetType.Model;
		if ( t == typeof( SoundFile ) ) return AssetType.SoundFile;
		if ( t == typeof( AnimationGraph ) ) return AssetType.AnimationGraph;
		if ( t == typeof( Shader ) ) return AssetType.Shader;

		foreach ( var a in t.GetCustomAttributes<AssetTypeAttribute>() )
		{
			var at = Find( a.Extension, false );
			if ( at != null ) return at;
		}

		return null;
	}

	static Dictionary<string, AssetType> _extensionCache = new();

	public static AssetType FromExtension( string extension )
	{
		if ( _extensionCache.TryGetValue( extension, out var t ) )
			return t;

		t = AssetTypeCache.Where( x => x.Value.HasExtension( extension ) ).Select( x => x.Value ).FirstOrDefault();

		// Don't cache null, the asset type might appear later
		if ( t is not null )
		{
			_extensionCache[extension] = t;
		}

		return t;
	}

	/// <summary>
	/// Tries its hardest to resolve an asset type from a file path
	/// </summary>
	internal static AssetType ResolveFromPath( string path )
	{
		var extension = System.IO.Path.GetExtension( path ).Trim( '.' );
		if ( extension.EndsWith( "_c" ) ) extension = extension.Substring( 0, extension.Length - 2 );

		return FromExtension( extension );
	}

	/// <summary>
	/// Returns true if there is an editor available for this asset type.
	/// </summary>
	public bool HasEditor
	{
		get
		{
			return EditorTypeLibrary.GetTypesWithAttribute<EditorForAssetTypeAttribute>()
					.Where( x => HasExtension( x.Attribute.Extension ) )
					.Any();
		}
	}
}
