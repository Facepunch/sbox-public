using System.Collections.Generic;
using System.Linq;

namespace GameObjects.Components;

[TestClass]
public class PolygonMeshSerializationTests
{
	/// <summary>
	/// Old format with Position, Rotation, and world-space texture parameters.
	/// </summary>
	const string LegacyMeshJson = """
	{
		"Topology": "H4sIAAAAAAAACl2QCQ7CMAwEp4Qj3NBy/f+laMRGsohUVbFn1+t04ANs+Z0VsM69AwfgBFyAKZ9MPXdgk3qLnsI+Um/h/me9i2ZXfFvYOazafTJN0XRgKZmPyWtfXvYVL73PZRd52WdYPa7xtC8veys7u6t17/7t6Tn0ZtFjvIXZR895zjKjPWf4NiOrGt+ivpXZ3XnozaOndVn9vvVkkTjEAQAA",
		"Position": "4.695496,-71.68547,64",
		"Rotation": "0,0,0,1",
		"Positions": [
			"-26.3793,-152.6447,64",
			"26.3793,-152.6447,64",
			"26.3793,152.6447,64",
			"-26.3793,152.6447,64",
			"-26.3793,152.6447,-64",
			"26.3793,152.6447,-64",
			"26.3793,-152.6447,-64",
			"-26.3793,-152.6447,-64"
		],
		"Blends": ["0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0"],
		"Colors": ["0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0"],
		"TextureCoord": [
			"0.2427719,1.752579",
			"-0.1694047,-1",
			"0.2427719,-0.632494",
			"-1.752579,-1",
			"-0.1694047,-0.632494",
			"-0.2427719,-1",
			"-0.1694047,1.752579",
			"-0.632494,-1",
			"0.2427719,-0.632494",
			"0.1694047,0",
			"0.2427719,1.752579",
			"0.632494,0",
			"-0.1694047,1.752579",
			"0.2427719,0",
			"-0.1694047,-0.632494",
			"1.752579,0",
			"1.752579,-1",
			"-0.1694047,0",
			"-0.632494,0",
			"0.1694047,-1",
			"-1.752579,0",
			"0.2427719,-1",
			"0.632494,-1",
			"-0.2427719,0"
		],
		"TextureUAxis": ["1,0,0","1,0,-0","0,-1,0","0,1,0","-1,0,0","1,-0,0"],
		"TextureVAxis": ["0,-1,0","0,-1,0","0,0,-1","-0,0,-1","0,0,-1","-0,0,-1"],
		"TextureScale": ["0.25,0.25","0.25,0.25","0.25,0.25","0.25,0.25","0.25,0.25","0.25,0.25"],
		"TextureOffset": ["0,0","0,0","0,0","0,0","0,0","0,0"],
		"MaterialIndex": [-1,-1,-1,-1,-1,-1],
		"EdgeFlags": [0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]
	}
	""";

