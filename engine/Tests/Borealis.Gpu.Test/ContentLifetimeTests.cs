using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2.Models;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// What a map loads goes when it's unloaded: a material's texture borrowed outside the worlds doesn't keep the material
/// (a map's sky, first read by a water body's reflection before the map's world held it, stayed: 64 MiB a cube), and a
/// model made at run time goes once no world holds it (a game's procedural model held a vertex page and a 56 MiB index page).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ContentLifetimeTests : GpuTest
{
	[TestMethod]
	public void ABorrowedMaterialGoesWithTheWorldThatHeldIt()
	{
		const string sky = "materials/skybox/skybox_day_01.vmat";
		using var backend = new BackendDevice( Device );
		if ( backend.LoadedMaterialTexture( sky, "g_tSkyTexture" ) is null ) Assert.Inconclusive( "No compiled sky material" );
		var materials = backend.MaterialsFreed;
		var textures = backend.TexturesFreed;

		// A world holds it, then goes
		backend.ModelMaterial( sky );
		backend.ReleaseModelMaterial( sky );
		Assert.AreEqual( materials + 1, backend.MaterialsFreed, "the borrowed material went with the world" );
		Assert.IsTrue( backend.TexturesFreed > textures, "and its sky texture with it" );
		Device.WaitIdle();
	}

	/// <summary>
	/// A texture a material drawn as a surface names but its surface doesn't read (a water material's caustics, which only
	/// the CPU reads: a WaterBody projects them with a LightModulator) is still the material's texture, borrowed with it
	/// and gone with it. It used to be unreadable, and the caustics drew transparent.
	/// </summary>
	[TestMethod]
	public void ASurfaceMaterialsUnreadTextureIsBorrowedWithIt()
	{
		const string water = "materials/water/dark.water.vmat";
		using var backend = new BackendDevice( Device );
		if ( backend.Content.TryRead( water ) is null ) Assert.Inconclusive( "No compiled water material" );
		var caustics = backend.LoadedMaterialTexture( water, "g_tCaustics" );
		Assert.IsNotNull( caustics, "the caustics texture" );
		Assert.IsTrue( caustics is BackendTexture { } loaded && Device.InfoOf( loaded.Handle ).Width > 1, "the file's texture, not a placeholder" );
		if ( backend.HeldMaterial( water ) is not ConvertedMaterial ) Assert.Inconclusive( "Water isn't drawn as a surface here" );
		var textures = backend.TexturesFreed;

		// A world holds it, then goes
		backend.ModelMaterial( water );
		backend.ReleaseModelMaterial( water );
		backend.ReleaseModelMaterial( water );
		Assert.IsTrue( backend.TexturesFreed > textures, "the caustics went with the material" );
		Device.WaitIdle();
	}

	[TestMethod]
	public void ARunTimeModelNoWorldHoldsIsFreed()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		if ( backend.Content.TryRead( "models/dev/box.vmdl" ) is not { } data ) { Assert.Inconclusive( "models/dev/box.vmdl isn't built" ); return; }
		var mesh = BackendModel.Create( backend, ModelFile.Read( data, backend.Content.TryRead ) );
		var pages = backend.GpuDrivenTables.Bytes.Pages;
		Assert.IsTrue( pages > 0, "it's in the pages" );

		var grace = BackendDevice.ModelGraceSeconds;
		BackendDevice.ModelGraceSeconds = 0;
		try
		{
			// A world draws it, then goes: it's freed at the next frame's start, its pages with it
			backend.TrackModel( mesh );
			backend.HoldModel( mesh );
			backend.BeginFrame();
			Assert.IsFalse( ((BackendModel)mesh.Geometry).Freed, "held, it stays" );
			backend.ReleaseModel( mesh );
			backend.BeginFrame();
			Assert.IsTrue( ((BackendModel)mesh.Geometry).Freed, "let go, it's freed" );
			Assert.AreEqual( 1, backend.ModelsFreed );
			Assert.AreEqual( 0L, backend.GpuDrivenTables.Bytes.Pages, "and its pages, the ones being filled too" );
		}
		finally
		{
			BackendDevice.ModelGraceSeconds = grace;
		}

		Device.WaitIdle();
	}

	/// <summary>
	/// A model's bytes with its first mesh's buffers (its <c>MBUF</c> block) empty: one vertex and one index buffer of
	/// 0 x 0 bytes, as an empty run-time mesh saves them (a voxel chunk with no faces).
	/// </summary>
	static byte[] WithEmptyBuffers( byte[] model )
	{
		var tableStart = 8 + BitConverter.ToInt32( model, 8 );
		var blocks = BitConverter.ToInt32( model, 12 );
		for ( int i = 0; i < blocks; i++ )
		{
			var entry = tableStart + i * 12;
			if ( System.Text.Encoding.ASCII.GetString( model, entry, 4 ) != "MBUF" ) continue;

			// VBIBBlockHeader_t: the vertex then index buffer arrays (offset from the field, count), then each buffer's
			// RenderMeshBufferData_t: element count and size 0, no layout fields, no data
			var empty = new byte[64];
			BitConverter.TryWriteBytes( empty.AsSpan( 0 ), 16 );
			BitConverter.TryWriteBytes( empty.AsSpan( 4 ), 1 );
			BitConverter.TryWriteBytes( empty.AsSpan( 8 ), 32 );
			BitConverter.TryWriteBytes( empty.AsSpan( 12 ), 1 );

			// Appended, and the block's entry pointed at it
			var bytes = new byte[model.Length + empty.Length];
			model.CopyTo( bytes, 0 );
			empty.CopyTo( bytes, model.Length );
			BitConverter.TryWriteBytes( bytes.AsSpan( entry + 4 ), model.Length - (entry + 4) );
			BitConverter.TryWriteBytes( bytes.AsSpan( entry + 8 ), empty.Length );
			return bytes;
		}

		throw new InvalidOperationException( "The model has no embedded mesh buffers" );
	}

	/// <summary>
	/// An empty run-time mesh (its buffers 0 x 0) reads as a mesh that draws nothing, as native has it: made, made again
	/// when it changes, and drawn, with no warning, exception or validation error.
	/// </summary>
	[TestMethod]
	public void AnEmptyRunTimeMeshDrawsNothing()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		if ( backend.Content.TryRead( "models/dev/box.vmdl" ) is not { } data ) { Assert.Inconclusive( "models/dev/box.vmdl isn't built" ); return; }
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		var warnings = new List<string>();
		backend.Warn = warnings.Add;
		try
		{
			var file = ModelFile.Read( WithEmptyBuffers( data ), backend.Content.TryRead );
			var buffers = file.Meshes[0].VertexBuffers.Single();
			Assert.AreEqual( 0, buffers.Count, "an empty vertex buffer" );
			Assert.AreEqual( 0, file.Meshes[0].IndexBuffers.Single().Count, "and index buffer" );
			Assert.IsTrue( file.Meshes[0].DrawCalls.Count > 0 && file.Meshes[0].DrawCalls.All( d => d.IndexCount == 0 && d.VertexCount == 0 ), "its draws draw nothing" );

			using var drawn = new DrawnModel( Device );
			for ( int version = 0; version < 2; version++ )
			{
				// Made, drawn, and made again as a changed run-time model is
				var mesh = BackendModel.Create( backend, ModelFile.Read( WithEmptyBuffers( data ), backend.Content.TryRead ), meshGroups: ulong.MaxValue, drawFastPath: true );
				Assert.AreEqual( 0, Enumerable.Range( 0, mesh.LodCount ).Sum( lod => mesh.DrawsForLod( lod ).Length ), "no draws" );
				drawn.Render( mesh, wait: true );
				drawn.Render( mesh, wait: true );
				((BackendModel)mesh.Geometry).Free();
			}

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.AreEqual( 0, warnings.Count, string.Join( "\n", warnings ) );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	const string Citizen = "models/citizen/citizen.vmdl";

	/// <summary>
	/// A skinned model freed while a world still draws it draws nothing from then on: its compute skinning and draws don't bind
	/// its destroyed buffers (the menu's dressed citizen skinned its clothes from freed buffers after a game load failed),
	/// and a frame still in flight with it finishes before they go.
	/// </summary>
	[TestMethod]
	public void AFreedSkinnedModelStillDrawnBindsNothingFreed()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var drawn = new DrawnModel( Device );
			var mesh = backend.LoadModel( Citizen );
			if ( mesh is null ) Assert.Inconclusive( $"{Citizen} isn't built" );

			// Drawn twice, so its skinning's binding sets are cached; the second frame still in flight when it's freed
			drawn.Render( mesh, wait: true );
			drawn.Render( mesh, wait: false );
			((BackendModel)mesh.Geometry).Free();

			for ( int i = 0; i < 3; i++ ) drawn.Render( mesh, wait: true );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A model something still draws after its last holder let it go isn't freed: it's kept, and reported, until a grace
	/// period passes with nothing drawing it.
	/// </summary>
	[TestMethod]
	public void AModelDrawnAfterItsLastHolderLetGoIsKept()
	{
		using var backend = new BackendDevice( Device );
		if ( backend.Content.TryRead( Citizen ) is not { } data ) { Assert.Inconclusive( $"{Citizen} isn't built" ); return; }
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		var warnings = new List<string>();
		backend.Warn = warnings.Add;
		var grace = BackendDevice.ModelGraceSeconds;
		try
		{
			using var drawn = new DrawnModel( Device );
			var mesh = BackendModel.Create( backend, ModelFile.Read( data, backend.Content.TryRead ) );
			var model = (BackendModel)mesh.Geometry;
			backend.TrackModel( mesh );
			backend.HoldModel( mesh );
			drawn.Render( mesh, wait: true );

			// Let go, and drawn again after: due, but kept
			backend.ReleaseModel( mesh );
			drawn.Render( mesh, wait: true );
			BackendDevice.ModelGraceSeconds = 0;
			backend.BeginFrame();
			Assert.IsFalse( model.Freed, "drawn after its last holder let go, it stays" );
			Assert.AreEqual( 1, backend.ModelsDrawnUnheld );
			Assert.AreEqual( 1, warnings.Count, "and it's reported" );
			Assert.IsTrue( warnings[0].Contains( "citizen" ), warnings[0] );

			// Not drawn since: freed
			backend.BeginFrame();
			Assert.IsTrue( model.Freed, "nothing draws it, it's freed" );
			Assert.AreEqual( 1, backend.ModelsFreed );

			// Still drawn after that: nothing
			drawn.Render( mesh, wait: true );
		}
		finally
		{
			BackendDevice.ModelGraceSeconds = grace;
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A material let go of while a model still draws with it (its textures destroyed with it) binds defaults for them from
	/// then on, not the destroyed images its kept binding sets named.
	/// </summary>
	[TestMethod]
	public void AReleasedMaterialStillDrawnBindsNoDestroyedTexture()
	{
		using var backend = new BackendDevice( Device );
		if ( backend.Content.TryRead( Citizen ) is not { } data ) { Assert.Inconclusive( $"{Citizen} isn't built" ); return; }
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var drawn = new DrawnModel( Device );
			var file = ModelFile.Read( data, backend.Content.TryRead );
			var mesh = BackendModel.Create( backend, file );
			drawn.Render( mesh, wait: true );
			drawn.Render( mesh, wait: true );

			// Every hold its draws took (BackendModel.Create holds a material per draw), let go: its textures go
			var textures = backend.TexturesFreed;
			for ( int lod = 0; lod < file.LodCount; lod++ )
			{
				for ( int m = 0; m < file.Meshes.Count; m++ )
				{
					if ( !file.DrawsAtLod( m, lod ) || (file.Meshes[m].MeshGroupMask & file.DefaultMeshGroupMask) == 0 ) continue;
					foreach ( var call in file.Meshes[m].DrawCalls ) backend.ReleaseModelMaterial( file.MaterialFor( call.Material, 0 ) );
				}
			}

			Assert.IsTrue( backend.TexturesFreed > textures, "its textures were destroyed" );
			for ( int i = 0; i < 2; i++ ) drawn.Render( mesh, wait: true );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A buffer an object's attributes name stops binding once it's destroyed: disposed, or for a borrowed one (an engine
	/// buffer), destroyed by its owner.
	/// </summary>
	[TestMethod]
	public void ADestroyedAttributeBufferIsNotLive()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		var owned = new Borealis.GpuBuffer( 16, 16, Borealis.GpuBuffer.UsageFlags.Structured, "Owned" );
		RenderHost.Current = previousHost;
		var handle = Device.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage, Name = "Engine buffer" } );
		var borrowed = backend.BorrowBuffer( handle, 16, 16, "Borrowed" );
		var (ownedBacking, borrowedBacking) = ((BackendBuffer)owned.Resource, (BackendBuffer)borrowed.Resource);
		Assert.IsTrue( ownedBacking.Live && borrowedBacking.Live );

		owned.Dispose();
		Device.Destroy( handle );
		Assert.IsFalse( ownedBacking.Live, "disposed" );
		Assert.IsFalse( borrowedBacking.Live, "destroyed by its owner" );
		Device.WaitIdle();
	}

	/// <summary>A model drawn posed in a world of its own, one frame a call.</summary>
	sealed class DrawnModel( Borealis.Gpu.Device device ) : IDisposable
	{
		readonly RenderSystem system = new() { ParallelRecording = false };
		readonly ViewTarget target = ViewTarget.Create( new Vector2Int( 128, 128 ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Freed model" );

		public void Render( RenderMesh mesh, bool wait )
		{
			var world = new RenderWorld();
			var obj = new MeshObject( mesh, Transform.Zero );
			obj.SetBones( mesh.BindPose.Select( Bone ).ToArray() );
			obj.LocalBounds = new BBox( mesh.Bounds.Mins - 40, mesh.Bounds.Maxs + 40 );
			world.Add( obj );

			var view = new RenderView { Name = "Freed model", FieldOfView = 60, Viewport = new Rect( 0, 0, 128, 128 ) };
			var size = mesh.Bounds.Size.Length;
			view.Rotation = Rotation.From( 20, 160, 0 );
			view.Position = mesh.Bounds.Center - view.Rotation.Forward * size * 1.4f;
			view.ZNear = size * 0.05f;
			view.ZFar = size * 10;

			device.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
			if ( wait ) device.WaitIdle();
		}

		static Transform Bone( Matrix3x4 m )
		{
			var matrix = new System.Numerics.Matrix4x4( m.Row0.x, m.Row1.x, m.Row2.x, 0, m.Row0.y, m.Row1.y, m.Row2.y, 0, m.Row0.z, m.Row1.z, m.Row2.z, 0, m.Row0.w, m.Row1.w, m.Row2.w, 1 );
			System.Numerics.Matrix4x4.Decompose( matrix, out _, out var rotation, out var position );
			return new Transform( new Vector3( position.X, position.Y, position.Z ), new Rotation( rotation.X, rotation.Y, rotation.Z, rotation.W ) );
		}

		public void Dispose()
		{
			device.WaitIdle();
			target.Dispose();
			system.Dispose();
		}
	}
}
