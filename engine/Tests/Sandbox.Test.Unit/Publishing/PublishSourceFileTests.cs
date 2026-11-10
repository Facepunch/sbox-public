using Editor;
using Sandbox;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

[TestClass, DoNotParallelize]
public class PublishSourceFileTests
{
	[TestMethod]
	public async Task SourceImageAliasesHaveTheSameNonzeroPublishEstimate()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"publish_image_estimate_{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );
		try
		{
			var asset = new PublishAsset( directory, "image.png", "unused.compiled", "image.jpg" );
			File.WriteAllBytes( asset.AbsolutePath, new byte[123] );
			var manifest = new ProjectPublisher.PackageManifest();
			var build = typeof( ProjectPublisher.PackageManifest ).GetMethod( "BuildReviewSizes", BindingFlags.Instance | BindingFlags.NonPublic );
			await (Task)build.Invoke( manifest, [new Asset[] { asset }, new HashSet<string>(), System.Threading.CancellationToken.None] );
			Assert.AreEqual( 123L, manifest.GetPublishImpact( ["image.jpg"] ).TotalBytes );
			Assert.AreEqual( 123L, manifest.GetPublishImpact( ["image.png"] ).TotalBytes );
			Assert.AreEqual( 123L, manifest.GetPublishImpact( ["image.jpg", "image.png"] ).TotalBytes );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ResourceRulesExcludeMetadataUnlessPublishingSources( bool usedOnly )
	{
		var directory = Path.Combine( Path.GetTempPath(), $"publish_metadata_{Guid.NewGuid():N}" );
		var assetsPath = Path.Combine( directory, "Assets" );
		string[] files = ["sounds/jump.wav", "sounds/jump.wav.meta", "sounds/jump.vsnd_c",
			"music/track.ogg", "music/track.ogg.META", "music/track.vsnd_c", "textures/icon.png.meta"];
		try
		{
			foreach ( var file in files )
			{
				var absolute = Path.Combine( assetsPath, file );
				Directory.CreateDirectory( Path.GetDirectoryName( absolute ) );
				File.WriteAllText( absolute, "test" );
			}
			var project = new Project( new DirectoryInfo( directory ) ) { Config = new Sandbox.DataModel.ProjectConfig() };
			project.Config.Type = "game";
			project.Config.SetMeta( "StartupScene", "" );
			project.Config.PublishUsedAssetsOnly = usedOnly;
			project.Config.Resources = "/sounds/*\n/music/*\n/textures/icon.png.meta";
			foreach ( var preview in new[] { false, true } )
				foreach ( var sources in new[] { false, true } )
				{
					var manifest = new ProjectPublisher.PackageManifest { IncludeSourceFiles = sources };
					Sandbox.Tasks.AsyncPump.RunSynchronously( () => manifest.BuildFromAssets( project, previewOnly: preview ) );
					CollectionAssert.AreEqual( Array.Empty<string>(), manifest.Errors.ToArray() );
					var expected = files.Where( x => sources || !x.EndsWith( ".meta", StringComparison.OrdinalIgnoreCase ) ).ToArray();
					CollectionAssert.AreEquivalent( expected, manifest.Assets.Select( x => x.Name ).ToArray(), $"usedOnly={usedOnly}, preview={preview}, sources={sources}" );
				}
			var sourceExport = new ProjectPublisher.PackageManifest();
			Sandbox.Tasks.AsyncPump.RunSynchronously( () => sourceExport.BuildFromSource( project ) );
			Assert.IsTrue( sourceExport.Assets.Any( x => x.Name.EndsWith( "jump.wav.meta", StringComparison.OrdinalIgnoreCase ) ) );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	[DataRow( "exact", true )]
	[DataRow( "wildcard", true )]
	[DataRow( "folder", true )]
	[DataRow( "extension", true )]
	[DataRow( "compiled", true )]
	[DataRow( "exclude", true )]
	[DataRow( "extension", false )]
	[DataRow( "exact", false )]
	[DataRow( "wildcard", false )]
	public void ResourceIncludesKeepRuntimeSourcesButRequireOptInForAssetDefinitions( string selection, bool usedOnly )
	{
		var directory = Path.Combine( Path.GetTempPath(), $"publish_sources_{Guid.NewGuid():N}" );
		var folder = selection == "folder" ? "Resources" : "levels";
		var assetsPath = Path.Combine( directory, "Assets" );
		Directory.CreateDirectory( Path.Combine( assetsPath, folder ) );
		var all = (Dictionary<ulong, Asset>)typeof( AssetSystem ).GetField( "allAssets", BindingFlags.Static | BindingFlags.NonPublic ).GetValue( null );
		var byPath = (ConcurrentDictionary<string, Asset>)typeof( AssetSystem ).GetField( "assetsByPath", BindingFlags.Static | BindingFlags.NonPublic ).GetValue( null );
		var scene = new PublishAsset( assetsPath, $"{folder}/rounds.scene", $"{folder}/rounds.scene_c" );
		var sounds = new[] { "wav", "ogg", "mp3" }.Select( extension => new PublishAsset( assetsPath,
			$"{folder}/music_{extension}.{extension}", $"{folder}/music_{extension}.vsnd_c", $"{folder}/music_{extension}.vsnd" ) ).ToArray();
		var material = new PublishAsset( assetsPath, "material.vmat", "material.vmat_c" );
		var texture = new PublishAsset( assetsPath, "texture.vtex", "texture.vtex_c" );
		var image = new PublishAsset( assetsPath, $"{folder}/icon.png", $"{folder}/icon.vtex_c", $"{folder}/icon.vtex" );
		var data = new PublishAsset( assetsPath, $"{folder}/extra.data", $"{folder}/extra.custom_c", $"{folder}/extra.custom" );
		var definition = new PublishAsset( assetsPath, "definition.custom", "definition.custom_c" );
		var prefab = new PublishAsset( assetsPath, "prop.prefab", "prop.prefab_c" );
		scene.References.AddRange( [material, texture, definition, prefab] );
		PublishAsset[] assets = [scene, .. sounds, material, texture, image, data, definition, prefab];
		var selected = assets.Where( x => selection != "exclude" || !x.RelativePath.EndsWith( ".ogg" ) ).ToArray();
		var aliases = new List<string>();
		try
		{
			foreach ( var asset in assets )
			{
				all.Add( asset.AssetId, asset );
				foreach ( var path in new[] { asset.Path, asset.AbsolutePath, asset.AbsoluteCompiledPath } )
				{
					Assert.IsTrue( byPath.TryAdd( path, asset ) );
					aliases.Add( path );
				}
				File.WriteAllText( asset.AbsolutePath, "source" );
			}
			File.WriteAllText( Path.Combine( assetsPath, $"{folder}/data.json" ), "{}" );
			var project = new Project( new DirectoryInfo( directory ) ) { Config = new Sandbox.DataModel.ProjectConfig() };
			project.Config.Type = "game";
			project.Config.SetMeta( "StartupScene", "" );
			project.Config.PublishUsedAssetsOnly = usedOnly;
			project.Config.Resources = selection switch
			{
				"exact" => $"{folder}/rounds.scene\n{folder}/data.json\n{folder}/icon.png\n{folder}/extra.data\n" + string.Join( "\n", sounds.Select( x => x.RelativePath ) ),
				"extension" => $"/{folder}/*.ogg\n/{folder}/*.mp3\n/{folder}/*.wav\n{folder}/rounds.scene\n{folder}/*.json\n{folder}/*.png\n{folder}/*.data",
				"compiled" => $"{folder}/*.vsnd\n{folder}/rounds.scene\n{folder}/*.json\n{folder}/*.vtex\n{folder}/*.custom",
				"exclude" => $"{folder}/*\n!{folder}/*.ogg",
				"wildcard" => $"{folder}/*",
				_ => ""
			};
			if ( selection != "folder" ) project.Config.Resources += "\nmaterial.vmat\ntexture.vtex\ndefinition.custom\nprop.prefab";
			foreach ( var preview in new[] { false, true } )
				foreach ( var sources in new[] { false, true } )
				{
					// Start without compiler output each time: checking for an existing _c before collection is insufficient.
					foreach ( var asset in assets ) File.Delete( asset.AbsoluteCompiledPath );
					var manifest = new ProjectPublisher.PackageManifest { IncludeSourceFiles = sources };
					// Match the editor thread context while asset collection awaits parallel work.
					Sandbox.Tasks.AsyncPump.RunSynchronously( () => manifest.BuildFromAssets( project, previewOnly: preview ) );
					CollectionAssert.AreEqual( Array.Empty<string>(), manifest.Errors.ToArray() );
					var expected = selected.Select( x => x.GetCompiledFile() ).ToHashSet( StringComparer.OrdinalIgnoreCase );
					expected.Add( $"{folder}/data.json" );
					if ( selection != "compiled" )
						expected.UnionWith( selected.Where( x => sounds.Contains( x ) || x == image || x == data ).Select( x => x.RelativePath ) );
					if ( sources ) expected.UnionWith( selected.Select( x => x.RelativePath ) );
					CollectionAssert.AreEquivalent( expected.ToArray(), manifest.Assets.Select( x => x.Name ).ToArray(), $"{selection}, usedOnly={usedOnly}, preview={preview}, sources={sources}" );
					Assert.AreEqual( preview, manifest.Assets.All( x => x.Hash is null ) );
				}
		}
		finally
		{
			foreach ( var asset in assets ) all.Remove( asset.AssetId );
			foreach ( var path in aliases ) byPath.TryRemove( path, out _ );
			Directory.Delete( directory, true );
		}
	}

	// Use the managed asset implementation; only compilation and paths need disk-backed substitutes.
	sealed class PublishAsset : MountAsset
	{
		public List<Asset> References { get; } = [];
		public PublishAsset( string root, string source, string compiled, string logicalPath = null ) : base( BitConverter.ToUInt64( Guid.NewGuid().ToByteArray() ), null, null )
		{
			Path = logicalPath ?? source;
			RelativePath = source;
			AbsolutePath = AbsoluteSourcePath = System.IO.Path.Combine( root, source ).Replace( '\\', '/' );
			AbsoluteCompiledPath = System.IO.Path.Combine( root, compiled ).Replace( '\\', '/' );
			CompiledPath = compiled;
			AssetType = new AssetType();
		}
		string CompiledPath { get; }
		public override string GetSourceFile( bool absolute = false ) => absolute ? AbsolutePath : RelativePath;
		public override string GetCompiledFile( bool absolute = false ) => !File.Exists( AbsoluteCompiledPath ) ? null : absolute ? AbsoluteCompiledPath : CompiledPath;
		public override List<Asset> GetReferences( bool deep ) => References;
		public override bool IsProcedural => false;
		public override ValueTask<bool> CompileIfNeededAsync( float timeout = 30.0f )
		{
			File.WriteAllText( AbsoluteCompiledPath, "compiled" );
			return ValueTask.FromResult( true );
		}
	}
}
