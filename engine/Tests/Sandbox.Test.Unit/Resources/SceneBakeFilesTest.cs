using Editor;
using System;
using System.Collections.Generic;
using System.IO;

namespace ResourceTests;

/// <summary>
/// Exercises scene geometry bake paths across recompiles and migration from GUID folders.
/// </summary>
[TestClass]
public class SceneBakeFilesTest
{
	/// <summary>
	/// Stage cleanup preserves shared outputs. A full clear removes the entire generated folder.
	/// </summary>
	[TestMethod]
	public void ClearsJobOutputs()
	{
		var folder = Directory.CreateTempSubdirectory();
		try
		{
			var scene = Path.Combine( folder.FullName, "test.scene" );
			var root = folder.CreateSubdirectory( "test_scene_data" );
			var job = Path.Combine( root.FullName, "custom.probes" );
			var shared = Path.Combine( root.FullName, "shared.bin" );
			var referenced = Path.Combine( root.FullName, "custom.vtex_c" );
			var authored = Path.Combine( root.FullName, "authored.probes" );
			var unclaimed = Path.Combine( root.CreateSubdirectory( "envmap" ).FullName, "12345678.vtex_c" );
			var extra = Path.Combine( root.FullName, "custom.index" );
			foreach ( var file in new[] { job, shared, referenced, authored, unclaimed, extra } )
				File.WriteAllText( file, "test" );

			File.WriteAllText( scene, """{ "Texture": "maps/test_scene_data/custom.vtex" }""" );
			var outputs = new SceneCompileOutputs( scene );
			outputs.Observe( "User probes", ["custom.probes", "shared.bin", "custom.vtex_c"] );
			outputs.Observe( "User audio", ["shared.bin"] );
			var cleanup = outputs.CleanupFor( "User probes" );
			Assert.ThrowsException<ArgumentException>( () => cleanup.DeleteOutput( "../test.scene" ) );
			Assert.ThrowsException<ArgumentException>( () => cleanup.DeleteOutput( scene ) );
			Assert.AreEqual( 3, outputs.Files().Count( x => x.Group == "User probes" ) );
			cleanup.DeleteOutput( "custom.index" );
			outputs.Clear( new HashSet<string> { "User probes" }, "maps/test.scene" );
			Assert.IsFalse( File.Exists( job ) );
			Assert.IsFalse( File.Exists( extra ) );
			Assert.IsTrue( File.Exists( shared ) );
			Assert.IsTrue( File.Exists( referenced ) );
			Assert.IsTrue( File.Exists( authored ) );
			Assert.IsTrue( File.Exists( unclaimed ) );

			// Full clear also works after reopening the scene, with no remembered jobs or files.
			outputs = new SceneCompileOutputs( scene );
			outputs.Clear( null, "maps/test.scene" );
			outputs.Clear( null, "maps/test.scene" );
			Assert.IsFalse( Directory.Exists( root.FullName ) );
			Assert.IsTrue( File.Exists( scene ) );
		}
		finally
		{
			folder.Delete( recursive: true );
		}
	}

	/// <summary>
	/// A smaller bake drops the unused indexes and legacy folder while keeping unrelated files.
	/// </summary>
	[TestMethod]
	public void CleanupAfterSmallerBake()
	{
		var folder = Directory.CreateTempSubdirectory();
		try
		{
			foreach ( var file in new[] { "aggregate_0.vmdl_c", "aggregate_1.vmdl_c", "mesh_0.vmdl_c", "collision_0.vphys_c", "collision_1.vphys_c", "mesh_custom.vmdl_c" } )
				File.WriteAllText( Path.Combine( folder.FullName, file ), file );

			var legacy = folder.CreateSubdirectory( Guid.NewGuid().ToString( "N" ) );
			File.WriteAllText( Path.Combine( legacy.FullName, "mesh_0.vmdl_c" ), "old" );
			var other = folder.CreateSubdirectory( "custom" );

			SceneCompiler.CleanupCompiledFiles( folder.FullName, 1, 0, 1 );

			CollectionAssert.AreEquivalent( new[] { "aggregate_0.vmdl_c", "collision_0.vphys_c", "mesh_custom.vmdl_c" },
				Array.ConvertAll( folder.GetFiles(), file => file.Name ) );
			Assert.IsFalse( Directory.Exists( legacy.FullName ) );
			Assert.IsTrue( Directory.Exists( other.FullName ) );

			SceneCompiler.CleanupCompiledFiles( folder.FullName, 0, 0, 0 );
			Assert.AreEqual( "mesh_custom.vmdl_c", folder.GetFiles().Single().Name );
		}
		finally
		{
			folder.Delete( recursive: true );
		}
	}

