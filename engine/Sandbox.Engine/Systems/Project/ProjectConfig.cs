using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox.DataModel;

/// <summary>
/// Configuration of a <see cref="Project"/>.
/// </summary>
[Expose]
public class ProjectConfig
{
	/// <summary>
	/// The directory housing this addon (TODO)
	/// </summary>
	[JsonIgnore]
	[Hide]
	public DirectoryInfo Directory { get; set; }

	/// <summary>
	/// The directory housing this addon (TODO)
	/// </summary>
	[JsonIgnore]
	[Hide]
	public DirectoryInfo AssetsDirectory { get; set; }

	/// <summary>
	/// The human readable title, for example "Sandbox", "Counter-Strike"
	/// </summary>
	[Display( GroupName = "Setup", Order = -100, Name = "Title", Description = "The human readable title, for example \"Sandbox\", \"Counter - Strike\"" )]
	[MaxLength( 32 )]
	[MinLength( 3 )]
	public string Title { get; set; }

	/// <summary>
	/// The type of addon. Current valid values are "game"
	/// </summary>
	[Display( GroupName = "Setup", Order = -90, Name = "Addon Type", Description = "Don't change this for christ's sake" )]
	public string Type { get; set; }

	/// <summary>
	/// The ident of the org that owns this addon. For example "facepunch", "valve".
	/// </summary>
	[Display( GroupName = "Setup", Order = -100, Name = "Organization Ident", Description = "The ident of the org that owns this addon. Set to local if you don't have an org or are just testing." )]
	[Editor( "organization" )]
	public string Org { get; set; }

	/// <summary>
	/// The ident of this addon. For example "sandbox", "cs" or "dm98"
	/// </summary>
	[Display( GroupName = "Setup", Order = -100, Name = "Package Ident", Description = "The ident of this addon. A short name with no special characters." )]
	[MaxLength( 64 )]
	[MinLength( 2 )]
	[RegularExpression( @"^[a-z0-9_\-]+$", ErrorMessage = "Lower case letters and underscores, no spaces or other special characters" )]
	public string Ident { get; set; }

	/// <summary>
	/// Type of the package.
	/// </summary>
	[JsonIgnore]
	[Obsolete( "Compare string Type instead" )]
	public Package.Type PackageType => default;

	/// <summary>
	/// Returns a combination of Org and Ident - for example "facepunch.sandbox" or "valve.cs".
	/// </summary>
	[Hide]
	[JsonIgnore]
	public string FullIdent => $"{Org}.{Ident}";

	/// <summary>
	/// The version of the addon file. Allows us to upgrade internally.
	/// </summary>
	[Hide]
	public int Schema { get; set; }

	/// <summary>
	/// If true then we'll include all the source files
	/// </summary>
	[Hide]
	public bool IncludeSourceFiles { get; set; }

	/// <summary>
	/// A list of paths in which to look for extra assets to upload with the addon. Note that compiled asset files are automatically included.
	/// </summary>
	public string Resources { get; set; }

	/// <summary>
	/// A list of packages that this package depends on. These should be installed alongside this package.
	/// </summary>
	public List<string> PackageReferences { get; set; }

	/// <summary>
	/// A list of packages that this package uses but there is no need to install. For example, a map package might use
	/// a model package - but there is no need to download that model package because any usage will organically be included
	/// in the manifest. However, when loading this item in the editor, it'd make sense to install these 'cloud' packages.
	/// </summary>
	public List<string> EditorReferences { get; set; }

	/// <summary>
	/// A list of mounts that are required
	/// </summary>
	public List<string> Mounts { get; set; }

	/// <summary>
	/// Contains unique elements from <see cref="PackageReferences"/>, along with any implicit package references.
	/// An example implicit reference is the parent package of an addon.
	/// </summary>
	[JsonIgnore, Hide]
	internal IReadOnlySet<string> DistinctPackageReferences
	{
		get
		{
			var set = new HashSet<string>( (IList<string>)PackageReferences ?? Array.Empty<string>(),
				StringComparer.OrdinalIgnoreCase );

			return set;
		}
	}

