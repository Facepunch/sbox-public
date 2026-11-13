using Editor;
using System;
using System.Collections.Generic;
using System.IO;

namespace SceneTests;

// Issue #12095: compiled scene generations were deleted through the mounted filesystem, which is
// read-only, so every compile left the previous generations behind in <scene>_scene_data/compiled.
[TestClass]
public class SceneCompilerCleanupTest
{
	[TestMethod]
	public void CleanupDeletesOnlyPreviousGenerations()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"scene_compile_cleanup_{Guid.NewGuid():N}" );

		// The asset system hands back source paths with forward slashes, even on Windows.
		var sourcePath = Path.Combine( directory, "level.scene" ).Replace( '\\', '/' );
		var currentGeneration = Guid.NewGuid().ToString( "N" );
		var previous = SceneCompiler.GetGenerationPath( sourcePath, Guid.NewGuid().ToString( "N" ) );
		var active = SceneCompiler.GetGenerationPath( sourcePath, Guid.NewGuid().ToString( "N" ) );
		var current = SceneCompiler.GetGenerationPath( sourcePath, currentGeneration );
		var looseFile = Path.Combine( Path.GetDirectoryName( current ), "aggregate_0.vmdl_c" );

		// Other bakes live next to the compiled folder and must survive the cleanup.
		var navData = Path.Combine( directory, "level_scene_data", "navmesh", "baked.navdata" );

		try
		{
			foreach ( var generation in new[] { previous, active, current } )
			{
				Directory.CreateDirectory( generation );
				File.WriteAllText( Path.Combine( generation, "aggregate_0.vmdl_c" ), "compiled" );
			}

			Directory.CreateDirectory( Path.GetDirectoryName( navData ) );
			File.WriteAllText( navData, "navmesh" );
			File.WriteAllText( looseFile, "compiled" );

			SceneCompiler.CleanupPreviousGenerations( current, new object(), new HashSet<string>( [active, current], StringComparer.OrdinalIgnoreCase ) );

			Assert.AreEqual( Path.GetFullPath( Path.Combine( directory, "level_scene_data", "compiled", currentGeneration ) ), current );
			Assert.IsFalse( Directory.Exists( previous ) );
			Assert.IsFalse( File.Exists( looseFile ) );
			Assert.IsTrue( Directory.Exists( active ) );
			Assert.IsTrue( File.Exists( Path.Combine( current, "aggregate_0.vmdl_c" ) ) );
			Assert.IsTrue( File.Exists( navData ) );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public void FailedCompileDeletesItsGeneration()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"scene_compile_cleanup_{Guid.NewGuid():N}" );
		var staged = SceneCompiler.GetGenerationPath( Path.Combine( directory, "level.scene" ), Guid.NewGuid().ToString( "N" ) );

		try
		{
			Directory.CreateDirectory( staged );
			File.WriteAllText( Path.Combine( staged, "collision_0.vphys_c" ), "compiled" );

			SceneCompiler.CleanupGeneration( staged );

			Assert.IsFalse( Directory.Exists( staged ) );
			Assert.IsTrue( Directory.Exists( Path.GetDirectoryName( staged ) ) );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}
}