	/// <summary>
	/// Every numbered output must exist, even when later indexes and unrelated files are present.
	/// </summary>
	[TestMethod]
	public void ChecksEveryNumberedOutput()
	{
		var folder = Directory.CreateTempSubdirectory();
		try
		{
			var scene = Path.Combine( folder.FullName, "test.scene" );
			File.WriteAllText( scene, "{}" );
			var hash = Convert.ToHexString( SceneResourceCompiler.GetSourceHash( scene ) );
			var bake = SceneResourceCompiler.GetBakePath( scene );
			Directory.CreateDirectory( Path.GetDirectoryName( bake ) );
			File.WriteAllText( bake, $"{hash} 0 0 0" );
			Assert.IsTrue( SceneResourceCompiler.IsBakeCurrent( scene, out _ ) );

			File.WriteAllText( bake, $"{hash} 3 1 1\n{{}}" );
			var compiled = Directory.CreateDirectory( SceneResourceCompiler.GetCompiledFolder( scene ) );
			Assert.IsFalse( SceneResourceCompiler.IsBakeCurrent( scene, out _ ) );
			foreach ( var file in new[] { "aggregate_0.vmdl_c", "aggregate_2.vmdl_c", "aggregate_3.vmdl_c", "mesh_0.vmdl_c", "collision_0.vphys_c" } )
				File.WriteAllText( Path.Combine( compiled.FullName, file ), "" );

			Assert.IsFalse( SceneResourceCompiler.IsBakeCurrent( scene, out _ ) );
			File.WriteAllText( Path.Combine( compiled.FullName, "aggregate_1.vmdl_c" ), "" );
			Assert.IsTrue( SceneResourceCompiler.IsBakeCurrent( scene, out _ ) );
			Assert.AreEqual( "{}", SceneResourceCompiler.ReadBake( scene ) );
			foreach ( var file in new[] { "mesh_0.vmdl_c", "collision_0.vphys_c" } )
			{
				var path = Path.Combine( compiled.FullName, file );
				File.Delete( path );
				Assert.IsFalse( SceneResourceCompiler.IsBakeCurrent( scene, out _ ) );
				Assert.IsNull( SceneResourceCompiler.ReadBake( scene ) );
				File.WriteAllText( path, "" );
			}

			File.WriteAllText( bake, $"{hash} -1 0 0" );
			Assert.IsFalse( SceneResourceCompiler.IsBakeCurrent( scene, out _ ) );
		}
		finally
		{
			folder.Delete( recursive: true );
		}
	}

