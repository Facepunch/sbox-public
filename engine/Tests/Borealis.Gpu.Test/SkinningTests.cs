using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// Skinned models on Borealis.Gpu: posed by their bones, through the vertex shader for one weight and through compute
/// skinning and the vertex cache for more, as Borealis poses them on Source 2's device.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SkinningTests : GpuTest
{
	const string Citizen = "models/citizen/citizen.vmdl";
	const string Human = "models/citizen_human/human.vmdl";
	const int Width = 192, Height = 192;

	/// <summary>
	/// The citizen read by Borealis.Source2 has its skinning: every mesh with bones has them mapped to model bones, and its
	/// many-weight meshes skin through the vertex cache.
	/// </summary>
	[TestMethod]
	public void ModelHasSkinning()
	{
		using var backend = new BackendDevice( Device );
		var mesh = backend.LoadModel( Citizen );
		if ( mesh is null ) Assert.Inconclusive( $"{Citizen} isn't built" );

		Assert.IsTrue( mesh.IsSkinned );
		Assert.IsTrue( mesh.BindPose.Length > 0 );
		var vertexCache = 0;
		for ( int m = 0; m < mesh.ModelMeshCount; m++ )
		{
			if ( mesh.SkinFor( m ) is not { } skin ) continue;
			Assert.AreEqual( skin.InverseBindPoses.Length, skin.MasterBones.Length );
			Assert.IsTrue( skin.MasterBones.All( b => b >= 0 && b < mesh.BindPose.Length ), $"mesh {m}'s bones are model bones" );
			Assert.IsTrue( skin.VertexCount > 0 );
			if ( skin.UsesVertexCache ) vertexCache++;
		}

		Assert.IsTrue( vertexCache > 0, "some meshes skin through the vertex cache" );
	}

	/// <summary>
	/// Bones move the model: the citizen with every bone raised and turned is drawn where the same citizen in its bind
	/// pose is drawn when the object itself is moved that way, and not where it stands unmoved.
	/// </summary>
	[TestMethod]
	public void BonesPoseTheModel()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );

		try
		{
			var mesh = backend.LoadModel( Citizen );
			if ( mesh is null ) Assert.Inconclusive( $"{Citizen} isn't built" );

			var moved = new Transform( new Vector3( 0, 12, 6 ), Rotation.FromYaw( 35 ) );
			var bindPose = mesh.BindPose.Select( ToTransform ).ToArray();
			var posed = bindPose.Select( x => moved.ToWorld( x ) ).ToArray();

			var still = Render( mesh, Transform.Zero, null );
			var objectMoved = Render( mesh, moved, null );
			var bonesMoved = Render( mesh, Transform.Zero, posed );

			var toObject = MeanDifference( bonesMoved, objectMoved );
			var toStill = MeanDifference( bonesMoved, still );
			Assert.IsTrue( MeanDifference( objectMoved, still ) > 2, "moving the object changes the picture" );
			Assert.IsTrue( toObject < 0.25, $"posed by bones as the moved object: mean difference {toObject:F3}" );
			Assert.IsTrue( toStill > 2, $"posed by bones, not standing still: mean difference {toStill:F3}" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Morphs move the face: the human's head with every flex controller at its maximum, composited into the morph atlas
	/// and skinned with <c>D_MORPH</c>, differs from it with none set, and with every controller at 0 it doesn't.
	/// </summary>
	[TestMethod]
	public void MorphsMoveTheFace()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );

		try
		{
			var mesh = backend.LoadModel( Human );
			if ( mesh is null ) Assert.Inconclusive( $"{Human} isn't built" );
			var model = ((BackendModel)mesh.Geometry).File;
			var controllers = model.Meshes.Where( x => x.Morphs is not null ).SelectMany( x => x.Morphs.Controllers ).DistinctBy( x => x.Name ).ToArray();
			Assert.IsTrue( controllers.Length > 0, "the human has flex controllers" );

			// The face, close up
			var head = new Vector3( 0, 0, mesh.Bounds.Mins.z + mesh.Bounds.Size.z * 0.93f );
			var none = Render( mesh, Transform.Zero, null, null, head, 0.25f );

			var zero = new BackendMorphs( backend );
			foreach ( var c in controllers ) zero.Set( c.Name, 0 );
			var unweighted = Render( mesh, Transform.Zero, null, zero, head, 0.25f );

			var full = new BackendMorphs( backend );
			foreach ( var c in controllers ) full.Set( c.Name, c.Max );
			var morphed = Render( mesh, Transform.Zero, null, full, head, 0.25f );
			Save( morphed, Width, Height, "skinning-morphed-face" );

			var toNone = MeanDifference( morphed, none );
			Assert.IsTrue( toNone > 1, $"morphs change the face: mean difference {toNone:F3}" );
			Assert.IsTrue( MeanDifference( unweighted, none ) < 0.05, "nothing weighted, nothing moves" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Skinned draws go GPU-driven (<see cref="BackendDevice.GpuDrivenSkinned"/>): the posed citizen, its many-weight meshes from
	/// the vertex cache and its one-weight ones skinned in the vertex shader, depth, shadows and forward, draws in more
	/// GPU-driven batches than with the switch off, and looks the same.
	/// </summary>
	[TestMethod]
	public void SkinnedDrawsGpuDriven()
	{
		var previousHost = RenderHost.Current;
		try
		{
			(byte[] Pixels, long Batches) Draw( bool skinned )
			{
				using var backend = new BackendDevice( Device ) { GpuDrivenSkinned = skinned, TimeDraws = true };
				RenderHost.Current = new BackendRenderHost( backend );
				var mesh = backend.LoadModel( Citizen );
				if ( mesh is null ) Assert.Inconclusive( $"{Citizen} isn't built" );

				var moved = new Transform( new Vector3( 0, 12, 6 ), Rotation.FromYaw( 35 ) );
				var posed = mesh.BindPose.Select( ToTransform ).Select( x => moved.ToWorld( x ) ).ToArray();
				Render( mesh, Transform.Zero, posed );
				var batches = backend.DrawTimes.GpuDrivenBatches;
				var pixels = Render( mesh, Transform.Zero, posed );
				return (pixels, backend.DrawTimes.GpuDrivenBatches - batches);
			}

			var classic = Draw( false );
			var gpuDriven = Draw( true );
			Save( gpuDriven.Pixels, Width, Height, "skinning-gpudriven" );

			Assert.IsTrue( gpuDriven.Batches > classic.Batches, $"skinned draws go GPU-driven: {gpuDriven.Batches} batches against {classic.Batches}" );
			var difference = MeanDifference( gpuDriven.Pixels, classic.Pixels );
			Assert.IsTrue( difference < 0.25, $"GPU-driven skinned draws look as the classic ones: mean difference {difference:F3}" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>A model-space bone (<c>matrix3x4_t</c> rows) as a transform.</summary>
	static Transform ToTransform( Matrix3x4 m )
	{
		var matrix = new System.Numerics.Matrix4x4(
			m.Row0.x, m.Row1.x, m.Row2.x, 0,
			m.Row0.y, m.Row1.y, m.Row2.y, 0,
			m.Row0.z, m.Row1.z, m.Row2.z, 0,
			m.Row0.w, m.Row1.w, m.Row2.w, 1 );
		System.Numerics.Matrix4x4.Decompose( matrix, out _, out var rotation, out var position );
		return new Transform( new Vector3( position.X, position.Y, position.Z ), new Rotation( rotation.X, rotation.Y, rotation.Z, rotation.W ) );
	}

	/// <summary>The model drawn once from a fixed camera, as 8-bit sRGB.</summary>
	byte[] Render( RenderMesh mesh, Transform transform, Transform[] bones, IMorphSource morphs = null, Vector3? focus = null, float distance = 1.4f )
	{
		var system = new RenderSystem { ParallelRecording = false };
		var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Skinning" );
		try
		{
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( -0.4f, 0.6f, -0.7f ).Normal;
			world.Lighting.AmbientColor = new Color( 0.2f, 0.2f, 0.2f, 1 );
			var obj = new MeshObject( mesh, transform ) { MorphSource = morphs };
			if ( bones is not null )
			{
				obj.SetBones( bones );
				obj.LocalBounds = new BBox( mesh.Bounds.Mins - 40, mesh.Bounds.Maxs + 40 );
			}

			world.Add( obj );

			var view = new RenderView { Name = "Skinning", FieldOfView = 60, ClearColor = new Color( 0.1f, 0.2f, 0.9f ), Viewport = new Rect( 0, 0, Width, Height ) };
			var size = mesh.Bounds.Size.Length;
			view.Rotation = Rotation.From( 20, 160, 0 );
			view.Position = (focus ?? mesh.Bounds.Center) - view.Rotation.Forward * size * distance;
			view.ZNear = size * 0.05f;
			view.ZFar = size * 10;

			Device.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
			Device.WaitIdle();

			var (texture, layout) = BackendDevice.Unwrap( target.Color );
			return ToRgba8( ReadBack( texture, layout ) );
		}
		finally
		{
			Device.WaitIdle();
			target.Dispose();
			system.Dispose();
		}
	}

	static double MeanDifference( byte[] a, byte[] b )
	{
		long total = 0;
		for ( int i = 0; i < a.Length; i++ )
		{
			if ( (i & 3) != 3 ) total += Math.Abs( a[i] - b[i] );
		}

		return total / (a.Length * 0.75);
	}

	static byte[] ToRgba8( byte[] halfs )
	{
		var pixels = new byte[Width * Height * 4];
		for ( int i = 0; i < pixels.Length; i++ )
		{
			var linear = Math.Clamp( (float)BitConverter.ToHalf( halfs, i * 2 ), 0, 1 );
			pixels[i] = (byte)(MathF.Pow( linear, 1 / 2.2f ) * 255 + 0.5f);
		}

		return pixels;
	}
}
