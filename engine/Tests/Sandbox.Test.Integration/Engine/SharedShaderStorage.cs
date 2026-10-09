using System;

namespace EngineTests;

/// <summary>Exercises shared VS/PS module loading through real material creation and feature selection.</summary>
[TestClass]
public class SharedShaderStorageTest
{
	[TestMethod]
	[DataRow( "simple", "F_ALPHA_TEST", 1 )]
	[DataRow( "generic", "F_RENDER_BACKFACES", 1 )]
	[DataRow( "skin", "F_RENDER_BACKFACES", 1 )]
	[DataRow( "complex", "F_USE_BENT_NORMALS", 1 )]
	[DataRow( "complex", "F_DETAIL_TEXTURE", 4 )]
	public void LoadOrdinaryAndSpecializedModules( string name, string feature, int value )
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" )
			Assert.Inconclusive( "Set SBOX_TEST_GRAPHICS=1 to exercise Vulkan shader creation." );

		var path = $"shaders/{name}.shader";
		var material = Material.Create( $"shared-storage-{name}-{feature}", path );
		try
		{
			Assert.IsTrue( material.IsValid );
			Assert.AreEqual( path, material.Shader.native.GetFilename(), "Shader fell back to error.shader." );
			material.SetFeature( feature, value );
			Assert.AreEqual( value, material.GetFeature( feature ) );
			material.SetFeature( feature, 0 );
			Assert.AreEqual( 0, material.GetFeature( feature ) );
		}
		finally
		{
			material.Destroy();
		}
	}
}
