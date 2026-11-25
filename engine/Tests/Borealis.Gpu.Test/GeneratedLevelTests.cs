using Borealis;
using Borealis.Backend;
using Borealis.Source2;
using Sandbox;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace BorealisGpuTests;

/// <summary>
/// Generated levels of a loaded model without LODs of its own (docs/reference/borealis-gpu-driven.md, "Generated LODs"), as
/// a view draws its own meshes (classic draws, or GPU-driven instance batches): the model's draws ask for their levels
/// through <see cref="BackendModel"/>, a far mesh draws a coarser level than a near one, with fewer triangles
/// than whole, and a level kept doesn't flip back as the camera sways where it changed.
/// </summary>
[TestClass]
[DoNotParallelize]
public class GeneratedLevelTests : GpuTest
{
	const string Model = "models/dev/sphere.vmdl";

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ViewsDrawTheirOwnMeshesAtGeneratedLevels( bool gpuDriven )
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is not null ) Borealis.Backend.Meshlets.MeshOptimizer.Directory = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );
		if ( !Borealis.Backend.Meshlets.MeshOptimizer.Available ) Assert.Inconclusive( "meshopt isn't built" );

		// Classic draws, or GPU-driven instance batches (resident meshes pick their levels on the GPU, borealis_resident_cull_cs)
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = gpuDriven, GpuDrivenResidentMeshes = false };
		var can = backend.LoadModel( Model );
		if ( can is null ) Assert.Inconclusive( $"{Model} isn't built" );
		if ( can.LodCount != 1 ) Assert.Inconclusive( $"{Model} has LODs of its own" );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( 640, 360 ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Generated levels test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			// Static, as GPU-driven calls take static meshes' draws as instance batches
			var near = new MeshObject( can, new Transform( new Vector3( 120, -40, -20 ) ) ) { IsStatic = true };
			var far = new MeshObject( can, new Transform( new Vector3( 4000, 300, -20 ) ) ) { IsStatic = true };
			world.Add( near );
			world.Add( far );

			var view = new RenderView { Name = "Generated levels test", FieldOfView = 70, ZNear = 4, ZFar = 20000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, 640, 360 ) };
			view.Rotation = Rotation.LookAt( Vector3.Forward );

			RenderStats Frame()
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				return system.Stats;
			}

			// The levels are built on workers and published at a frame's start, after a view first asks for them
			var waited = Stopwatch.StartNew();
			while ( can.LevelCount == 0 && waited.Elapsed.TotalSeconds < 30 )
			{
				Frame();
				Thread.Sleep( 20 );
			}

			Assert.IsTrue( can.LevelCount > 0, $"the model's draws have generated levels: {backend.LodReport}" );
			var own = can.DrawsForLod( 0 ).ToArray();
			var first = can.DrawsForLod( 1 ).ToArray();
			Assert.AreEqual( own.Length, first.Length, "a level is the model's draws over fewer indices" );
			Assert.IsTrue( first.Sum( d => d.IndexCount ) < own.Sum( d => d.IndexCount ) );
			Assert.IsTrue( first.Zip( own ).All( p => p.First.IndexCount == p.Second.IndexCount || p.First.StartIndex > p.Second.StartIndex ), "levels sit after the model's own indices in their page" );

			var meshes = system.GetFeature<Borealis.Features.MeshRenderFeature>();
			var withLevels = Frame();
			Assert.IsTrue( meshes.LodFor( far ) > 0, "the far mesh draws a generated level" );
			Assert.IsTrue( meshes.LodFor( near ) < meshes.LodFor( far ), $"the near one a finer one: {meshes.LodFor( near )} against {meshes.LodFor( far )}" );

			far.LodOverride = 0;
			var whole = Frame();
			Assert.AreEqual( 0, meshes.LodFor( far ), "a fixed LOD draws whole" );
			Assert.IsTrue( withLevels.Triangles < whole.Triangles, $"fewer triangles at the level: {withLevels.Triangles} against {whole.Triangles}" );
			far.LodOverride = -1;
			Frame();

			// Dolly towards the far mesh until its level goes finer, then sway there: the level it keeps doesn't flip back
			var level = meshes.LodFor( far );
			var at = view.Position;
			for ( int i = 0; i < 400 && meshes.LodFor( far ) == level; i++ )
			{
				at += Vector3.Forward * 10;
				view.Position = at;
				Frame();
			}

			Assert.AreNotEqual( level, meshes.LodFor( far ), "dollying in changes the far mesh's level" );
			var reversals = Borealis.Features.MeshRenderFeature.LevelReversals;
			var distance = far.Transform.Position.x - at.x;
			for ( int i = 0; i < 40; i++ )
			{
				view.Position = at + Vector3.Forward * distance * 0.005f * MathF.Sin( i / 40.0f * 4 * MathF.Tau );
				Frame();
			}

			Assert.AreEqual( reversals, Borealis.Features.MeshRenderFeature.LevelReversals, "no level changes back where it changed" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A model made at run time (a water surface's clipmap, whose vertices hold cells its vertex stage places) and a draw
	/// whose material places its vertices itself get no generated levels: they'd be simplified from positions that aren't
	/// where they draw, and Construct's pit water drew a few strips from far away.
	/// </summary>
	[TestMethod]
	public void RunTimeModelsAndMaterialsThatPlaceTheirVerticesGetNoLevels()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is not null ) Borealis.Backend.Meshlets.MeshOptimizer.Directory = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );
		if ( !Borealis.Backend.Meshlets.MeshOptimizer.Available ) Assert.Inconclusive( "meshopt isn't built" );

		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = false, GpuDrivenResidentMeshes = false };
		var can = backend.LoadModel( Model );
		if ( can is null ) Assert.Inconclusive( $"{Model} isn't built" );
		if ( can.LodCount != 1 ) Assert.Inconclusive( $"{Model} has LODs of its own" );

		// The same model made as a run-time model is (MirrorContent.RuntimeMesh)
		var runTime = BackendModel.Create( backend, Borealis.Source2.Models.ModelFile.Read( backend.Content.TryRead( Model ), backend.Content.TryRead ), meshGroups: ulong.MaxValue, drawFastPath: true, generateLevels: false );
		Assert.AreSame( GeneratedLevels.None, runTime.Geometry.LevelsFor( runTime.DrawsForLod( 0 ).ToArray()[0] ), "a run-time model's draws: none" );

		var model = (BackendModel)can.Geometry;
		var draw = can.DrawsForLod( 0 ).ToArray()[0];
		using var placing = new PlacingMaterial( true );
		Assert.AreSame( GeneratedLevels.None, model.LevelsFor( draw with { Material = placing } ), "a material that places its vertices itself: none" );
	}

	sealed class PlacingMaterial( bool? places ) : GpuMaterial
	{
		public override string Name => "Places its vertices";
		public override bool IsTranslucent => false;
		public override bool IsAlphaTest => false;
		public override bool ReadsFrameBuffer => false;
		public override bool ShadowFastPath => false;
		public override bool NeedsLightProbe => false;
		public override ulong SimilarityKey => 1;
		public override bool? PlacesVertices => places;
		public override void Dispose() { }
	}
}