	/// <summary>
	/// A cancellation after replacing the first model leaves the complete set of filenames
	/// on disk, but the resource compiler must decline the interrupted bake.
	/// </summary>
	[TestMethod]
	public void CancelledGeometryWriteInvalidatesBake()
	{
		var folder = Directory.CreateTempSubdirectory();
		try
		{
			var scene = Path.Combine( folder.FullName, "test.scene" );
			File.WriteAllText( scene, "{}" );
			var compiled = Directory.CreateDirectory( SceneResourceCompiler.GetCompiledFolder( scene ) );
			var first = Path.Combine( compiled.FullName, "aggregate_0.vmdl_c" );
			var second = Path.Combine( compiled.FullName, "aggregate_1.vmdl_c" );
			File.WriteAllBytes( first, [1] );
			File.WriteAllBytes( second, [2] );
			var hash = Convert.ToHexString( SceneResourceCompiler.GetSourceHash( scene ) );
			File.WriteAllText( SceneResourceCompiler.GetBakePath( scene ), $"{hash} 2 0 0\n{{}}" );

			var output = new SceneCompiler.GeometryOutputFolder( new InterruptedFolder( compiled ), scene );
			Assert.IsTrue( SceneResourceCompiler.IsBakeCurrent( scene, out _ ) );
			Assert.ThrowsException<OperationCanceledException>( () => output.WriteFile( "aggregate_0.vmdl_c", [3] ) );
			CollectionAssert.AreEqual( new byte[] { 3 }, File.ReadAllBytes( first ) );
			CollectionAssert.AreEqual( new byte[] { 2 }, File.ReadAllBytes( second ) );
			Assert.IsFalse( SceneResourceCompiler.IsBakeCurrent( scene, out _ ) );
			Assert.IsNull( SceneResourceCompiler.ReadBake( scene ) );
		}
		finally
		{
			folder.Delete( recursive: true );
		}
	}

	sealed class InterruptedFolder( DirectoryInfo folder ) : SceneFolder
	{
		/// <summary>
		/// Interrupts the compile immediately after its output reaches disk.
		/// </summary>
		public override string WriteFile( string filename, byte[] data )
		{
			File.WriteAllBytes( Path.Combine( folder.FullName, filename ), data );
			throw new OperationCanceledException();
		}
	}

	/// <summary>
	/// Legacy bakes validate the model and physics references in the saved scene.
	/// </summary>
	[TestMethod]
	[DataRow( "." )]
	[DataRow( "eee4dcd1e0d84a45bad19444b41fd5ad" )]
	public void ChecksLegacyOutputReferences( string generation )
	{
		var folder = Directory.CreateTempSubdirectory();
		try
		{
			var scene = Path.Combine( folder.FullName, "test.scene" );
			File.WriteAllText( scene, "{}" );
			var hash = Convert.ToHexString( SceneResourceCompiler.GetSourceHash( scene ) );
			var bake = SceneResourceCompiler.GetBakePath( scene );
			var relative = "test_scene_data/compiled" + (generation == "." ? "" : "/" + generation);
			Directory.CreateDirectory( Path.GetDirectoryName( bake ) );
			File.WriteAllText( bake, $"{hash} -" );
			Assert.IsTrue( SceneResourceCompiler.IsBakeCurrent( scene, out _ ) );

			var json = $$$"""{"GameObjects":[{"Components":[{"Model":{"Path":"{{{relative}}}/aggregate_0.vmdl"}},{"Physics":"{{{relative}}}/collision_0.vphys"}]}]}""";
			File.WriteAllText( bake, $"{hash} {generation}\n{json}" );
			Assert.IsFalse( SceneResourceCompiler.IsBakeCurrent( scene, out _ ) );
			var compiled = Directory.CreateDirectory( Path.Combine( folder.FullName, relative ) );
			File.WriteAllText( Path.Combine( compiled.FullName, "aggregate_0.vmdl_c" ), "" );
			Assert.IsFalse( SceneResourceCompiler.IsBakeCurrent( scene, out _ ) );
			File.WriteAllText( Path.Combine( compiled.FullName, "collision_0.vphys_c" ), "" );
			Assert.IsTrue( SceneResourceCompiler.IsBakeCurrent( scene, out var outputFolder ) );
			Assert.AreEqual( compiled.FullName, Path.GetFullPath( outputFolder ) );
			Assert.AreEqual( json, SceneResourceCompiler.ReadBake( scene ) );
		}
		finally
		{
			folder.Delete( recursive: true );
		}
	}
}