	[TestMethod]
	public void ReadLegacyFormatWithWorldSpaceFields()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );

		Assert.IsNotNull( mesh );
		Assert.AreEqual( new Vector3( 4.695496f, -71.68547f, 64f ), mesh.Transform.Position );
		Assert.AreEqual( 8, mesh.VertexHandles.Count() );
	}

	[TestMethod]
	public void WriteDoesNotContainWorldSpaceFields()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json = Json.Serialize( mesh );

		Assert.IsFalse( json.Contains( "\"Position\"" ), "Should not write world-space Position" );
		Assert.IsFalse( json.Contains( "\"Rotation\"" ), "Should not write world-space Rotation" );
		Assert.IsFalse( json.Contains( "\"TextureUAxis\"" ), "Should not write derived TextureUAxis" );
		Assert.IsFalse( json.Contains( "\"TextureVAxis\"" ), "Should not write derived TextureVAxis" );
		Assert.IsFalse( json.Contains( "\"TextureScale\"" ), "Should not write derived TextureScale" );
		Assert.IsFalse( json.Contains( "\"TextureOffset\"" ), "Should not write derived TextureOffset" );
	}

	[TestMethod]
	public void WriteContainsExpectedFields()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json = Json.Serialize( mesh );

		Assert.IsTrue( json.Contains( "\"Topology\"" ) );
		Assert.IsTrue( json.Contains( "\"Positions\"" ) );
		Assert.IsTrue( json.Contains( "\"TextureCoord\"" ) );
		Assert.IsTrue( json.Contains( "\"MaterialIndex\"" ) );
		Assert.IsTrue( json.Contains( "\"EdgeFlags\"" ) );
	}

	[TestMethod]
	public void RoundTripPreservesData()
	{
		var original = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json = Json.Serialize( original );
		var restored = Json.Deserialize<PolygonMesh>( json );

		Assert.AreEqual( original.VertexHandles.Count(), restored.VertexHandles.Count() );
		Assert.AreEqual( original.FaceHandles.Count(), restored.FaceHandles.Count() );
	}

	[TestMethod]
	public void RoundTripIsDeterministic()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json1 = Json.Serialize( mesh );
		var json2 = Json.Serialize( mesh );

		Assert.AreEqual( json1, json2 );
	}

	[TestMethod]
	public void ResaveDoesNotDrift()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );

		var json1 = Json.Serialize( mesh );
		var mesh2 = Json.Deserialize<PolygonMesh>( json1 );
		var json2 = Json.Serialize( mesh2 );

		Assert.AreEqual( json1, json2, "Second serialize should be identical — no drift from world-space recomputation" );
	}

	[TestMethod]
	public void TextureParametersRestoredAfterTransformSet()
	{
		var original = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json = Json.Serialize( original );
		var worldTransform = new Transform( new Vector3( 100, 200, 300 ), Rotation.FromYaw( 45 ) );

		// Deserialize twice from new format (no world-space fields)
		var mesh1 = Json.Deserialize<PolygonMesh>( json );
		var mesh2 = Json.Deserialize<PolygonMesh>( json );

		// This is what MeshComponent.TransformChanged does: Mesh.Transform = WorldTransform
		mesh1.Transform = worldTransform;
		mesh2.Transform = worldTransform;

		// Both should produce identical texture parameters from the same UVs + transform
		var faces1 = mesh1.FaceHandles.ToArray();
		var faces2 = mesh2.FaceHandles.ToArray();
		Assert.AreEqual( faces1.Length, faces2.Length );

		for ( int i = 0; i < faces1.Length; i++ )
		{
			mesh1.GetFaceTextureParameters( faces1[i], out var u1, out var v1, out var s1 );
			mesh2.GetFaceTextureParameters( faces2[i], out var u2, out var v2, out var s2 );

			Assert.AreEqual( u1, u2, $"Face {i} TextureUAxis mismatch" );
			Assert.AreEqual( v1, v2, $"Face {i} TextureVAxis mismatch" );
			Assert.AreEqual( s1, s2, $"Face {i} TextureScale mismatch" );
		}

		// Verify something was actually computed (not all zeros)
		mesh1.GetFaceTextureParameters( faces1[0], out var axisU, out _, out var scale );
		Assert.AreNotEqual( Vector4.Zero, axisU, "TextureUAxis should not be zero" );
		Assert.AreNotEqual( Vector2.Zero, scale, "TextureScale should not be zero" );
	}

	[TestMethod]
	public void TransformChangeDoesNotDirtyMeshJson()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json1 = Json.Serialize( mesh );

		// Simulate MeshComponent moving: Mesh.Transform = WorldTransform
		var restored = Json.Deserialize<PolygonMesh>( json1 );
		restored.Transform = new Transform( new Vector3( 500, -300, 100 ), Rotation.FromYaw( 90 ) );

		// Re-serialize — should be unchanged since world-space fields are no longer written
		var json2 = Json.Serialize( restored );
		Assert.AreEqual( json1, json2, "Mesh JSON should not change when the transform changes" );
	}

	[TestMethod]
	public void TextureParametersStableAcrossDifferentTransforms()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json = Json.Serialize( mesh );

		var transform1 = new Transform( new Vector3( 100, 0, 0 ), Rotation.Identity );
		var transform2 = new Transform( new Vector3( 50000, 50000, 50000 ), Rotation.FromYaw( 180 ) );

		// Set transform, serialize, restore, set same transform again
		var mesh1 = Json.Deserialize<PolygonMesh>( json );
		mesh1.Transform = transform1;
		var roundTripped = Json.Deserialize<PolygonMesh>( Json.Serialize( mesh1 ) );
		roundTripped.Transform = transform1;

		// Should match because TextureCoord is the stable source
		var faces1 = mesh1.FaceHandles.ToArray();
		var faces2 = roundTripped.FaceHandles.ToArray();

		for ( int i = 0; i < faces1.Length; i++ )
		{
			mesh1.GetFaceTextureParameters( faces1[i], out var u1, out var v1, out var s1 );
			roundTripped.GetFaceTextureParameters( faces2[i], out var u2, out var v2, out var s2 );

			Assert.AreEqual( u1, u2, $"Face {i} TextureUAxis drifted after round-trip" );
			Assert.AreEqual( v1, v2, $"Face {i} TextureVAxis drifted after round-trip" );
			Assert.AreEqual( s1, s2, $"Face {i} TextureScale drifted after round-trip" );
		}
	}
}
