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
		Material ordinary = null;
		Material copy = null;
		SceneWorld world = null;
		try
		{
			Assert.IsTrue( material.IsValid );
			Assert.AreEqual( path, material.Shader.native.GetFilename(), "Shader fell back to error.shader." );
			material.SetFeature( feature, value );
			Assert.AreEqual( value, material.GetFeature( feature ) );
			material.SetFeature( feature, 0 );
			Assert.AreEqual( 0, material.GetFeature( feature ) );

			Assert.IsTrue( material.Flags.GetBool( "NeedsLightProbe" ) );
			Assert.IsTrue( material.Flags.GetBool( "UsesBakedLightingFromProbe", true ) );
			ordinary = material.CreateCopy();
			material.Attributes.Set( "UsesBakedLightingFromProbe", false );
			material.SetFeature( feature, value );
			Assert.IsFalse( material.Flags.GetBool( "UsesBakedLightingFromProbe" ) );
			copy = material.CreateCopy();
			copy.SetFeature( feature, 0 );
			Assert.IsFalse( copy.Flags.GetBool( "UsesBakedLightingFromProbe" ) );

			var mesh = new Mesh( material );
			mesh.CreateVertexBuffer( 3, new Vertex[]
			{
				new() { Position = Vector3.Zero, Normal = Vector3.Up },
				new() { Position = Vector3.Forward * 32, Normal = Vector3.Up },
				new() { Position = Vector3.Right * 32, Normal = Vector3.Up }
			} );
			mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
			mesh.Bounds = new BBox( new Vector3( -32 ), new Vector3( 32 ) );
			mesh.AddSubMesh( copy, 0, 3, 0, 3 );
			var model = Model.Builder.AddMesh( mesh ).Create();
			world = new SceneWorld();
			var obj = new SceneObject( world, model );
			Assert.IsFalse( obj.Flags.NeedsLightProbe, "Materials that disable baked probes must not request a lookup." );
			obj.SetMaterialOverride( ordinary );
			Assert.IsTrue( obj.Flags.NeedsLightProbe, "Materials with no explicit probe setting must retain the original behavior." );
			obj.SetMaterialOverride( null );
			Assert.IsFalse( obj.Flags.NeedsLightProbe );

			material.Attributes.Set( "UsesBakedLightingFromProbe", true );
			obj.UpdateFlagsBasedOnMaterial();
			Assert.IsTrue( obj.Flags.NeedsLightProbe, "A mixed-material model must retain probes for its lit material." );
			obj.SetMaterialOverride( copy );
			Assert.IsFalse( obj.Flags.NeedsLightProbe );
			obj.SetMaterialOverride( null );
			Assert.IsTrue( obj.Flags.NeedsLightProbe );
		}
		finally
		{
			world?.Delete();
			ordinary?.Destroy();
			copy?.Destroy();
			material.Destroy();
		}
	}
}
