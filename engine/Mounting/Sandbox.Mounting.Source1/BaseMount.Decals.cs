using System;
using System.IO;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

public abstract partial class BaseMount
{
	private void MountDecals( MountContext context, FileIndex files )
	{
		files.DecalMaterials.Clear();
		files.Decals = new SourceDecalLibrary();
		var script = files.Files.ContainsKey( "scripts/decals_subrect.txt" ) ? "scripts/decals_subrect.txt"
			: files.Files.ContainsKey( "scripts/decals.txt" ) ? "scripts/decals.txt" : null;
		if ( script is not null )
		{
			try
			{
				using var stream = files.Files[script].Open();
				using var reader = new StreamReader( stream );
				files.Decals = SourceDecalLibrary.Parse( reader.ReadToEnd(), script, message => Log.Trace( message ) );
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
			{
				Log.Trace( $"{Title}: named decal groups from '{script}' were not imported: {e.Message}" );
			}
		}
		else
			Log.Trace( $"{Title}: no Source decal script is available; named impact decal groups were not mounted." );

		var referenced = files.Decals.Groups.Values.SelectMany( entries => entries )
			.Where( entry => entry.Weight > 0 ).Select( entry => (Path: entry.Material, Root: "") )
			.Concat( files.MapDecals ).Distinct();
		foreach ( var (path, root) in referenced )
		{
			var resourcePath = $"{root}{path}";
			if ( files.DecalMaterials.ContainsKey( resourcePath ) ) continue;
			try
			{
				var material = SourceDecalMaterial.Read( path, file => OpenContentFile( file, root ), message => Log.Trace( message ) )
					with
				{ Path = resourcePath };
				files.DecalMaterials.Add( resourcePath, material );
				context.Add( ResourceType.Texture, $"{resourcePath}.decal_color.vtex", new SourceDecalTextureLoader( material, false ) );
				if ( !string.IsNullOrWhiteSpace( material.Parameters.String( "$bumpmap" ) ) )
					context.Add( ResourceType.Texture, $"{resourcePath}.decal_normal.vtex", new SourceDecalTextureLoader( material, true ) );
				context.Add( ResourceType.GameResource, $"{resourcePath}.decal", new SourceDecalLoader( material ) );
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
			{
				Log.Trace( $"{Title}: decal metadata from '{path}' could not be imported: {e.Message}" );
			}
		}
		foreach ( var group in files.Decals.Groups )
		{
			if ( group.Value.Where( entry => entry.Weight > 0 ).Select( entry => entry.Weight ).Distinct().Skip( 1 ).Any() )
				Log.Trace( $"{Title}: decal group '{group.Key}' has unequal weights; eligible variants use equal selection chances." );
			context.Add( ResourceType.PrefabFile, DecalGroupPath( group.Key ), new SourceDecalPrefabLoader( group.Key, group.Value ) );
		}
		context.Add( ResourceType.PrefabFile, "decals/no_impact.prefab", new SourceDecalPrefabLoader( "no impact", [] ) );
	}

	private string DecalGroupPath( string group ) => $"decals/{SurfaceResourceName( group )}.prefab";

	internal bool HasDecalMaterial( string path ) => index.DecalMaterials.ContainsKey( path );

	internal DecalDefinition GetDecal( string path, string contentRoot = "" )
	{
		path = VmtFile.NormalizePath( path, ".vmt" );
		if ( contentRoot.Length > 0 && HasDecalMaterial( $"{contentRoot}{path}" ) )
			path = $"{contentRoot}{path}";
		if ( !HasDecalMaterial( path ) )
			throw new FileNotFoundException( $"{Title}: decal material '{path}' is not registered in the mount.", path );
		var resource = $"mount://{Ident}/{path}.decal";
		return GameResource.Load<DecalDefinition>( resource )
			?? throw new InvalidDataException( $"{Title}: mounted decal '{resource}' could not be loaded." );
	}

	internal GameObject GetImpactDecal( char material )
	{
		var group = index.Decals.ImpactGroup( material );
		if ( group is null && material != '-' ) return null;
		var path = material == '-' ? "decals/no_impact.prefab" : DecalGroupPath( group );
		var prefab = PrefabFile.Load( $"mount://{Ident}/{path}" );
		if ( prefab is null )
		{
			Log.Trace( $"{Title}: impact decal group '{group}' could not be loaded; its surface slot is empty." );
			return null;
		}
		return SceneUtility.GetPrefabScene( prefab );
	}
}
