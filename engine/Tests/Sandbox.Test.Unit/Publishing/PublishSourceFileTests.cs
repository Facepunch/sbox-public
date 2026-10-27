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
	[DataRow( "exact", true )]
	[DataRow( "wildcard", true )]
	[DataRow( "folder", true )]
	[DataRow( "exact", false )]
	[DataRow( "wildcard", false )]
	public async Task ResourceIncludesPublishCompiledAssetsAndOnlyOptInSources( string selection, bool usedOnly )
	{
		var directory = Path.Combine( Path.GetTempPath(), $"publish_sources_{Guid.NewGuid():N}" );
		var folder = selection == "folder" ? "Resources" : "levels";
		var assetsPath = Path.Combine( directory, "Assets" );
		Directory.CreateDirectory( Path.Combine( assetsPath, folder ) );
		var all = (Dictionary<ulong, Asset>)typeof( AssetSystem ).GetField( "allAssets", BindingFlags.Static | BindingFlags.NonPublic ).GetValue( null );
		var byPath = (ConcurrentDictionary<string, Asset>)typeof( AssetSystem ).GetField( "assetsByPath", BindingFlags.Static | BindingFlags.NonPublic ).GetValue( null );
		var scene = new PublishAsset( assetsPath, $"{folder}/rounds.scene", $"{folder}/rounds.scene_c" );
		var sound = new PublishAsset( assetsPath, $"{folder}/music.wav", $"{folder}/music.vsnd_c" );
		var material = new PublishAsset( assetsPath, "material.vmat", "material.vmat_c" );
		scene.References.Add( material );
		PublishAsset[] assets = [scene, sound, material];
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
			File.WriteAllText( Path.Combine( assetsPath, $"{folder}/icon.png" ), "runtime image" );
			var project = new Project( new DirectoryInfo( directory ) ) { Config = new Sandbox.DataModel.ProjectConfig() };
			project.Config.Type = "game";
			project.Config.SetMeta( "StartupScene", "" );
			project.Config.PublishUsedAssetsOnly = usedOnly;
			project.Config.Resources = selection switch
			{
				"exact" => $"{folder}/rounds.scene\n{folder}/music.wav\n{folder}/data.json\n{folder}/icon.png",
				"wildcard" => $"{folder}/*",
				_ => ""
			};
			foreach ( var preview in new[] { false, true } )
				foreach ( var sources in new[] { false, true } )
				{
					// Start without compiler output each time: checking for an existing _c before collection is insufficient.
					foreach ( var asset in assets ) File.Delete( asset.AbsoluteCompiledPath );
					var manifest = new ProjectPublisher.PackageManifest { IncludeSourceFiles = sources };
					await manifest.BuildFromAssets( project, previewOnly: preview );
					CollectionAssert.AreEqual( Array.Empty<string>(), manifest.Errors.ToArray() );
					var expected = new List<string> { $"{folder}/rounds.scene_c", $"{folder}/music.vsnd_c", "material.vmat_c", $"{folder}/data.json", $"{folder}/icon.png" };
					if ( sources ) expected.AddRange( assets.Select( x => x.RelativePath ) );
					CollectionAssert.AreEquivalent( expected, manifest.Assets.Select( x => x.Name ).ToList(), $"{selection}, usedOnly={usedOnly}, preview={preview}, sources={sources}" );
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
