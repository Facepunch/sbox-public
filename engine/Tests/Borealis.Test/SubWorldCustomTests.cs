using System;
using Borealis;
using Borealis.Gpu;
using BorealisTests.Headless;
using System.Collections.Generic;
using System.Linq;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// Custom objects of a world drawn in another's views (<see cref="RenderWorld.Include"/>, the editor's gizmo world) each
/// draw with their own transform slot: their own transform and tint.
/// </summary>
// Frames go through Borealis's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class SubWorldCustomTests
{
	/// <summary>A custom object in an overlay layer that notes the instance each of its draws had.</summary>
	sealed class Line : CustomObject
	{
		public static readonly List<(Line Line, int Instance)> Drawn = new();

		public Line( Vector3 position, Color tint, LayerMatch layer )
		{
			LocalBounds = UnitBox;
			Transform = new Transform( position );
			Tint = tint;
			LayerMatch = layer;
		}

		internal override void Render( GpuContext context, ViewPass view, MeshLayer layer ) { }

		internal override void RenderRun( GpuContext context, ViewPass view, MeshLayer layer, ReadOnlySpan<CustomDraw> run )
		{
			foreach ( var draw in run ) Drawn.Add( ((Line)draw.Object, draw.FirstInstance) );
			base.RenderRun( context, view, layer, run );
		}
	}

	[TestMethod]
	[DataRow( LayerMatch.OverlayWithoutDepth )]
	[DataRow( LayerMatch.OverlayWithDepth )]
	[DataRow( LayerMatch.None )]
	public void IncludedWorldsCustomObjectsDrawWithTheirOwnSlots( LayerMatch layer )
	{
		Line.Drawn.Clear();
		var main = new RenderWorld();
		var gizmos = new RenderWorld();
		var mesh = TestMesh( false );
		for ( int i = 0; i < 4; i++ ) main.Add( new MeshObject( mesh, new Transform( new Vector3( 40, i * 3 - 6, -10 ) ) ) );

		var lines = new[]
		{
			new Line( new Vector3( 20, -4, 0 ), Color.Red, layer ),
			new Line( new Vector3( 20, 0, 0 ), Color.Green, layer ),
			new Line( new Vector3( 20, 4, 0 ), Color.Blue, layer ),
		};
		foreach ( var line in lines ) gizmos.Add( line );

		var view = LookingDownX();
		view.SubWorlds = 1UL << (main.Include( gizmos ) - 1);

		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();
		for ( int frame = 0; frame < 3; frame++ )
		{
			Line.Drawn.Clear();
			RenderHost.Current.NextFrame();
			system.Render( main, view, target );

			// Each drawn, reading its own transform and tint
			foreach ( var line in lines )
			{
				var draws = Line.Drawn.Where( d => d.Line == line ).ToList();
				Assert.IsTrue( draws.Count > 0, $"frame {frame}: {line.Tint} drawn" );
				foreach ( var (_, instance) in draws )
				{
					var entry = system.Transforms[system.Transforms.InstanceSlot( instance )];
					Assert.AreEqual( line.Transform.Position.y, entry.Row1.w, 0.001f, $"frame {frame}: {line.Tint}'s transform" );
					Assert.AreEqual( TransformBuffer.PackRgb888( line.Tint ), entry.TintRgb888, $"frame {frame}: {line.Tint}'s tint" );
				}
			}
		}
	}
}
