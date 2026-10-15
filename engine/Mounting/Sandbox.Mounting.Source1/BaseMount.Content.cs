using System;
using System.IO;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

public abstract partial class BaseMount
{
	internal Material LoadContentMaterial( string path )
	{
		path = VmtFile.NormalizePath( path, ".vmt" );
		return Material.Load( GetRegisteredContentPath( path, ResourceType.Material ) );
	}

	internal SoundFile LoadContentSound( string path ) =>
		SoundFile.Load( GetRegisteredContentPath( path, ResourceType.Sound ) );

	internal Model LoadContentModel( string path ) =>
		Model.Load( GetRegisteredContentPath( path, ResourceType.Model ) );

	internal string GetBreakModelPath( string path )
	{
		var resource = GetContentPath( path );
		if ( resource is null ) return null;
		return GetByPath( $"{resource}.vmdl" ) is { Type: ResourceType.Model } loader ? loader.Path : null;
	}

	private string GetRegisteredContentPath( string path, ResourceType type )
	{
		path = path.Replace( '\\', '/' );
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
		path = VmtFile.NormalizePath( path, ".vmt" );
		lock ( indexLock )
		{
			var filename = ResolveContentFile( index, path );
			if ( !index.MaterialSources.TryGetValue( filename, out var source ) )
			{
				var scope = index.SourceScope;
				index.MaterialSources.Add( filename, source = new Lazy<SourceMaterial>( () => SourceMaterial.Read( this, path, scope ) ) );
			}
			return source.Value;
		}
	}

	internal SourceModel ReadSourceModel( string path )
	{
		lock ( indexLock )
			return SourceModel.Read( this, path );
	}

	internal SourceTexture GetSourceTexture( string path )
	{
		path = path.Replace( '\\', '/' );
		lock ( indexLock )
		{
			if ( !index.Files.ContainsKey( path ) )
				path = VmtFile.NormalizePath( path, ".vtf" );
			var filename = ResolveContentFile( index, path );
			if ( !index.TextureSources.TryGetValue( filename, out var source ) )
			{
				var file = index.Files[filename];
				index.TextureSources.Add( filename, source = new Lazy<SourceTexture>( () =>
					SourceTexture.Read( file, $"mount://{Ident}/{filename}.vtex" ) ) );
			}
			return source.Value;
		}
	}

	private static string ResolveContentFile( FileIndex files, string path )
	{
		if ( files.Files.ContainsKey( path ) )
			return path;
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

	internal Material CreateMaterialVariant( Material original, string name )
	{
		SourceMaterialVariant source;
		lock ( indexLock )
		{
			if ( original is null )
				throw new InvalidOperationException( "A missing material cannot be used to build a skin variant." );
			if ( !index.MaterialVariants.TryGetValue( original, out source ) )
				source = SourceMaterialVariant.ReadErrorMaterial( original, index.SourceScope )
					?? throw new InvalidOperationException( $"Material '{original.Name}' has no import provenance; a skin variant cannot be built." );
		}
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
