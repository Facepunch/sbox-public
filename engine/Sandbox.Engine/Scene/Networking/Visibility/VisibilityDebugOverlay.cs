using System.Collections.Generic;

namespace Sandbox;

/// <summary>
/// Server-side debug overlay that draws green/red LoS lines using Gizmos.
/// Enable with <c>sv_vis_debug 1</c> in the console.
///
/// <para>
/// <b>Green lines</b> = visible (at least one BBox test point has clear LoS).<br/>
/// <b>Red lines</b> = occluded (all test points blocked by geometry).<br/>
/// <b>Yellow lines</b> = visible via prediction or sound (not direct LoS).<br/>
/// <b>Cyan spheres</b> = BBox test points on the target.
/// </para>
/// </summary>
public sealed class VisibilityDebugOverlay : GameObjectSystem<VisibilityDebugOverlay>
{
	// ─── Recorded check data ─────────────────────────────────────

	/// <summary>
	/// A single recorded LoS check for debug visualisation.
	/// </summary>
	public struct DebugCheckRecord
	{
		public Connection Observer;
		public GameObject Target;
		public Vector3 ObserverPosition;
		public LineOfSightChecker.LoSResult Result;
		public float RecordedAt; // RealTime.Now
	}

	/// <summary>
	/// Thread-safe-ish buffer of recent checks.  We keep the last N checks and
	/// draw them for a short duration.
	/// </summary>
	private static readonly List<DebugCheckRecord> _records = new( 256 );
	private static readonly object _recordLock = new();

	/// <summary>
	/// How long (seconds) a debug line persists on screen.
	/// </summary>
	private const float DrawDuration = 0.15f;

	/// <summary>
	/// Maximum number of records to keep.
	/// </summary>
	private const int MaxRecords = 512;

	// ─── Lifecycle ───────────────────────────────────────────────

	public VisibilityDebugOverlay( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 2000, DrawOverlay, "VisibilityDebugOverlay.Draw" );
	}

	// ─── Recording ───────────────────────────────────────────────

	/// <summary>
	/// Record a LoS check result for debug drawing.
	/// Called from <see cref="VisibilityManager.IsVisibleTo"/> when debug is enabled.
	/// </summary>
	public static void RecordCheck( Connection observer, GameObject target,
		Vector3 observerPosition, LineOfSightChecker.LoSResult result )
	{
		if ( !VisibilityConfig.DebugEnabled )
			return;

		// Filter by Steam ID if configured.
		if ( VisibilityConfig.DebugFilterSteamId != 0 &&
			 (ulong)observer.SteamId != VisibilityConfig.DebugFilterSteamId )
			return;

		lock ( _recordLock )
		{
			if ( _records.Count >= MaxRecords )
				_records.RemoveAt( 0 );

			_records.Add( new DebugCheckRecord
			{
				Observer = observer,
				Target = target,
				ObserverPosition = observerPosition,
				Result = result,
				RecordedAt = RealTime.Now
			} );
		}
	}

	// ─── Drawing ─────────────────────────────────────────────────

	/// <summary>
	/// Draw all active debug records using Gizmo.
	/// </summary>
	private void DrawOverlay()
	{
		if ( !VisibilityConfig.DebugEnabled )
			return;

		if ( !Networking.IsHost )
			return;

		lock ( _recordLock )
		{
			var now = RealTime.Now;

			// Remove expired records.
			for ( var i = _records.Count - 1; i >= 0; i-- )
			{
				if ( now - _records[i].RecordedAt > DrawDuration * 3f )
					_records.RemoveAt( i );
			}

			// Draw remaining records.
			foreach ( var record in _records )
			{
				var age = now - record.RecordedAt;
				var alpha = 1f - (age / (DrawDuration * 3f));
				alpha = MathF.Max( alpha, 0.1f );

				DrawCheckRecord( record, alpha );
			}
		}

		// Draw spatial grid stats.
		DrawGridStats();
	}

	/// <summary>
	/// Draw a single LoS check record.
	/// </summary>
	private void DrawCheckRecord( in DebugCheckRecord record, float alpha )
	{
		var from = record.ObserverPosition;

		if ( record.Result.IsVisible )
		{
			// Green line to the first visible point.
			var to = record.Result.FirstVisiblePoint;

			using ( Gizmo.Scope( "vis_line_ok" ) )
			{
				Gizmo.Draw.Color = new Color( 0f, 1f, 0f, alpha );
				Gizmo.Draw.Line( from, to );

				// Small green sphere at the visible point.
				Gizmo.Draw.Color = new Color( 0f, 1f, 0f, alpha * 0.5f );
				Gizmo.Draw.LineSphere( to, 4f );
			}
		}
		else
		{
			// Red line to the first blocked point.
			var to = record.Result.FirstBlockedPoint;

			if ( to != Vector3.Zero )
			{
				using ( Gizmo.Scope( "vis_line_blocked" ) )
				{
					Gizmo.Draw.Color = new Color( 1f, 0f, 0f, alpha );
					Gizmo.Draw.Line( from, to );

					// Small red sphere at the blocked point.
					Gizmo.Draw.Color = new Color( 1f, 0f, 0f, alpha * 0.5f );
					Gizmo.Draw.LineSphere( to, 4f );
				}
			}
		}

		// Draw the target's bounding box.
		if ( record.Target is not null && record.Target.IsValid() )
		{
			DrawTargetBBox( record.Target, record.Result.IsVisible, alpha );
		}
	}

	/// <summary>
	/// Draw the target's bounding box and its 9 test points.
	/// </summary>
	private void DrawTargetBBox( GameObject target, bool isVisible, float alpha )
	{
		var bounds = target.WorldBounds;
		var color = isVisible
			? new Color( 0f, 1f, 0f, alpha * 0.2f )
			: new Color( 1f, 0f, 0f, alpha * 0.2f );

		using ( Gizmo.Scope( "vis_bbox" ) )
		{
			Gizmo.Draw.Color = color;
			Gizmo.Draw.LineBBox( bounds );
		}

		// Draw the 9 test points as cyan spheres.
		Span<Vector3> testPoints = stackalloc Vector3[VisibilityConfig.BBoxTestPoints];
		LineOfSightChecker.GetTestPoints( bounds, testPoints );

		using ( Gizmo.Scope( "vis_testpoints" ) )
		{
			Gizmo.Draw.Color = new Color( 0f, 1f, 1f, alpha * 0.6f );

			for ( var i = 0; i < VisibilityConfig.BBoxTestPoints; i++ )
			{
				Gizmo.Draw.LineSphere( testPoints[i], 2f );
			}
		}
	}

	/// <summary>
	/// Draw spatial grid statistics as on-screen text.
	/// </summary>
	private void DrawGridStats()
	{
		var manager = Scene.GetSystem<VisibilityManager>();
		if ( manager is null )
			return;

		var entityCount = manager.Grid.Count;

		using ( Gizmo.Scope( "vis_stats" ) )
		{
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.ScreenText(
				$"Visibility System: {entityCount} tracked entities | " +
				$"Grid cell: {VisibilityConfig.GridCellSize}u | " +
				$"Max range: {VisibilityConfig.MaxVisibilityRange}u | " +
				$"Budget: {VisibilityConfig.MaxLoSChecksPerTick}/tick",
				new Vector2( 10, 10 ),
				"Consolas",
				12
			);
		}
	}
}
