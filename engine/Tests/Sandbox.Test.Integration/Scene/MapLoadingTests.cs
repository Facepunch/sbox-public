using System.Linq;
using System.Reflection;

namespace SceneTests;

/// <summary>
/// Maps, aggregates and skies with no native world renderer, aggregate or sky behind them: a map loads from its compiled
/// world in a process with no Borealis.Gpu device (a server, these tests), traces hit it, and a scene-compiled aggregate
/// and a sky managed code makes keep what they draw in managed code.
/// </summary>
[TestClass]
public class MapLoadingTests
{
	static readonly System.Type RenderType = System.Type.GetType( "Sandbox.Rendering.Bridge.WorldRender, Sandbox.Rendering", true );
	const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	[TestMethod]
	public void MapLoadsWithoutADevice()
	{
		Assert.IsNotInstanceOfType<BorealisGpuRenderBackend>( RenderBackend.Default, "headless" );

		var world = new SceneWorld();
		try
		{
			var map = new SceneMap( world, "maps/dev/preview_flat.vmap" );
			Assert.IsTrue( map.IsValid, "the map loads" );
			Assert.AreEqual( "dev/preview_flat", map.MapName );
			Assert.IsTrue( map.Bounds.Size.x > 0 && map.Bounds.Size.y > 0, $"its bounds: {map.Bounds}" );

			// Its world geometry is traced from its compiled models
			var center = map.Bounds.Center;
			var hit = world.Trace.Ray( center.WithZ( map.Bounds.Maxs.z + 100 ), center.WithZ( map.Bounds.Mins.z - 100 ) ).Run();
			Assert.IsTrue( hit.Hit, "a trace down through the middle hits the map" );

			map.Delete();
			Assert.IsFalse( map.IsValid, "deleted" );
			Assert.IsFalse( world.Trace.Ray( center.WithZ( map.Bounds.Maxs.z + 100 ), center.WithZ( -10000 ) ).Run().Hit, "nothing left to hit" );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// A scene trace with render meshes that hits a map's world geometry, which has no scene object behind it (the editor's
	/// click, a tool's placement trace): the map's tags, no game object.
	/// </summary>
	[TestMethod]
	public void SceneTraceHitsMapGeometry()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var map = new SceneMap( scene.SceneWorld, "maps/dev/preview_flat.vmap" );
		try
		{
			Assert.IsTrue( map.IsValid );
			var center = map.Bounds.Center;
			// UseRenderMeshes( true ), which only the editor may ask for: front faces
			var trace = scene.Trace.Ray( center.WithZ( map.Bounds.Maxs.z + 100 ), center.WithZ( map.Bounds.Mins.z - 100 ) ).UsePhysicsWorld( false );
			trace.IncludeRenderMeshes = true;
			trace.CullMode = 2;
			var hit = trace.Run();
			Assert.IsTrue( hit.Hit, "hits the map" );
			Assert.IsNull( hit.GameObject );
			Assert.IsTrue( hit.HasTag( "world" ) && hit.HasTag( "map" ) && hit.HasTag( "solid" ), "the map's tags" );
		}
		finally
		{
			map.Delete();
			scene.Destroy();
		}
	}

	/// <summary>
	/// Map traces off the main thread (a DDGI bake relocates its probes in parallel while the main thread waits) hit the map
	/// without loading anything there, and carry the material once the main thread has it.
	/// </summary>
	[TestMethod]
	public void MapTracesOffTheMainThread()
	{
		var world = new SceneWorld();
		try
		{
			var map = new SceneMap( world, "maps/dev/preview_flat.vmap" );
			Assert.IsTrue( map.IsValid );
			var center = map.Bounds.Center;
			var from = center.WithZ( map.Bounds.Maxs.z + 100 );
			var to = center.WithZ( map.Bounds.Mins.z - 100 );

			// The map's trace is built once however many threads ask for it first
			var traces = new object[16];
			System.Threading.Tasks.Task.Run( () => System.Threading.Tasks.Parallel.For( 0, traces.Length, i => traces[i] = map.Geometry.Trace() ) ).Wait();
			Assert.IsTrue( traces.All( x => ReferenceEquals( x, traces[0] ) ), "one trace for every thread" );

			var hits = new Sandbox.Engine.Utility.RayTrace.MeshTraceRequest.Result[16];
			System.Threading.Tasks.Task.Run( () => System.Threading.Tasks.Parallel.For( 0, hits.Length, i =>
				hits[i] = world.Trace.Ray( from + new Vector3( i, 0, 0 ), to + new Vector3( i, 0, 0 ) ).Run() ) ).Wait();
			Assert.IsTrue( hits.All( x => x.Hit ), "every worker's trace hits the map" );

			var onMain = world.Trace.Ray( from, to ).Run();
			Assert.IsTrue( onMain.Hit );
			Assert.IsNotNull( onMain.Material, "the main thread loads the hit's material" );

			var later = System.Threading.Tasks.Task.Run( () => world.Trace.Ray( from, to ).Run() ).Result;
			Assert.AreSame( onMain.Material, later.Material, "a worker's trace carries it once loaded" );
			map.Delete();
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void MissingMapIsInvalid()
	{
		var world = new SceneWorld();
		try
		{
			var map = new SceneMap( world, "maps/does_not_exist.vmap" );
			Assert.IsFalse( map.IsValid );
		}
		finally
		{
			world.Delete();
		}
	}

	static Model AggregateModel( int drawCalls )
	{
		var material = Material.Load( "materials/default/white.vmat" );
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( 3, new Vertex[]
		{
			new() { Position = new Vector3( 0, 0, 0 ), Normal = Vector3.Up },
			new() { Position = new Vector3( 32, 0, 0 ), Normal = Vector3.Up },
			new() { Position = new Vector3( 0, 32, 0 ), Normal = Vector3.Up }
		} );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
		mesh.Bounds = new BBox( Vector3.Zero, new Vector3( 32, 32, 0 ) );
		for ( int i = 1; i < drawCalls; i++ )
			mesh.AddSubMesh( material, 0, 3, 0, 3 );

		return Model.Builder.AddMesh( mesh ).Create();
	}

	[TestMethod]
	public void AggregateKeepsItsFragments()
	{
		var world = new SceneWorld();
		try
		{
			var model = AggregateModel( 3 );
			var fragments = Enumerable.Range( 0, 3 ).Select( i => new AggregateFragment
			{
				Transform = new Transform( new Vector3( i * 100, 0, 0 ) ),
				BoundsMin = new Vector3( i * 100, 0, 0 ),
				BoundsMax = new Vector3( i * 100 + 32, 32, 0 ),
				Tint = Color.White,
				DrawDescriptorIndex = i,
				Flags = i == 1 ? AggregateFragmentFlags.DoNotCastShadows : AggregateFragmentFlags.None,
			} ).ToArray();

			var aggregate = new SceneAggregateObject( world, model, fragments, true );
			Assert.IsTrue( aggregate.IsValid() );
			Assert.IsTrue( aggregate.CastsShadows, "casts shadows" );
			Assert.IsTrue( aggregate.Flags.IsStatic, "static" );
			Assert.AreEqual( new BBox( Vector3.Zero, new Vector3( 232, 32, 0 ) ), aggregate.Bounds, "bounds of all its fragments" );

			Assert.AreSame( model, aggregate.AggregateModel );
			Assert.AreEqual( 3, aggregate.Fragments.Length );

			// What Borealis draws: here without a device, its mesh made by hand, a draw per draw call
			var draws = Enumerable.Range( 0, 3 ).Select( i => new Borealis.RenderMesh.Draw( 0, i, i * 3, 3, 3, 0, null, Tint: new Vector3( 1, 0.5f, 0.25f ), UvDensity: 2 ) ).ToArray();
			var mesh = new Borealis.RenderMesh( null, model.Bounds, [draws] );
			var drawn = (Borealis.AggregateObject.FragmentDraw[])RenderType.GetMethod( "FragmentDraws", Any ).Invoke( null, [aggregate, model, mesh] );
			Assert.AreEqual( 3, drawn.Length );
			for ( int i = 0; i < 3; i++ )
			{
				var fragment = drawn[i];
				Assert.AreEqual( new Vector3( i * 100, 0, 0 ), new Vector3( fragment.Transform.Row0.w, fragment.Transform.Row1.w, fragment.Transform.Row2.w ), $"fragment {i} placed" );
				Assert.AreEqual( new Vector3( i * 100 + 32, 32, 0 ), fragment.BoundsMax, $"fragment {i} bounds" );
				Assert.AreEqual( 3u, fragment.Args.IndexCount, $"fragment {i} draws its draw call" );
				Assert.AreEqual( (uint)(i * 3), fragment.Args.FirstIndex, $"fragment {i} draws its draw call's range" );
				Assert.AreEqual( (uint)i, fragment.Args.FirstInstance, $"fragment {i} is its own instance" );
				Assert.AreEqual( 2f, fragment.UvDensity );
				Assert.AreEqual( new Vector3( 1, 0.5f, 0.25f ), fragment.DrawTint );
			}

			Assert.AreEqual( Borealis.FragmentFlags.DoNotCastShadows, drawn[1].Flags );
			Assert.AreEqual( Borealis.FragmentFlags.None, drawn[0].Flags );
			aggregate.Delete();
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// An aggregate of a model made at run time (a game's ground chunks, built with <c>Model.Builder</c>) whose draw calls each
	/// start at their own vertex: each fragment draws its draw call's vertices, as the file Borealis made the mesh from has
	/// them. With every base vertex 0, Realistic Car Driving 2's ground drew other chunks' vertices across the view.
	/// </summary>
	[TestMethod]
	public void AggregateOfARunTimeModelKeepsItsBaseVertices()
	{
		TestGraphics.Require();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );
		var host = Borealis.RenderHost.Current;
		Borealis.RenderHost.Current = new DeviceHost( borealis.Services );
		var world = new SceneWorld();
		try
		{
			// Two triangles, the second's vertices after the first's, each draw call indexing its own from 0
			var material = Material.Load( "materials/default/white.vmat" );
			var built = new Mesh( material );
			built.CreateVertexBuffer( 6, Enumerable.Range( 0, 6 ).Select( v => new Vertex { Position = new Vector3( v % 3 == 1 ? 32 : 0, v % 3 == 2 ? 32 : 0, v / 3 * 100 ), Normal = Vector3.Up } ).ToArray() );
			built.CreateIndexBuffer( 6, new[] { 0, 1, 2, 0, 1, 2 } );
			built.Bounds = new BBox( Vector3.Zero, new Vector3( 32, 32, 100 ) );
			built.AddSubMesh( material, 3, 3, 3, 3 );
			var model = Model.Builder.AddMesh( built ).Create();
			Assert.IsTrue( model.IsProcedural );

			Borealis.RenderMesh mesh = null;
			for ( int i = 0; i < 200 && mesh is null; i++ )
			{
				mesh = Sandbox.Rendering.Bridge.MirrorContent.Mesh( model, out _ );
				if ( mesh is null ) System.Threading.Thread.Sleep( 10 );
			}

			Assert.IsNotNull( mesh, "the run-time model was made" );
			var calls = ((Borealis.Backend.BackendModel)mesh.Geometry).File.Meshes[0].DrawCalls;
			Assert.IsTrue( calls.Count >= 2 && calls.Any( c => c.BaseVertex != 0 ), "a draw call starts past the first vertex" );

			var fragments = Enumerable.Range( 0, calls.Count ).Select( i => new AggregateFragment { Transform = Transform.Zero, BoundsMax = new Vector3( 32, 32, 100 ), Tint = Color.White, DrawDescriptorIndex = i } ).ToArray();
			var aggregate = new SceneAggregateObject( world, model, fragments, true );
			var drawn = (Borealis.AggregateObject.FragmentDraw[])RenderType.GetMethod( "FragmentDraws", Any ).Invoke( null, [aggregate, model, mesh] );
			for ( int i = 0; i < calls.Count; i++ )
			{
				Assert.AreEqual( calls[i].BaseVertex, drawn[i].Args.BaseVertex, $"fragment {i} draws its draw call's vertices" );
				Assert.AreEqual( (uint)calls[i].StartIndex, drawn[i].Args.FirstIndex, $"fragment {i} draws its draw call's indices" );
			}

			aggregate.Delete();
		}
		finally
		{
			world.Delete();
			Borealis.RenderHost.Current = host;
		}

		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	sealed class DeviceHost( Borealis.GpuDevice device ) : Borealis.RenderHost
	{
		public override Borealis.GpuDevice Device => device;
	}

	[TestMethod]
	public void AggregateNeedsAFragmentPerDrawCall()
	{
		var world = new SceneWorld();
		try
		{
			var fragments = new[] { new AggregateFragment { Transform = Transform.Zero, DrawDescriptorIndex = 0 } };
			Assert.ThrowsException<System.ArgumentException>( () => new SceneAggregateObject( world, AggregateModel( 2 ), fragments, true ) );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void SkyKeepsItsSettings()
	{
		var world = new SceneWorld();
		try
		{
			var material = Material.Load( "materials/skybox/skybox_day_01.vmat" );
			var sky = new SceneSkyBox( world, material );
			Assert.AreSame( sky, world.GetSky(), "the world's sky" );
			Assert.AreEqual( material, sky.Material );
			Assert.AreEqual( Color.White, sky.SkyTint );
			Assert.AreEqual( SceneSkyBox.FogType.Distance, sky.FogParams.FogType, "native's default fog" );
			Assert.AreEqual( 35f, sky.FogParams.FogMaxEnd );

			sky.SkyTint = Color.Red;
			Assert.AreEqual( Color.Red, sky.SkyTint );

			sky.Delete();
			SceneWorld.FlushQueuedDeletes();
			Assert.IsNull( world.GetSky(), "gone" );
		}
		finally
		{
			world.Delete();
		}
	}
}