	/// <summary>
	/// Whether or not this project is standalone-only, and supports disabling the whitelist, compiling with /unsafe, etc.
	/// </summary>
	public bool IsStandaloneOnly { get; set; }

	/// <summary>
	/// Custom key-value storage for this project.
	/// </summary>
	[Hide]
	public Dictionary<string, object> Metadata { get; set; } = new();

	public override string ToString() => FullIdent;

	internal void Init( string path )
	{
		path = System.IO.Path.GetDirectoryName( path );

		Directory = new DirectoryInfo( path );
		AssetsDirectory = new DirectoryInfo( System.IO.Path.Combine( path, "Assets" ) );

		Org ??= "local";
		Ident ??= Directory.Name.ToLower();
		PackageReferences ??= new List<string>();

		if ( Type == "map" ) Type = "content";
	}

	internal bool Upgrade()
	{
		if ( Schema == 1 )
			return false;

		//
		// Upgrade from 0 to 1
		//
		if ( Schema < 1 )
		{
			// in version 1 all addons were game addons,
			// with a set file structure
			Type = "game";
			Title = Ident.ToTitleCase();
			Schema = 1;

			Log.Info( $"Upgraded addon {this} schema from 0 > 1" );
		}

		return true;
	}

	/// <summary>
	/// Serialize the entire config to a JSON string.
	/// </summary>
	public string ToJson()
	{
		return Json.SerializeAsObject( this ).ToJsonString( Json.options );
	}

	/// <summary>
	/// Try to get a value at given key in <see cref="Metadata"/>.
	/// </summary>
	/// <typeparam name="T">Type of the value.</typeparam>
	/// <param name="keyname">The key to retrieve the value of.</param>
	/// <param name="outvalue">The value, if it was present in the metadata storage.</param>
	/// <returns>Whether the value was successfully retrieved.</returns>
	public bool TryGetMeta<T>( string keyname, out T outvalue )
	{
		outvalue = default;

		if ( Metadata == null )
			return false;

		if ( !Metadata.TryGetValue( keyname, out var val ) )
			return false;

		if ( val is T t )
		{
			outvalue = t;
			return true;
		}

		if ( val is JsonElement je )
		{
			try
			{
				outvalue = je.Deserialize<T>( Json.options ) ?? default;
			}
			catch ( System.Exception )
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Get the package's meta value. If it's missing or the wrong type then use the default value.
	/// </summary>
	public T GetMetaOrDefault<T>( string keyname, T defaultValue )
	{
		if ( Metadata == null )
			return defaultValue;

		if ( !Metadata.TryGetValue( keyname, out var val ) )
			return defaultValue;

		if ( val is T t )
		{
			return t;
		}

		if ( val is JsonElement je )
		{
			try
			{
				return je.Deserialize<T>( Json.options ) ?? defaultValue;
			}
			catch ( System.Exception )
			{
				return defaultValue;
			}
		}

		return defaultValue;
	}

	/// <summary>
	/// Store custom data at given key in the <see cref="Metadata"/>.
	/// </summary>
	/// <param name="keyname">The key for the data.</param>
	/// <param name="outvalue">The data itself to store.</param>
	/// <returns>Always true.</returns>
	public bool SetMeta( string keyname, object outvalue )
	{
		Metadata ??= new Dictionary<string, object>();

		if ( outvalue is null )
		{
			return Metadata.Remove( keyname );
		}
		else
		{
			Metadata[keyname] = outvalue;
		}


		return true;
	}


	internal Compiler.Configuration GetCompileSettings()
	{
		if ( !TryGetMeta<Compiler.Configuration>( "Compiler", out var compilerSettings ) )
		{
			compilerSettings = new Compiler.Configuration();
		}

		compilerSettings.Clean();

		return compilerSettings;
	}

	internal void SetMountState( string name, bool state )
	{
		Mounts ??= new List<string>();

		if ( state ) Mounts.Add( name );
		else Mounts.Remove( name );

		// Make sure there's no dupes
		Mounts = Mounts.Distinct().ToList();
	}
}
