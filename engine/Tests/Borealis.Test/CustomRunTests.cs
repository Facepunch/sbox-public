using System;
using Borealis;
using Borealis.Gpu;
using BorealisTests.Headless;
using System.Collections.Generic;
using System.Linq;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// Custom objects that follow one another in a layer are handed to the first of them as one run
/// (<see cref="CustomObject.RenderRun"/>), each with its own transform slot, so a host can draw them in one block.
/// </summary>
// Frames go through Borealis's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class CustomRunTests
{
	/// <summary>An opaque custom object that notes the runs it's handed and its own draws.</summary>
	sealed class RunCustom : CustomObject
	{
		public static readonly List<int[]> Runs = new();
		public int Drawn;

		public RunCustom( Vector3 position )
		{
			LocalBounds = UnitBox;
			Transform = new Transform( position );
			IsOpaque = true;
		}

		internal override void Render( GpuContext context, ViewPass view, MeshLayer layer ) => Drawn++;

		internal override void RenderRun( GpuContext context, ViewPass view, MeshLayer layer, ReadOnlySpan<CustomDraw> run )
		{
			if ( layer.Name == "Opaque Forward" ) Runs.Add( run.ToArray().Select( d => d.FirstInstance ).ToArray() );
			base.RenderRun( context, view, layer, run );
		}
	}

	/// <summary>An opaque custom object of another type.</summary>
	sealed class OtherCustom : CustomObject
	{
		public OtherCustom( Vector3 position )
		{
			LocalBounds = UnitBox;
			Transform = new Transform( position );
			IsOpaque = true;
		}

		internal override void Render( GpuContext context, ViewPass view, MeshLayer layer ) { }
	}

	[TestMethod]
	public void ConsecutiveCustomObjectsDrawAsOneRun()
	{
		RunCustom.Runs.Clear();
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 40, 0, 0 ) ) ) );
		var customs = Enumerable.Range( 0, 4 ).Select( i => new RunCustom( new Vector3( 20, i * 2 - 3, 0 ) ) ).ToList();
		foreach ( var custom in customs ) world.Add( custom );

		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();
		RenderHost.Current.NextFrame();
		system.Render( world, LookingDownX(), target );

		// One run of all four in the forward layer, each with a slot of its own, and each still drawn by the default run
		Assert.AreEqual( 1, RunCustom.Runs.Count, string.Join( " | ", RunCustom.Runs.Select( r => string.Join( ",", r ) ) ) );
		Assert.AreEqual( 4, RunCustom.Runs[0].Length );
		Assert.AreEqual( 4, RunCustom.Runs[0].Distinct().Count() );
		Assert.IsTrue( customs.All( c => c.Drawn >= 1 ) );
	}

	[TestMethod]
	public void RunsHoldOneTypeOfCustomObject()
	{
		RunCustom.Runs.Clear();
		var world = new RenderWorld();
		var customs = Enumerable.Range( 0, 3 ).Select( i => new RunCustom( new Vector3( 20, i * 2 - 2, 0 ) ) { RenderOrder = i * 2 } ).ToList();
		foreach ( var custom in customs ) world.Add( custom );
		world.Add( new OtherCustom( new Vector3( 20, 0, 1 ) ) { RenderOrder = 1 } );

		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();
		RenderHost.Current.NextFrame();
		system.Render( world, LookingDownX(), target );

		// Every one drawn, in runs of its own type only
		Assert.IsTrue( customs.All( c => c.Drawn >= 1 ) );
		Assert.AreEqual( 3, RunCustom.Runs.Sum( r => r.Length ) );
	}
}
