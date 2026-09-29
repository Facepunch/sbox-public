using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Editor.ProjectPublisher;

namespace Editor;

/// <summary>
/// Scene compilation policy and immutable runtime snapshots for one publication.
/// </summary>
internal sealed class SceneCompilePublication( bool sourcePackage = false )
{
	readonly Dictionary<Asset, FileSnapshot> _scenes = new();
	readonly SceneCompileCache.ManifestSnapshot _manifests = new();

	sealed record FileSnapshot( string Name, string AbsolutePath, int Size, string Hash );

	static InvalidOperationException Changed( string path ) =>
		new( $"Scene compilation file '{path}' changed or is missing from the publication snapshot. Prepare a new publish manifest before publishing." );

	internal bool IncludeAsset( Asset asset )
	{
		if ( !SceneCompileCache.ShouldPublishFile( asset.AbsolutePath, sourcePackage, _manifests ) )
			return false;

		Capture( asset );
		return true;
	}

	void Capture( Asset asset )
	{
		if ( sourcePackage || asset?.AssetType?.FileExtension != "scene" || _scenes.ContainsKey( asset ) )
			return;

		var validation = new SceneCompileCache.ValidationScope();
		if ( !SceneCompileCache.Validate( asset, out var error, validation ) )
			throw new InvalidOperationException( error );

		if ( !SceneCompileCache.HasCompilation( asset, validation ) )
		{
			_scenes.Add( asset, null );
			return;
		}

		if ( !asset.IsCompiledAndUpToDate && !asset.Compile( false ) )
			throw new InvalidOperationException( $"Could not compile '{asset.Path}' before collecting its compiled scene data." );

		var path = asset.GetCompiledFile( true );
		if ( string.IsNullOrEmpty( path ) )
			path = asset.GetSourceFile( true ) + "_c";

		var relativePath = asset.GetCompiledFile( false );
		if ( string.IsNullOrEmpty( relativePath ) )
			relativePath = asset.Path + "_c";

		using var stream = File.OpenRead( path );
		_scenes.Add( asset, new FileSnapshot(
			relativePath.NormalizeFilename( false, false ).TrimStart( '/' ),
			path, checked((int)stream.Length), Sandbox.Utility.Crc64.FromStream( stream ).ToString( "x" ) ) );
	}

	static Asset FindScene( ProjectFile file )
	{
		var path = file.Name.EndsWith( ".scene_c", StringComparison.OrdinalIgnoreCase ) ? file.Name[..^2] : file.Name;
		if ( !path.EndsWith( ".scene", StringComparison.OrdinalIgnoreCase ) )
			return null;

		var absolutePath = file.AbsolutePath;
		if ( absolutePath?.EndsWith( ".scene_c", StringComparison.OrdinalIgnoreCase ) == true )
			absolutePath = absolutePath[..^2];

		return AssetSystem.FindByPath( file.AbsolutePath ) ?? AssetSystem.FindByPath( absolutePath ) ?? AssetSystem.FindByPath( path );
	}

	internal IReadOnlyList<ProjectFile> PrepareFiles( ProjectFile file )
	{
		if ( !SceneCompileCache.ShouldPublishFile( file.AbsolutePath, sourcePackage, _manifests ) )
			return [];

		if ( file.Contents is null && !File.Exists( file.AbsolutePath ) )
			return [file];

		if ( !sourcePackage )
			Capture( FindScene( file ) );

		return [file];
	}

	static void ValidateFile( string name, string path, int size, string hash )
	{
		if ( !File.Exists( path ) )
			throw Changed( name );

		using var stream = File.OpenRead( path );
		if ( stream.Length != size || Sandbox.Utility.Crc64.FromStream( stream ).ToString( "x" ) != hash )
			throw Changed( name );
	}

	internal void ValidateUploadContents( ProjectFile file, byte[] contents )
	{
		if ( _scenes.Values.Any( x => x is not null )
			&& (contents.Length != file.Size || Sandbox.Utility.Crc64.FromBytes( contents ).ToString( "x" ) != file.Hash) )
			throw Changed( file.Name );
	}

	internal void Validate( IReadOnlyList<ProjectFile> files )
	{
		if ( sourcePackage )
			return;

		var byName = files.ToDictionary( x => x.Name, StringComparer.OrdinalIgnoreCase );
		foreach ( var (asset, snapshot) in _scenes )
		{
			if ( snapshot is null )
				continue;

			if ( !byName.TryGetValue( snapshot.Name, out var runtime ) || runtime.Hash != snapshot.Hash || runtime.Size != snapshot.Size )
				throw Changed( snapshot.Name );

			foreach ( var reference in asset.GetReferences( true ) )
			{
				if ( !CanPublishFile( reference ) )
					continue;

				var compiled = reference.GetCompiledFile( false );
				if ( !string.IsNullOrEmpty( compiled ) && !byName.ContainsKey( compiled.NormalizeFilename( false, false ).TrimStart( '/' ) ) )
					throw Changed( compiled );
			}
		}

		// Reference discovery can compile resources. Do not reuse validation fingerprints across it.
		var validation = new SceneCompileCache.ValidationScope();
		foreach ( var (asset, snapshot) in _scenes )
		{
			if ( !SceneCompileCache.Validate( asset, out var error, validation ) )
				throw new InvalidOperationException( error );

			if ( SceneCompileCache.HasCompilation( asset, validation ) != (snapshot is not null) )
				throw Changed( asset.Path );

			if ( snapshot is not null )
				ValidateFile( snapshot.Name, snapshot.AbsolutePath, snapshot.Size, snapshot.Hash );
		}

		foreach ( var file in files )
		{
			if ( !SceneCompileCache.ShouldPublishFile( file.AbsolutePath, false, validation.Manifests, validation ) )
				throw Changed( file.Name );

			// The source-package filter also identifies compiler-owned runtime output.
			if ( !SceneCompileCache.ShouldPublishFile( file.AbsolutePath, true, validation.Manifests, validation ) )
				ValidateFile( file.Name, file.AbsolutePath, file.Size, file.Hash );

			var asset = FindScene( file );
			if ( asset is null || _scenes.ContainsKey( asset ) )
				continue;

			if ( !SceneCompileCache.Validate( asset, out var error, validation ) )
				throw new InvalidOperationException( error );

			if ( SceneCompileCache.HasCompilation( asset, validation ) )
				throw Changed( file.Name );
		}
	}
}
