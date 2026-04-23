using Sandbox.DataModel;
using System.ComponentModel;
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
			ProjectConfig.Title ??= asset.Name.ToTitleCase();
			ProjectConfig.Ident ??= FixIdentName( asset.Name );
			ProjectConfig.Org ??= "local";
			ProjectConfig.Type = PackageType();
			ProjectConfig.SetMeta( "SingleAssetSource", asset.RelativePath );

			// If we're a map then make a note of what we're targetting
			if ( ProjectConfig.Type == "map" )
			{
				var proj = Project.Current;

				//
				// If we're publishing a map in a game project, then set the map's parent package as this game
				//
				if ( proj.Config.Type == "game" )
				{
					bool IsProjectParent = !proj.Config.FullIdent.StartsWith( "local." ) // not a local package
						&& !string.Equals( proj.Config.FullIdent, ProjectConfig.FullIdent ); // not the same ident

					ProjectConfig.SetMeta( "ParentPackage", IsProjectParent ? Project.Current.Config.FullIdent : null );
				}

				//
				// If we're publishing a map in a addon project, set the map's parent package as the addon's target
				//
				if ( proj.Config.Type == "addon" )
				{
					ProjectConfig.SetMeta( "ParentPackage", Project.Current.Config.GetMetaOrDefault( "ParentPackage", "" ) );
				}
			}
		}

		string FixIdentName( string name )
		{
			if ( name is null )
				return "";

			string cleanedName = new string( name.Where( c => char.IsLetterOrDigit( c ) ).ToArray() );
			cleanedName = cleanedName.ToLower();
			cleanedName = cleanedName.Truncate( 63 );
			return cleanedName;
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
	}

}
