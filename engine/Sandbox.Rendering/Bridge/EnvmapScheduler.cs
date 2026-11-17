using System.Runtime.CompilerServices;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Which envmap probes Borealis captures, and when (<see cref="ISceneRenderer.RenderEnvmaps"/>): those the scene camera
/// can see something in, the most needed first, within a time budget a frame. Native captures the first dirty probe
/// every frame, seen or not; a map's 141 probes cost Borealis frames of 80-230 ms each after a load.
/// See basalt-status designs/slangcompile.md ("On-demand envmap captures").
/// </summary>
internal sealed class EnvmapScheduler
{
	/// <summary>Milliseconds of capturing each frame earns; a capture runs while there's credit, its cost taken off.</summary>
	public static float BudgetMs { get; set; } = 4;

	/// <summary>Off: native's order (the first dirty probe every frame, seen or not). For comparisons.</summary>
	public static bool Enabled { get; set; } = true;

	// Probes captured at least once: their later captures (bounces, refreshes) wait behind first ones
	readonly ConditionalWeakTable<EnvmapProbe, object> captured = new();

	float credit;
	float lastCost = 12;

	/// <summary>Captures made, and how long they took, since the counters were last read. For measuring.</summary>
	public int Captures { get; private set; }

	/// <summary>Milliseconds spent capturing; see <see cref="Captures"/>.</summary>
	public double CaptureMs { get; private set; }

	/// <summary>Capture what <paramref name="scene"/> needs this frame.</summary>
	public void Run( Scene scene )
	{
		// Behind a loading screen nothing's on screen, and a probe's OnLoad waits for its first capture: every dirty probe
		// is needed, one a frame as native
		var camera = scene.Camera;
		if ( !Enabled || camera is null || (IGameInstance.Current?.IsLoading ?? false) )
		{
			foreach ( var envmap in scene.EnvmapProbes() )
			{
				if ( !envmap.Dirty ) continue;
				Capture( envmap );
				break;
			}

			return;
		}

		credit = Math.Min( credit + BudgetMs, Math.Max( BudgetMs, lastCost * 2 ) );
		if ( credit <= 0 ) return;

		var frustum = camera.GetFrustum();
		var eye = camera.WorldPosition;
		EnvmapProbe best = null;
		var bestScore = 0f;
		foreach ( var envmap in scene.EnvmapProbes() )
		{
			if ( !envmap.Dirty ) continue;

			// Its influence as a sphere: anything visible inside it can sample it
			var transform = envmap.WorldTransform;
			var bounds = envmap.Bounds;
			var center = transform.PointToWorld( bounds.Center );
			var radius = bounds.Size.Length * 0.5f * MathF.Max( MathF.Abs( transform.Scale.x ), MathF.Max( MathF.Abs( transform.Scale.y ), MathF.Abs( transform.Scale.z ) ) );
			if ( !frustum.IsInside( center, radius, true ) ) continue;

			var distance = eye.Distance( center ) - radius;
			var coverage = distance <= 1 ? 1000f : radius / distance;
			var score = coverage * (captured.TryGetValue( envmap, out _ ) ? 1 : 4);
			if ( score <= bestScore ) continue;
			best = envmap;
			bestScore = score;
		}

		if ( best is not null ) Capture( best );
	}

	void Capture( EnvmapProbe envmap )
	{
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		envmap.RenderCubemap();
		var ms = (float)System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
		captured.AddOrUpdate( envmap, null );
		lastCost = ms;
		credit -= ms;
		Captures++;
		CaptureMs += ms;
	}
}
