using Sandbox.Mounting;
using System;
using System.IO;
using System.Threading.Tasks;
using MountDirectory = Sandbox.Mounting.Directory;

namespace Sandbox;

/// <summary>
/// Manual editor commands for refreshing the model spawnicons shipped in the bake cache.
/// </summary>
internal sealed class SpawnIconBake( BakeCacheWriter cache )
{
	int written;
	int failed;

	[ConCmd( "bake_spawnicons_addons", ConVarFlags.Protected, Help = "Bake models from addons/content and addons/sandbox into core/bake.cache." )]
	internal static Task BakeAddons() => Bake( mounts: false );

	[ConCmd( "bake_spawnicons_mounts", ConVarFlags.Protected, Help = "Bake models from every available game mount into core/bake.cache." )]
	internal static Task BakeMounts() => Bake( mounts: true );

	static async Task Bake( bool mounts )
	{
		try
		{
			using var writer = BakeCacheWriter.Open();
			var bake = new SpawnIconBake( writer );

			if ( mounts )
			{
				await bake.RenderMounts();
			}
			else
			{
				await bake.RenderAddons();
			}

			await Task.Run( writer.Save );
			Log.Info( $"Baked {bake.written} spawnicons to game/core/bake.cache. {bake.failed} models failed." );
		}
		catch ( Exception e )
		{
			Log.Error( e, "Spawnicon bake could not be saved." );
		}
	}

	async Task RenderMounts()
	{
		foreach ( var info in MountDirectory.GetAll().Where( x => x.Available ) )
		{
			var alreadyMounted = MountDirectory.Get( info.Ident )?.IsMounted == true;
			try
			{
				var paths = await GetMountedModels( info );
				if ( paths is null ) continue;

				Log.Info( $"Baking {paths.Length} models from {info.Title}..." );
				await RenderModels( paths );
			}
			finally
			{
				if ( !alreadyMounted ) await ReleaseMount( info );
			}
		}
	}

	static async Task<string[]> GetMountedModels( MountInfo info )
	{
		try
		{
			var mount = await MountDirectory.Mount( info.Ident );
			if ( mount?.IsMounted == true )
				return mount.GetAll( ResourceType.Model ).Select( x => x.Path ).ToArray();

			Log.Warning( $"Couldn't mount {info.Title} for the spawnicon bake." );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Skipping {info.Title} in the spawnicon bake." );
		}

		return null;
	}

	static async Task ReleaseMount( MountInfo info )
	{
		try
		{
			await EditorUtility.Mounting.UnmountForSession( info.Ident );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't unmount {info.Title} after the spawnicon bake." );
		}
	}

	async Task RenderAddons()
	{
		var sandbox = Editor.FileSystem.Root.GetFullPath( "/addons/sandbox/Assets" );
		if ( Project.Current?.GetAssetsPath()?.NormalizeFilename() != sandbox.NormalizeFilename() )
			throw new InvalidOperationException( "Open addons/sandbox/sandbox.sbproj so its models and materials are mounted." );

		var content = Editor.FileSystem.Root.GetFullPath( "/addons/content/Assets" );
		var paths = AddonModels( sandbox ).Concat( AddonModels( content ) ).Distinct().ToArray();
		Log.Info( $"Baking {paths.Length} addon models..." );
		await RenderModels( paths );
	}

	static IEnumerable<string> AddonModels( string root )
	{
		foreach ( var file in System.IO.Directory.EnumerateFiles( root, "*.vmdl*", SearchOption.AllDirectories ) )
		{
			var path = Path.GetRelativePath( root, file ).NormalizeFilename( false );
			if ( path.EndsWith( "_c" ) ) path = path[..^2];
			if ( path.EndsWith( ".vmdl" ) ) yield return path;
		}
	}

	async Task RenderModels( IEnumerable<string> paths )
	{
		foreach ( var path in paths )
		{
			await Task.Yield();
			(string Key, byte[] Png) icon;
			try
			{
				icon = await EditorUtility.RenderSpawnIconAsync( path );
			}
			catch ( Exception e )
			{
				Log.Warning( $"Couldn't bake '{path}': {e.Message}" );
				failed++;
				continue;
			}

			cache.Set( icon.Key, icon.Png );
			written++;
			if ( written % 100 == 0 ) Log.Info( $"Baked {written} spawnicons..." );
		}
	}
}
