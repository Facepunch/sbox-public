using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace Sandbox;

public partial class Package
{
	/// <summary>
	/// The Steam Workshop item this package's files are installed from, or 0 if they're downloaded from the backend.
	/// </summary>
	public ulong WorkshopId => GetCachedMeta<ulong>( "WorkshopId", 0 );

	/// <summary>
	/// Are this package's files installed from its Steam Workshop item, rather than downloaded from the backend?
	/// Only maps, games and addons - clothing has a workshop item too, but its files are on the backend.
	/// </summary>
	public bool InstallsFromWorkshop => IsRemote && WorkshopId != 0
		&& (string.Equals( TypeName, "map", StringComparison.OrdinalIgnoreCase )
			|| string.Equals( TypeName, "game", StringComparison.OrdinalIgnoreCase )
			|| string.Equals( TypeName, "addon", StringComparison.OrdinalIgnoreCase ));

	/// <summary>
	/// Whether this map uses the session's Workshop item loader.
	/// </summary>
	internal bool IsWorkshopMap => IsRemote && WorkshopId != 0 && string.Equals( TypeName, "map", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// The workshop item this package's files are installed from, or 0 if they aren't, or we can't tell. Search results
	/// don't have the package's meta - for those it's the full package if we've fetched it.
	/// </summary>
	internal ulong WorkshopFilesId
	{
		get
		{
			if ( InstallsFromWorkshop )
				return WorkshopId;

			return TryGetCached( FullIdent, out var full, false ) && full.InstallsFromWorkshop ? full.WorkshopId : 0;
		}
	}

	/// <summary>
	/// Take this package's meta from its workshop item installed in <paramref name="folder"/> - the game's own, as it
	/// was published, rather than the backend's. It keeps its <see cref="WorkshopId"/>, a new item can't know its own.
	/// </summary>
	internal void UseWorkshopMeta( string folder )
	{
		if ( this is not RemotePackage ) return;
		if ( WorkshopItemMetaData.Read( folder )?.Meta is not { Length: > 0 } meta ) return;

		UseWorkshopMetaJson( meta );
	}

	/// <summary>
	/// <see cref="UseWorkshopMeta"/>, given the meta json the item shipped with
	/// </summary>
	internal void UseWorkshopMetaJson( string meta )
	{
		if ( this is not RemotePackage remote || string.IsNullOrEmpty( meta ) ) return;

		JsonObject json;

		try
		{
			json = JsonNode.Parse( meta ) as JsonObject;
		}
		catch ( JsonException e )
		{
			Log.Warning( $"Workshop item {WorkshopId}'s meta for {FullIdent} isn't valid json ({e.Message})" );
			return;
		}

		if ( json is null ) return;

		json["WorkshopId"] = WorkshopId;
		remote.UseMeta( json.ToJsonString() );
	}

	/// <summary>
	/// Mounts a Workshop map after installing any packages its resources depend on.
	/// </summary>
	async Task<BaseFileSystem> MountWorkshopMapAsync()
	{
		var items = WorkshopItems.Current;
		var token = items.LifetimeToken;
		var owner = Networking.IsHost ? ServerPackages.Current : null;
		using var files = await WorkshopFileSystem.OpenAsync( WorkshopId, Title, token );
		UseWorkshopMetaJson( files.Metadata?.Meta );
		var dependencies = new List<string>();
		bool completed = false;

		try
		{
			// Maps can depend on packages with assemblies. Keep those requirements on the package loader.
			foreach ( var dependency in EnumerateInstallDependencies().Distinct( StringComparer.OrdinalIgnoreCase ) )
			{
				token.ThrowIfCancellationRequested();
				if ( await ServerPackages.DownloadAndMount( dependency, token, reloadResources: !Networking.IsClient ) is null )
					return null;
				dependencies.Add( dependency );
			}

			token.ThrowIfCancellationRequested();
			var content = await items.LoadAsync( WorkshopId, Workshop.ItemType.Map, token );
			if ( content is null ) return null;
			RememberForOffline();
			completed = true;
			return content;
		}
		finally
		{
			// A reset already released these requirements; don't remove references from the next game.
			if ( !completed && token == items.LifetimeToken )
			{
				foreach ( var dependency in dependencies )
					owner?.RemoveRequirement( dependency );
			}
		}
	}

	/// <summary>
	/// Releases one mount requirement, including any package dependencies of a Workshop map.
	/// </summary>
	internal void RemoveMountRequirement()
	{
		if ( IsWorkshopMap )
		{
			WorkshopItems.Current?.RemoveRequirement( WorkshopId );
			foreach ( var dependency in EnumerateInstallDependencies().Distinct( StringComparer.OrdinalIgnoreCase ) )
				ServerPackages.Current?.RemoveRequirement( dependency );
		}
		else
		{
			ServerPackages.Current?.RemoveRequirement( this );
		}
	}

	/// <summary>
	/// Uses the shared Workshop mount while retaining the package's metadata and compiled-code setup.
	/// </summary>
	async Task<BaseFileSystem> DownloadWorkshopFiles( CancellationToken token, PackageLoadOptions options )
	{
		var manifest = string.Equals( TypeName, "game", StringComparison.OrdinalIgnoreCase ) ? Revision.DownloadManifestAsync( token ) : Task.CompletedTask;
		var files = await WorkshopFileSystem.OpenAsync( WorkshopId, Title, token, options.Loading, options.SkipAssetDownload, FullIdent );
		try
		{
			await manifest;
			UseWorkshopMetaJson( files.Metadata?.Meta );
			RememberForOffline();
			return files;
		}
		catch
		{
			files.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Downloads Workshop content ahead of use without mounting it.
	/// </summary>
	async Task<bool> PrefetchWorkshopFiles( CancellationToken token, PackageLoadOptions options )
	{
		var manifest = string.Equals( TypeName, "game", StringComparison.OrdinalIgnoreCase ) ? Revision.DownloadManifestAsync( token ) : Task.CompletedTask;
		var folder = await Workshop.InstallContentAsync( WorkshopId, Title, options.Loading, token, prefetch: true, ident: FullIdent );
		await manifest;
		if ( folder is null ) return false;
		UseWorkshopMeta( folder );
		return true;
	}
}
