using System;
using System.IO;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

public abstract partial class BaseMount
{
	internal Material LoadContentMaterial( string path, string contentRoot = "" )
	{
		path = NormalizeMaterialPath( path );
		return Material.Load( GetRegisteredContentPath( path, ResourceType.Material, contentRoot ) );
	}

	internal SoundFile LoadContentSound( string path, string contentRoot = "" ) =>
		SoundFile.Load( GetRegisteredContentPath( path, ResourceType.Sound, contentRoot ) );

	internal Model LoadContentModel( string path, string contentRoot = "" ) =>
		Model.Load( GetRegisteredContentPath( path, ResourceType.Model, contentRoot ) );

	internal string GetBreakModelPath( string path )
	{
		var resource = GetContentPath( path );
		if ( resource is null ) return null;
		return GetByPath( $"{resource}.vmdl" ) is { Type: ResourceType.Model } loader ? loader.Path : null;
	}

	private string GetRegisteredContentPath( string path, ResourceType type, string contentRoot = "" )
	{
		path = path.Replace( '\\', '/' );
		path = FindContentFile( index, path, contentRoot ) ?? path;
		var extension = type switch
		{
			ResourceType.Material => ".vmat",
			ResourceType.Sound => ".vsnd",
			ResourceType.Model => ".vmdl",
			_ => throw new ArgumentOutOfRangeException( nameof( type ), type, null )
		};
		var resource = $"mount://{Ident}/{path}{extension}";
		if ( GetByPath( resource ) is not { } loader || loader.Type != type )
			throw new FileNotFoundException( $"Resource '{resource}' is not registered in mounted content.", path );
		return loader.Path;
	}

	internal SourceMaterial GetSourceMaterial( string path )
	{
		using var files = AcquireIndex();
		path = NormalizeMaterialPath( files, path );
		Lazy<SourceMaterial> source;
		lock ( indexLock )
		{
			var filename = ResolveContentFile( files, path );
			if ( !files.MaterialSources.TryGetValue( filename, out source ) )
				files.MaterialSources.Add( filename, source = new Lazy<SourceMaterial>( () =>
					SourceMaterial.Read( this, path, files.SourceScope, ( file, root ) => OpenContentFile( files, file, root ) ) ) );
		}
		return source.Value;
	}

	internal SourceModel ReadSourceModel( string path, string contentRoot = "", bool readLighting = false )
	{
		using var files = AcquireIndex();
		return SourceModel.Read( this, ResolveContentFile( files, path, contentRoot ), readLighting,
			file => OpenContentFile( files, file ),
			file => FindContentFile( files, file ) is { } filename ? files.Files[filename].Open() : null );
	}

	internal SourceTexture GetSourceTexture( string path, string contentRoot = "" )
	{
		path = path.Replace( '\\', '/' );
		using var files = AcquireIndex();
		Lazy<SourceTexture> source;
		lock ( indexLock )
		{
			if ( !files.Files.ContainsKey( path ) )
				path = VmtFile.NormalizePath( path, ".vtf" );
			var filename = ResolveContentFile( files, path, contentRoot );
			if ( !files.TextureSources.TryGetValue( filename, out source ) )
			{
				var file = files.Files[filename];
				files.TextureSources.Add( filename, source = new Lazy<SourceTexture>( () =>
					SourceTexture.Read( file, $"mount://{Ident}/{filename}.vtex" ) ) );
			}
		}
		return source.Value;
	}

	internal static string GetContentRoot( string path, string folder )
	{
		path = path.Replace( '\\', '/' );
		if ( path.StartsWith( $"{folder}/", StringComparison.OrdinalIgnoreCase ) )
			return "";
		var start = path.IndexOf( $"/{folder}/", StringComparison.OrdinalIgnoreCase );
		return start < 0 ? "" : path[..(start + 1)];
	}

	private string NormalizeMaterialPath( string path ) => NormalizeMaterialPath( index, path );

	private static string NormalizeMaterialPath( FileIndex files, string path )
	{
		path = path.Replace( '\\', '/' );
		return files.Files.ContainsKey( path ) ? path : VmtFile.NormalizePath( path, ".vmt" );
	}

	private static string FindContentFile( FileIndex files, string path, string contentRoot = "" )
	{
		path = path.Replace( '\\', '/' );
		while ( contentRoot.Length > 0 )
		{
			if ( files.Files.ContainsKey( $"{contentRoot}{path}" ) )
				return $"{contentRoot}{path}";
			contentRoot = files.ContentParents.GetValueOrDefault( contentRoot, "" );
		}
		return files.Files.ContainsKey( path ) ? path : null;
	}

	private static string ResolveContentFile( FileIndex files, string path, string contentRoot = "" )
	{
		if ( FindContentFile( files, path, contentRoot ) is { } filename )
			return filename;
		throw new FileNotFoundException( $"File '{path}' was not found in mounted content.", path );
	}

	internal void TrackMaterialSource( Material material, SourceMaterial source )
	{
		lock ( indexLock )
		{
			if ( !ReferenceEquals( source.Scope, index.SourceScope ) )
				throw new ObjectDisposedException( nameof( SourceMaterial ), "The material import snapshot was retired." );
			index.MaterialVariants[material] = new SourceMaterialVariant( source );
		}
	}

	internal Vector2 GetMaterialMappingSize( Material material, BspTexData data )
	{
		SourceMaterial source;
		lock ( indexLock )
			source = index.MaterialVariants.GetValueOrDefault( material )?.Source;
		return source is not null && source.TryGetMappingSize( out var size )
			? size : new Vector2( Math.Max( data.Width, 1 ), Math.Max( data.Height, 1 ) );
	}

	internal Material CreateMaterialVariant( Material original, string name, SourceMaterialOverrides bindings = null )
	{
		SourceMaterialVariant source;
		lock ( indexLock )
		{
			if ( original is null )
				throw new InvalidOperationException( "A missing material cannot be used to build a skin variant." );
			if ( !index.MaterialVariants.TryGetValue( original, out source ) )
				source = SourceMaterialVariant.ReadEngineFallback( original, index.SourceScope )
					?? throw new InvalidOperationException( $"Material '{original.Name}' has no import provenance; a skin variant cannot be built." );
		}
		if ( bindings is not null )
			source = source.WithOverrides( bindings );
		var material = source.Create( name );
		lock ( indexLock )
		{
			if ( !ReferenceEquals( source.Scope, index.SourceScope ) )
				throw new ObjectDisposedException( nameof( SourceMaterial ), "The material import snapshot was retired." );
			index.MaterialVariants[material] = source;
		}
		return material;
	}
}
