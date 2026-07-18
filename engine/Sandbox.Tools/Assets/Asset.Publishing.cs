using Sandbox.DataModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Editor;

public partial class Asset
{
	PublishSettings _publishConfig;

	/// <summary>
	/// Access the asset publisher config.
	/// </summary>
	public PublishSettings Publishing => GetPublishSettings( true );

	/// <summary>
	/// Fetches and caches ProjectSettings, optionally sets one up, prefer using <seealso cref="Publishing"/>
	/// </summary>
	internal PublishSettings GetPublishSettings( bool createNew )
	{
		if ( _publishConfig is not null )
			return _publishConfig;

		var settings = MetaData?.Get<PublishSettings>( "publish" );
		if ( createNew ) settings ??= new PublishSettings();

		if ( settings is not null )
		{
			settings.InitializeInternal( this );
			_publishConfig = settings;
		}

		return settings;
	}

	/// <summary>
	/// This is data that is saved in an asset's meta file under "publish" to configure
	/// its project for uploading. 
	/// </summary>
	public class PublishSettings
	{
		[JsonIgnore]
		internal Asset asset;

		/// <summary>
		/// Whether the asset should be published or not.
		/// </summary>
		[Browsable( false )]
		public bool Enabled { get; set; }

		/// <summary>
		/// Project configuration information
		/// </summary>
		[Browsable( false )]
		public Sandbox.DataModel.ProjectConfig ProjectConfig { get; set; }

		internal void InitializeInternal( Asset asset )
		{
			this.asset = asset;

			if ( ProjectConfig is null )
			{
				ProjectConfig = new ProjectConfig();
				ProjectConfig.IncludeSourceFiles = false;
			}

			ProjectConfig ??= new ProjectConfig();
			ProjectConfig.Title ??= FixTitle( asset.Name );
			ProjectConfig.Ident ??= FixIdentName( asset.Name );
			ProjectConfig.Org ??= "local";

			// A previously-stored ident might be invalid - created by older code that kept accented
			// characters, or too long, or hand-edited. An invalid ident can never have been published (the
			// backend rejects it, so there's no live package to orphan), which makes it safe to regenerate
			// here and stop a publish - especially the no-UI batch publish - from shipping something broken.
			if ( !IsValidIdent( ProjectConfig.Ident ) )
				ProjectConfig.Ident = FixIdentName( asset.Name );

			// Titles aren't unique or immutable, so there's nothing to orphan - an over-long one (e.g. from
			// older code that didn't truncate) just gets trimmed to fit, keeping the user's text rather than
			// erroring or regenerating it from the name.
			ProjectConfig.Title = TruncateTitle( ProjectConfig.Title );
			ProjectConfig.Type = PackageType();
			ProjectConfig.SetMeta( "SingleAssetSource", asset.RelativePath );

			var project = Project.Current;

			//
			// If we're publishing in a game project, then set the parent package as this game
			//
			if ( project.Config.Type == "game" )
			{
				var isParent = !project.Config.FullIdent.StartsWith( "local." ) // not a local package
					&& !string.Equals( project.Config.FullIdent, ProjectConfig.FullIdent ); // not the same ident

				ProjectConfig.SetMeta( "ParentPackage", isParent ? project.Config.FullIdent : null );
			}

			//
			// If we're publishing in an addon project, set the parent package as the addon's target
			//
			if ( project.Config.Type == "addon" )
			{
				ProjectConfig.SetMeta( "ParentPackage", project.Config.GetMetaOrDefault( "ParentPackage", "" ) );
			}
		}

		/// <summary>
		/// Maximum length of a package ident. The publish backend rejects idents of 64 or more characters,
		/// so we cap generation one below that.
		/// </summary>
		const int MaxIdentLength = 63;

		/// <summary>
		/// Maximum length of a package title accepted by the publish backend.
		/// </summary>
		const int MaxTitleLength = 64;

		/// <summary>
		/// Turn an asset name into a valid package ident. Separators are collapsed to single underscores
		/// (the ident format allows them) instead of being stripped, so the words stay readable. Only when
		/// the result is over the maximum do we trim it down: whole trailing words are dropped to make room
		/// and a short hash of the asset's relative path is appended. The hash keeps over-long idents unique
		/// instead of silently colliding once their distinguishing tail gets truncated away.
		/// </summary>
		string FixIdentName( string name )
		{
			var clean = CleanIdent( name );

			// Valid length already - keep the full, readable name as-is.
			if ( clean.Length is >= 2 and <= MaxIdentLength )
				return clean;

			// Either over the limit, or so short it wouldn't be a valid ident (e.g. an all-symbol name).
			// Either way we lean on a hash of the relative path: it disambiguates long names that share a
			// prefix, and gives us a valid fallback when there's no usable readable stem.
			var hash = HashSuffix( asset.RelativePath );

			if ( clean.Length < 2 )
				return hash;

			// Over the limit. Reserve room for "_" + hash and trim to whole words.
			var stem = TrimToWords( clean, MaxIdentLength - hash.Length - 1 );
			if ( stem.Length < 2 )
				return hash;

			return $"{stem}_{hash}";
		}

