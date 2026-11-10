using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using System.Linq;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>
/// Skinning on the CPU: native's transform entry layout for a skinned mesh, bones concatenated with their inverse
/// bind poses, written once a frame for every view, and the bind pose when an object has no bones.
/// </summary>
// Frames go through the engine's ShadowMapper, whose cache and frame counter are static: one test's frame
// can't run in the middle of another's
[TestClass]
[DoNotParallelize]
public class SkinningTests
{
	static void AreEqual( Vector4 expected, Vector4 actual, string message )
	{
		Assert.IsTrue( expected.Distance( actual ) < 1e-4f, $"{message}: expected {expected}, got {actual}" );
	}

	static void AreEqual( in Matrix3x4 expected, in Matrix3x4 actual, string message )
	{
		AreEqual( expected.Row0, actual.Row0, message );
		AreEqual( expected.Row1, actual.Row1, message );
		AreEqual( expected.Row2, actual.Row2, message );
	}

	[TestMethod]
	public void ConcatAppliesTheSecondFirst()
	{
		var a = new Transform( new Vector3( 1, 2, 3 ), Rotation.From( 10, 70, -20 ), 2 );
		var b = new Transform( new Vector3( -4, 5, 0.5f ), Rotation.From( -30, 15, 5 ), 0.5f );

		// Row-vector matrices: b then a is b * a
		var expected = Matrix3x4.From( Matrix.FromTransform( b ) * Matrix.FromTransform( a ) );
		AreEqual( expected, Matrix3x4.Concat( Matrix3x4.From( a ), Matrix3x4.From( b ) ), "a after b" );
	}

	static (RenderSystem System, RenderWorld World, MeshObject Object) SkinnedScene( int blendWeights, bool posed )
	{
		var world = new RenderWorld();
		var obj = new MeshObject( TestSkinnedMesh( blendWeights ), new Transform( new Vector3( 20, 0, 0 ) ) ) { Tint = new Color( 0.5f, 1, 1 ) };
		if ( posed ) obj.SetBones( [new Transform( new Vector3( 20, 1, 0 ) ), new Transform( new Vector3( 20, 0, 3 ), Rotation.From( 0, 90, 0 ) )] );
		world.Add( obj );

		var system = new RenderSystem();
		return (system, world, obj);
	}

	[TestMethod]
	public void EntriesMatchNativeLayout()
	{
		var (system, world, obj) = SkinnedScene( 4, posed: true );
		using var _ = system;
		var feature = system.GetFeature<MeshRenderFeature>();

		CollectAndPrepare( system, world, LookingDownX() );

		// A skinned run of one, whose instance reads its base entry
		var run = feature.RunsFor( system.MainPass ).Opaque.Single();
		Assert.IsTrue( run.Skinned );
		Assert.AreEqual( 1, run.Count );

		var slot = obj.SkinSlots[0];
		var transforms = system.Transforms;

		// Base: tint, alpha, blend weights, no cache block until BeforeUpload, and the "animation scale" native writes
		var first = transforms[slot];
		Assert.AreEqual( 4u, first.BlendWeightCount );
		Assert.AreEqual( uint.MaxValue, first.VertexCacheOffset );
		Assert.AreEqual( 1.0f, first.Alpha );
		Assert.AreEqual( 0x80FFFFu, first.TintRgb888 );
		AreEqual( new Vector4( 0, 0, 1, 1 ), first.Row0, "base entry" );

		// Then the morph entry, then each render bone: the model bone it follows, after its inverse bind pose
		var skin = obj.Mesh.SkinFor( 0 );
		for ( int b = 0; b < 2; b++ )
		{
			var world0 = obj.BoneMatrices[skin.MasterBones[b]];
			var expected = Matrix3x4.Concat( world0, skin.InverseBindPoses[b] );
			var entry = transforms[slot + 2 + b];
			AreEqual( expected, new Matrix3x4 { Row0 = entry.Row0, Row1 = entry.Row1, Row2 = entry.Row2 }, $"bone {b}" );
		}

		Assert.AreEqual( slot, transforms.InstanceSlot( run.FirstInstance ), "the run's instance reads the base entry" );
	}

