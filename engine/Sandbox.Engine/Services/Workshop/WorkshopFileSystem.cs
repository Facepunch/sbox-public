using Sandbox.Internal;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Sandbox;

/// <summary>
/// A reference to an installed Workshop item's files. Games, maps and network requirements share the same mount.
/// </summary>
internal sealed class WorkshopFileSystem : AggregateFileSystem, IDisposable
{
	sealed class MountedItem
	{
		internal ulong Id;
		internal WorkshopItemMetaData? Metadata;
		internal long Version;
		internal PackageFileSystem Files;
		internal int References;
	}

	static readonly Dictionary<ulong, MountedItem> mounted = new();
	static readonly ConcurrentDictionary<ulong, SemaphoreSlim> locks = new();
	MountedItem item;

	internal WorkshopItemMetaData? Metadata => item.Metadata;
	internal long Version => item.Version;
	internal string Title => item.Metadata?.Title ?? $"Workshop item {item.Id}";

	WorkshopFileSystem( MountedItem item )
	{
		this.item = item;
		item.References++;
		Mount( item.Files );
	}

	/// <summary>
	/// Whether Steam must wait before replacing this item's installed files.
	/// </summary>
	internal static bool IsInUse( ulong id )
	{
		lock ( mounted ) return mounted.ContainsKey( id );
	}

	/// <summary>
	/// Downloads and mounts an item, or acquires the mount already used by another loader.
	/// </summary>
	internal static Task<WorkshopFileSystem> OpenAsync( ulong id, string title, CancellationToken token, ILoadingInterface loading = null, string ident = null )
	{
		return OpenAsync( id, title, () => Workshop.InstallContentAsync( id, title, loading, token, ident: ident ),
			async () => (await Workshop.FetchAsync( id, token ))?.Metadata, token );
	}

	/// <summary>
	/// Opens an already installed item through the same shared mount path. <paramref name="itemMetadata"/> stands in
	/// for the item's Steam metadata.
	/// </summary>
	internal static Task<WorkshopFileSystem> OpenInstalledAsync( ulong id, string folder, CancellationToken token, string itemMetadata = null )
	{
		return OpenAsync( id, $"Workshop item {id}", () => Task.FromResult( folder ), () => Task.FromResult( itemMetadata ), token );
	}

	/// <summary>
	/// The item's metadata comes from its <see cref="WorkshopItemMetaData.FileName"/>, or from its Steam metadata
	/// (<paramref name="itemMetadata"/>, only asked for then) when it has none: maps published before items shipped one.
	/// </summary>
	static async Task<WorkshopFileSystem> OpenAsync( ulong id, string title, Func<Task<string>> install, Func<Task<string>> itemMetadata, CancellationToken token )
	{
		ArgumentOutOfRangeException.ThrowIfZero( id );
		var gate = locks.GetOrAdd( id, _ => new SemaphoreSlim( 1, 1 ) );
		await gate.WaitAsync( token );
		try
		{
			lock ( mounted )
			{
				if ( mounted.TryGetValue( id, out var existing ) ) return new WorkshopFileSystem( existing );
			}

			var folder = await install();
			token.ThrowIfCancellationRequested();
			if ( folder is null ) throw new IOException( $"Couldn't install Workshop item {id}." );

			var metadata = WorkshopItemMetaData.Read( folder ) ?? WorkshopItemMetaData.FromItemMetadata( await itemMetadata() );
			token.ThrowIfCancellationRequested();

			var item = new MountedItem { Id = id, Metadata = metadata, Version = Workshop.GetInstalledVersion( id ), Files = new PackageFileSystem() };
			var redirect = item.Files.Redirect;
			lock ( mounted ) mounted.Add( id, item );

			try
			{
				foreach ( var file in Directory.EnumerateFiles( folder, "*", SearchOption.AllDirectories ) )
				{
					token.ThrowIfCancellationRequested();
					var path = Path.GetRelativePath( folder, file ).Replace( '\\', '/' );
					if ( !IsContentFile( path ) ) continue;

					AssetDownloadCache.Mount( redirect, path, file );
				}

				await Engine.Shaders.ShaderUpgrade.UpgradeAsync( redirect, title, token );
				token.ThrowIfCancellationRequested();
				lock ( mounted ) return new WorkshopFileSystem( item );
			}
			catch
			{
				lock ( mounted ) mounted.Remove( id );
				item.Files.Dispose();
				Services.Ugc.OnPackagesUnmounted();
				throw;
			}
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>
	/// Published content includes code archives, but never executable binaries or the item's metadata file.
	/// </summary>
	internal static bool IsContentFile( string path ) => !string.Equals( path, WorkshopItemMetaData.FileName, StringComparison.OrdinalIgnoreCase ) && AssetDownloadCache.IsLegalDownload( path );

	/// <summary>
	/// Releases this reference when its using scope ends.
	/// </summary>
	void IDisposable.Dispose() => Dispose();

	/// <summary>
	/// Releases this owner's reference. The last owner unmounts the files and lets Steam update them.
	/// </summary>
	internal override void Dispose()
	{
		if ( item is null ) return;
		base.Dispose();

		MountedItem released;
		lock ( mounted )
		{
			released = item;
			item = null;
			if ( --released.References != 0 ) return;
			mounted.Remove( released.Id );
		}

		released.Files.Dispose();
		NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources();
		Services.Ugc.OnPackagesUnmounted();
	}
}
