using Sandbox.SceneRenderer;
using System;

namespace SceneRendererTests;

/// <summary>
/// What the scene renderer tests build their worlds and views from.
/// </summary>
internal static class TestScenes
{
	internal sealed class TestObject : RenderObject
	{
		public TestObject( BBox bounds, Transform transform )
		{
			LocalBounds = bounds;
			Transform = transform;
		}
	}

	internal static readonly BBox UnitBox = new( new Vector3( -1 ), new Vector3( 1 ) );

	/// <summary>
	/// A unit box mesh with one draw per entry, translucent where the entry is true. It has no model, so it can
	/// be culled and prepared but not drawn.
	/// </summary>
	internal static RenderMesh TestMesh( params bool[] translucent )
	{
		var draws = new RenderMesh.Draw[translucent.Length];
		for ( int i = 0; i < draws.Length; i++ )
			draws[i] = new RenderMesh.Draw( 0, i, 0, 36, 24, 0, null, Translucent: translucent[i] );

		return new RenderMesh( UnitBox, [draws] );
	}

	/// <summary>
	/// A one-draw unit box mesh skinned to two bones: render bone 0 follows model bone 1 and bone 1 follows model
	/// bone 0, each inverse bind pose a translation, and <paramref name="blendWeights"/> weights per vertex.
	/// </summary>
	internal static RenderMesh TestSkinnedMesh( int blendWeights )
	{
		var mesh = TestMesh( false );
		var skin = new RenderMesh.MeshSkin
		{
			BlendWeightCount = blendWeights,
			VertexCount = 24,
			InverseBindPoses = [Matrix3x4.From( new Transform( new Vector3( 0, 0, -10 ) ) ), Matrix3x4.From( new Transform( new Vector3( -5, 0, 0 ) ) )],
			MasterBones = [1, 0],
		};

		Matrix3x4[] bindPose = [Matrix3x4.From( new Transform( new Vector3( 5, 0, 0 ) ) ), Matrix3x4.From( new Transform( new Vector3( 0, 0, 10 ) ) )];
		mesh.SetSkinning( [skin], bindPose );
		return mesh;
	}

	internal static RenderView LookingDownX()
	{
		var view = new RenderView
		{
			Position = Vector3.Zero,
			Rotation = Rotation.Identity,
			FieldOfView = 90,
			ZNear = 1,
			ZFar = 1000,
			Viewport = new Rect( 0, 0, 1000, 1000 ),
		};
		view.Update();
		return view;
	}

	internal static Vector4 Project( RenderView view, Vector3 point )
	{
		var clip = view.WorldToProjection.Transform( new Vector4( point, 1 ) );
		return clip / clip.w;
	}

	/// <summary>
	/// A frame's culling and preparing. The frame counter goes up first, as the engine loop's does - the
	/// shadow cache and time slicing count frames.
	/// </summary>
	internal static void CollectAndPrepare( RenderSystem system, RenderWorld world, RenderView view )
	{
		Application.FrameCount++;
		var stats = new RenderStats();
		system.Collect( world, view, ref stats );
		system.Prepare( world, view );
	}
}