	[TestMethod]
	public void BonesAreWrittenOnceAFrame()
	{
		var (system, world, obj) = SkinnedScene( 4, posed: true );
		using var _ = system;
		var feature = system.GetFeature<MeshRenderFeature>();
		Sandbox.Rendering.ShadowMapper.ResetCache();

		// The sun's cascades see it too
		CollectAndPrepare( system, world, LookingDownX() );

		var slot = obj.SkinSlots[0];
		var shadowRuns = system.ShadowMaps.Passes.ToArray().SelectMany( p => feature.RunsFor( p ).Opaque ).Where( r => r.Skinned ).ToArray();
		Assert.IsTrue( shadowRuns.Length > 0, "a cascade drew it" );
		Assert.AreEqual( slot + 4, system.Transforms.Count, "every view shares the one base entry and set of bones" );
		foreach ( var run in shadowRuns )
			Assert.AreEqual( slot, system.Transforms.InstanceSlot( run.FirstInstance ) );

		// Next frame they're written again
		CollectAndPrepare( system, world, LookingDownX() );
		Assert.AreEqual( 4, system.Transforms.Count );
	}

	[TestMethod]
	public void UnposedObjectsTakeTheBindPose()
	{
		var (system, world, obj) = SkinnedScene( 1, posed: false );
		using var _ = system;

		CollectAndPrepare( system, world, LookingDownX() );

		var skin = obj.Mesh.SkinFor( 0 );
		var objectMatrix = Matrix3x4.From( obj.Transform );
		for ( int b = 0; b < 2; b++ )
		{
			var bind = Matrix3x4.Concat( objectMatrix, obj.Mesh.BindPose[skin.MasterBones[b]] );
			var entry = system.Transforms[obj.SkinSlots[0] + 2 + b];
			AreEqual( Matrix3x4.Concat( bind, skin.InverseBindPoses[b] ), new Matrix3x4 { Row0 = entry.Row0, Row1 = entry.Row1, Row2 = entry.Row2 }, $"bone {b}" );
		}

		// One weight per vertex is skinned in the vertex shader, not the cache. It instances like the rest: the shader finds
		// the bones after the base entry its instance reads
		Assert.IsFalse( skin.UsesVertexCache );
		Assert.AreEqual( 1u, system.Transforms[obj.SkinSlots[0]].BlendWeightCount );
		var run = system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass ).Opaque.Single();
		Assert.IsTrue( run.Skinned );
		Assert.AreEqual( obj.SkinSlots[0], system.Transforms.InstanceSlot( run.FirstInstance ) );
	}

	/// <summary>
	/// Skinned objects of the same mesh instance together whatever skins them: each instance reads its own base entry,
	/// so its own bones and vertex cache block.
	/// </summary>
	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 4 )]
	public void SkinnedObjectsInstanceTogether( int blendWeights )
	{
		var mesh = TestSkinnedMesh( blendWeights );
		var world = new RenderWorld();
		var a = new MeshObject( mesh, new Transform( new Vector3( 20, 0, 0 ) ) );
		var b = new MeshObject( mesh, new Transform( new Vector3( 20, 5, 0 ) ) );
		world.Add( a );
		world.Add( b );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		var run = system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass ).Opaque.Single();
		Assert.IsTrue( run.Skinned );
		Assert.AreEqual( 2, run.Count );
		int[] read = [system.Transforms.InstanceSlot( run.FirstInstance ), system.Transforms.InstanceSlot( run.FirstInstance + 1 )];
		CollectionAssert.AreEquivalent( new[] { a.SkinSlots[0], b.SkinSlots[0] }, read, "each instance reads its own base entry" );
	}
}
