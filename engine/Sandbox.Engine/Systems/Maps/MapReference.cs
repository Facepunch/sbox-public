using System.IO;
using System.Threading;
using Sandbox.Mounting;

namespace Sandbox;

/// <summary>
/// A map selected by a game, scene component or menu. A registered map project supplies its Workshop item's files.
/// </summary>
internal sealed class MapReference
{
	internal string Text { get; private set; }
	internal ulong WorkshopId { get; private set; }
	internal Package LocalProject { get; }
	internal string ShortName { get; private set; }
	internal string ServerTag => Text?.Contains( ':' ) == false ? Text : string.Empty;

	// A mounted scene opens through the game's startup scene, so it has no launch asset.
	internal string Asset { get; private set; }
	internal string Title { get; private set; }

	readonly string mountIdent;
	readonly string lookupIdent;
	Action release;

	internal MapReference( string text )
	{
		Text = text;

		if ( ulong.TryParse( text, out var id ) )
		{
			WorkshopId = id;
			if ( id != 0 ) LocalProject = Project.All.FirstOrDefault( x => IsMap( x ) && x.Package.WorkshopId == id )?.Package;
		}
		else if ( MountUtility.IsMountPath( text ) )
		{
			if ( MountUtility.TryParse( text, out mountIdent ) ) ShortName = Path.GetFileNameWithoutExtension( text );
		}
		else if ( IsAssetPath( text ) )
		{
			Asset = text;
			ShortName = Path.GetFileNameWithoutExtension( text );
		}
		else if ( Package.TryParseIdentParts( text, out var ident ) )
		{
			ShortName = ident.package;
			var project = Project.FindByIdent( ident.org, ident.package );
			if ( project is null ) lookupIdent = Package.FormatIdent( ident.org, ident.package );
			if ( IsMap( project ) ) LocalProject = project.Package;
		}
	}

	static bool IsMap( Project project ) => project is { Broken: false, Config.Type: "map" };

	/// <summary>
	/// Resolve an identifier to the Workshop ID used in saves and handshakes. Local projects take precedence.
	/// </summary>
	internal static async Task<MapReference> ResolveAsync( string text, Func<string, Task<ulong>> lookup = null )
	{
		var map = new MapReference( text );
		if ( map.lookupIdent is { } ident )
		{
			var id = await (lookup ?? Workshop.LookupAsync)( ident );
			if ( id != 0 ) return new MapReference( id.ToString() );
		}
		else if ( map.WorkshopId == 0 && map.LocalProject is { WorkshopId: > 0 } project )
		{
			map.WorkshopId = project.WorkshopId;
			map.Text = map.WorkshopId.ToString();
			map.ShortName = null;
		}

		return map;
	}

	/// <summary>
	/// Prepare the launch map. Each peer installs its local project. Joining clients get other mounts from the host.
	/// </summary>
	internal async Task<bool> LaunchAsync( bool joining, BaseFileSystem gameFiles, CancellationToken token )
	{
		if ( LocalProject is { } project )
		{
			var installed = await PackageManager.InstallAsync( new PackageLoadOptions( project.FullIdent, "game", token ) { Package = project } );
			if ( installed is null ) return false;

			FileSystem.Mounted.Mount( installed.FileSystem );
			Asset = project.PrimaryAsset;
			Title = project.Title;
			return true;
		}

		if ( Asset is not null ) return joining || Contains( gameFiles, Asset );
		if ( joining ) return WorkshopId != 0 || mountIdent is not null;

		var opened = await OpenAsync( token );
		if ( opened.Asset is null ) return false;

		release = opened.Release;
		if ( mountIdent is null ) Asset = opened.Asset;
		return true;
	}

	internal void Release()
	{
		release?.Invoke();
		release = null;
	}

	/// <summary>
	/// Get the asset a component loads and the requirement it releases on unload.
	/// </summary>
	internal async Task<(string Asset, Action Release)> OpenAsync( CancellationToken token )
	{
		if ( LocalProject is { } project )
		{
			var asset = project.PrimaryAsset ?? Text;
			if ( Application.Map is { Asset: not null } launch && launch.LocalProject == project ) return (asset, null);

			var owner = Networking.IsHost ? ServerPackages.Current : null;
			var ident = project.GetVersionedIdent();
			if ( await ServerPackages.DownloadAndMount( ident, token ) is null ) return default;

			return (asset, owner is null ? null : () => owner.RemoveRequirement( ident ));
		}

		if ( WorkshopId != 0 )
		{
			var items = WorkshopItems.Current;
			var opened = await items.OpenMapAsync( WorkshopId, token );
			Title = items.Get( WorkshopId )?.Metadata?.Title;
			return opened;
		}

		if ( mountIdent is not null )
		{
			var mount = await Mounting.Directory.Mount( mountIdent );
			if ( mount?.GetByPath( Text ) is { Type: Mounting.ResourceType.Scene } scene )
			{
				Title = scene.Name;
				return (scene.Path, null);
			}
		}

		if ( Asset is not null ) return (Asset, null);
		Log.Warning( $"Can't load map '{Text}' - use a Workshop item ID, a map identifier, a map asset path or a mounted scene." );
		return default;
	}

	/// <summary>
	/// Get the display name and art without mounting or downloading the map.
	/// </summary>
	internal async Task<(string Title, string Thumb, Texture Thumbnail)> GetInfoAsync()
	{
		if ( LocalProject is { } project ) return (project.Title, project.ThumbWide, null);
		if ( WorkshopId != 0 )
		{
			var item = await Workshop.FetchAsync( WorkshopId );
			return (item?.Title, item?.Thumbnail, null);
		}

		if ( mountIdent is not null )
		{
			var meta = Mounting.Directory.GetMetadata( Text );
			return (meta?.Name, null, meta?.Thumbnail);
		}

		return (Asset is null ? null : ShortName, null, null);
	}

	internal static bool IsAssetPath( string path ) => AssetDownloadCache.IsLegalDownload( path )
		&& !Path.IsPathRooted( path )
		&& Path.GetExtension( path ).ToLowerInvariant() is ".scene" or ".vmap" or ".vpk";

	internal static bool Contains( BaseFileSystem files, string path )
	{
		if ( path.EndsWith( ".scene", StringComparison.OrdinalIgnoreCase ) )
			return files.FileExists( path + "_c" ) || files.FileExists( path );

		return files.FileExists( Path.ChangeExtension( path, ".vpk" ) );
	}
}
