using Borealis.Backend;
using System.Text;
using V3 = System.Numerics.Vector3;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// <c>borealislab -real -lod-shimmer</c>: whether generated LODs change level while the camera keeps still or moves slowly.
/// Counts the culls' level changes (<see cref="BackendDevice.CountLodChanges"/>) over sequences of frames: still, a slow
/// dolly toward the point the view looks at, a slow orbit around it, and a sway back and forth by a small fraction of the
/// distance, there and where the dolly ended (where fragments it changed sit at a threshold). A reversal is a fragment changing
/// back against its last change: what shimmer would be.
/// </summary>
internal static class LodShimmer
{
	/// <summary>Frames rendered after a sequence, still, so the counters (read a few frames late) take it all in.</summary>
	const int Settle = 8;

	public static string Run( BackendDevice backend, MapView view, int frames, Action<MapView> render )
	{
		if ( backend.LodChanges is null ) return "generated LODs: off, no shimmer check";
		backend.CountLodChanges = true;
		var report = new StringBuilder();
		report.AppendLine( "| Sequence | Frames | Level changes | Reversals | To coarser | To finer |" );
		report.AppendLine( "|---|---|---|---|---|---|" );
		try
		{
			// Settled at the view first: every fragment's level picked once
			for ( int i = 0; i < Settle * 2; i++ ) render( view );
			var distance = V3.Distance( view.Position, view.LookAt );
			var toward = V3.Normalize( view.LookAt - view.Position );

			Sequence( "still", _ => view );
			Sequence( "dolly in, 10% of the distance", t => view with { Position = view.Position + toward * distance * 0.1f * t } );
			Sequence( "orbit, 1/32 turn", t => Orbit( view, t / 32 ) );
			Sequence( "sway, 0.5% of the distance, 4 times", t => view with { Position = view.Position + toward * distance * 0.005f * MathF.Sin( t * 4 * MathF.Tau ) } );

			// Where the dolly ended, fragments it changed sit at a threshold: a sway there is what would flip them
			var dollied = view with { Position = view.Position + toward * distance * 0.1f };
			for ( int i = 0; i < Settle * 2; i++ ) render( dollied );
			Sequence( "sway at the dolly's end, 0.5% of the distance, 4 times", t => dollied with { Position = dollied.Position + toward * distance * 0.005f * MathF.Sin( t * 4 * MathF.Tau ) } );
			Sequence( "still again", _ => view );
		}
		finally
		{
			backend.CountLodChanges = false;
		}

		return report.ToString();

		void Sequence( string name, Func<float, MapView> at )
		{
			var before = Counts();
			for ( int i = 0; i < frames; i++ ) render( at( i / (float)frames ) );

			// Ends where it ends, still, while the counters catch up
			var end = at( 1 );
			for ( int i = 0; i < Settle; i++ ) render( end );
			var after = Counts();
			report.AppendLine( FormattableString.Invariant( $"| {name} | {frames} | {after.Changes - before.Changes} | {after.Reversals - before.Reversals} | {after.Coarser - before.Coarser} | {after.Finer - before.Finer} |" ) );

			// Back where the next starts, settled
			for ( int i = 0; i < Settle; i++ ) render( view );
		}

		(long Changes, long Reversals, long Coarser, long Finer) Counts() => backend.LodChanges ?? default;
	}

	static MapView Orbit( MapView view, float turns )
	{
		var (sin, cos) = MathF.SinCos( turns * MathF.Tau );
		var offset = view.Position - view.LookAt;
		var turned = new V3( offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos, offset.Z );
		return view with { Position = view.LookAt + turned };
	}
}