		/// <summary>
		/// Whether the ident would pass the validation on <see cref="Sandbox.DataModel.ProjectConfig.Ident"/>
		/// (length 2-64, lower-case letters/digits/underscore/hyphen only). Kept in sync with that attribute set.
		/// </summary>
		static bool IsValidIdent( string ident )
		{
			if ( ident is null || ident.Length < 2 || ident.Length > MaxIdentLength )
				return false;

			foreach ( var c in ident )
			{
				if ( c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' )
					continue;

				return false;
			}

			return true;
		}

		/// <summary>
		/// Turn an asset name into a readable title clamped to the title length limit.
		/// </summary>
		static string FixTitle( string name )
		{
			return TruncateTitle( name.ToTitleCase() );
		}

		/// <summary>
		/// Clean a title so it passes publishing validation: strip the line breaks and tabs the backend
		/// rejects, trim leading/trailing whitespace, and clamp to the length limit on a word boundary where
		/// possible. Titles don't need to be unique, so over-long ones are just trimmed to fit - no hash.
		/// </summary>
		static string TruncateTitle( string title )
		{
			if ( string.IsNullOrEmpty( title ) )
				return title;

			// The backend disallows line breaks/tabs and leading/trailing whitespace.
			title = title.Replace( '\r', ' ' ).Replace( '\n', ' ' ).Replace( '\t', ' ' ).Trim();

			if ( title.Length <= MaxTitleLength )
				return title;

			var cut = title.LastIndexOf( ' ', MaxTitleLength );
			if ( cut <= 0 )
				return title.Substring( 0, MaxTitleLength ).TrimEnd();

			return title.Substring( 0, cut ).TrimEnd();
		}

		/// <summary>
		/// Lowercase the name, fold accented characters down to plain ASCII (é -> e, õ -> o), and collapse
		/// every run of non-alphanumeric characters into a single underscore, then trim leading/trailing
		/// underscores. Keeps the word boundaries the old strip-everything approach threw away, while
		/// guaranteeing the result stays within the ident's allowed character set (a-z, 0-9, _).
		/// </summary>
		static string CleanIdent( string name )
		{
			if ( string.IsNullOrEmpty( name ) )
				return "";

			// Decompose accents (é -> 'e' + combining mark) so we can keep the base letter and drop the mark.
			var normalized = name.Normalize( NormalizationForm.FormD );

			var builder = new StringBuilder( normalized.Length );
			var lastWasSeparator = true; // start true so we never lead with an underscore

			foreach ( var c in normalized )
			{
				// Skip the combining marks left over from decomposition - they're the accents themselves,
				// and must not count as word separators or we'd split mid-word.
				if ( CharUnicodeInfo.GetUnicodeCategory( c ) == UnicodeCategory.NonSpacingMark )
					continue;

				// Only plain ASCII letters/digits survive - anything else (incl. non-decomposable letters
				// like ø or CJK) becomes a separator.
				if ( char.IsAsciiLetterOrDigit( c ) )
				{
					builder.Append( char.ToLowerInvariant( c ) );
					lastWasSeparator = false;
				}
				else if ( !lastWasSeparator )
				{
					builder.Append( '_' );
					lastWasSeparator = true;
				}
			}

			return builder.ToString().Trim( '_' );
		}

		/// <summary>
		/// Trim a cleaned ident to at most <paramref name="max"/> characters, preferring to drop whole trailing
		/// underscore-separated words. Falls back to a hard cut if the first word alone is already too long.
		/// </summary>
		static string TrimToWords( string clean, int max )
		{
			if ( clean.Length <= max )
				return clean;

			var cut = clean.LastIndexOf( '_', max );

			// No earlier word boundary to break on - hard cut.
			if ( cut <= 0 )
				return clean.Substring( 0, max ).Trim( '_' );

			return clean.Substring( 0, cut );
		}

		/// <summary>
		/// A short, stable, lowercase-alphanumeric hash of the given string, used to disambiguate truncated
		/// idents. Base36 keeps it dense (5 chars is ~60M values) and within the ident's allowed character set.
		/// </summary>
		static string HashSuffix( string source )
		{
			const ulong base36Pow5 = 60466176; // 36^5
			return ToBase36( (source ?? "").FastHash64() % base36Pow5, 5 );
		}

		/// <summary>
		/// Encode a value as a fixed-width, zero-padded base36 (0-9a-z) string.
		/// </summary>
		static string ToBase36( ulong value, int width )
		{
			const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

			var chars = new char[width];
			for ( var i = width - 1; i >= 0; i-- )
			{
				chars[i] = alphabet[(int)(value % 36)];
				value /= 36;
			}

			return new string( chars );
		}

		string PackageType()
		{
			if ( asset.AssetType is null ) return default;

			return asset.AssetType.FileExtension switch
			{
				"vmdl" => "model",
				"vmat" => "material",
				"sound" => "sound",
				"vmap" => "map",
				"scene" => "map",

				_ => asset.AssetType.FileExtension
			};
		}

		public void Save()
		{
			asset.MetaData.Set( "publish", this );
		}

		/// <summary>
		/// Create a Project usually with the intention of editing and publishing a single asset.
		/// The project isn't stored or listed anywhere, so is considered a transient that you can load
		/// up, edit, save and then throw away.
		/// </summary>
		public Project CreateTemporaryProject()
		{
			var lp = new Project();
			lp.ProjectSourceObject = asset;
			lp.IsTransient = true;
			lp.OnSaveProject = () => asset.Publishing.Save();
			lp.Config = asset.Publishing.ProjectConfig;

			return lp;
		}

		/// <summary>
		/// Ask the resource how it wants to be published (eg. whether its code should be bundled).
		/// </summary>
		public ResourcePublishContext BuildPublishContext()
		{
			var context = new ResourcePublishContext();

			var resource = asset.LoadResource();
			if ( resource is null )
				return context;

			resource.ConfigurePublishing( context );
			return context;
		}
	}

}
