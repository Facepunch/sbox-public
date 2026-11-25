using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2.Shaders;

namespace BorealisGpuTests;

/// <summary>
/// Append buffers' hidden counters, bound under their buffer's counter name (<see cref="ShaderResource.CounterName"/>) as
/// the engine backend binds a buffer's: <c>clutter_cull_cs</c>'s pass module appends every instance it keeps to its LOD's bucket.
/// </summary>
[TestClass]
public class AppendCounterTests : GpuTest
{
	const int Instances = 100;

	/// <summary>The pass module's counters are told apart from its buffers, each named by the buffer it counts.</summary>
	[TestMethod]
	public void ModuleCountsItsAppends()
	{
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Passes.Error = e => errors.Add( e );
		using var drawer = new ShaderDrawer( backend );
		Cull( drawer, backend.Shaders.Load( "shaders/clutter_cull_cs.shader" ) );
		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
	}

	/// <summary>
	/// Cull <see cref="Instances"/> spheres with no frustum (a zero matrix keeps everything) and one LOD: those with a radius
	/// land in LOD 0's bucket, which its counter counts from the 5 it's set to; the other buckets' counters stay at 0.
	/// </summary>
	void Cull( ShaderDrawer drawer, ShaderFile shader )
	{
		if ( shader is null ) Assert.Inconclusive( "clutter_cull_cs isn't compiled" );
		var variant = drawer.Variant( shader, new ShaderAttributes() );
		var counted = variant.Resources.Where( r => r.Kind == ShaderResourceKind.Counter ).Select( r => r.Name ).Order().ToArray();
		CollectionAssert.AreEqual( new[] { "VisibleLod0", "VisibleLod1", "VisibleLod2", "VisibleLod3" }, counted, string.Join( ", ", variant.Resources ) );

		var spheres = new float[Instances * 4];
		var kept = 0;
		for ( int i = 0; i < Instances; i++ )
		{
			spheres[i * 4 + 0] = i;
			spheres[i * 4 + 3] = i % 3 == 0 ? -1 : 1;
			if ( i % 3 != 0 ) kept++;
		}

		var instances = Buffer( Instances * 64, "Instances" );
		var sphereBuffer = Buffer( Instances * 16, "Spheres" );
		var distances = Buffer( 16, "Distances" );
		Device.WriteBuffer<float>( sphereBuffer, spheres );
		Device.WriteBuffer<float>( instances, new float[Instances * 16] );
		Device.WriteBuffer<float>( distances, [0, 0, 0, 0] );

		var attributes = new ShaderAttributes();
		attributes.Set( "AllInstances", instances );
		attributes.Set( "AllInstanceSpheres", sphereBuffer );
		attributes.Set( "ClutterLodSwitchDistances", distances );
		attributes.Set( "InstanceCount", Instances );
		attributes.Set( "ClutterModelRadius", 1 );
		attributes.Set( "ClutterFrustumScale", 1 );
		attributes.Set( "ClutterLodCount", 1 );
		attributes.Set( "ClutterLodTanHalfFov", 1 );
		attributes.Set( "ClutterLodViewportWidth", 100 );
		attributes.Set( "ClutterWorldToProjection", new float[16] );

		var counters = new BufferHandle[4];
		for ( int lod = 0; lod < 4; lod++ )
		{
			attributes.Set( $"VisibleLod{lod}", Buffer( Instances * 64, $"Visible {lod}" ) );
			attributes.Set( ShaderResource.CounterName( $"VisibleLod{lod}" ), counters[lod] = Buffer( 16, $"Counter {lod}" ) );
		}

		var list = Device.BeginCommandList( QueueType.Graphics, "Cull" );
		var context = new GraphicsContext( Device, drawer, list );
		foreach ( var counter in counters ) context.FillBuffer( counter, 0, 0, 16 );
		context.FillBuffer( counters[0], 5, 0, 4 );
		Assert.IsTrue( context.Dispatch( shader, attributes, Instances, 1, 1 ) );

		var readback = Device.CreateBuffer( new BufferInfo { Size = 16, Memory = MemoryLocation.Readback, Name = "Counts" } );
		var gathered = Buffer( 16, "Gathered" );
		for ( int lod = 0; lod < 4; lod++ ) context.CopyBuffer( counters[lod], 0, gathered, lod * 4, 4 );
		context.CopyToReadback( gathered, 0, readback, 16 );
		context.Finish();
		Device.Wait( Device.Submit( list ) );

		var counts = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>( Device.Map( readback )[..16] ).ToArray();
		CollectionAssert.AreEqual( new uint[] { 5u + (uint)kept, 0, 0, 0 }, counts );
		context.Release();
		Device.Destroy( readback );
		foreach ( var buffer in made ) Device.Destroy( buffer );
		made.Clear();
	}

	readonly List<BufferHandle> made = new();

	BufferHandle Buffer( int size, string name )
	{
		var buffer = Device.CreateBuffer( new BufferInfo { Size = size, Usage = BufferUsage.Storage | BufferUsage.CopySource | BufferUsage.CopyDestination, Name = name } );
		made.Add( buffer );
		return buffer;
	}
}
