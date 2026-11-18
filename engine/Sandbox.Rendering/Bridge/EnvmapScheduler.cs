using System.Runtime.CompilerServices;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Which envmap probes Borealis captures, and when (<see cref="ISceneRenderer.RenderEnvmaps"/>): those the scene camera
/// can see something in, the most needed first, within a time budget a frame. Native captures the first dirty probe
/// every frame, seen or not; a map's 141 probes cost Borealis frames of 80-230 ms each after a load.
/// See basalt-status designs/slangcompile.md ("On-demand envmap captures").
/// </summary>
internal sealed class EnvmapScheduler( SceneBridge bridge )
{
	/// <summary>Milliseconds of capturing each frame earns; a capture runs while there's credit, its cost taken off.</summary>
	public static float BudgetMs { get; set; } = 4;

	/// <summary>Off: native's order (the first dirty probe every frame, seen or not). For comparisons.</summary>
	public static bool Enabled { get; set; } = true;

	/// <summary>Whether a capture's faces spread over frames within the budget (on), rather than all six in its first frame.</summary>
	public static bool SpreadFaces { get; set; } = true;

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
		// is needed, one a frame as native. So it is without a camera to see from: none, or one not enabled yet (or any
		// more), whose scene camera isn't made
		var camera = scene.Camera;
		if ( !Enabled || camera is not { IsValid: true, SceneCamera: not null } || (IGameInstance.Current?.IsLoading ?? false) )
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

		// A capture spread over frames goes on before another starts: its faces wait in the targets the next would draw into
		if ( bridge is { CaptureSpread: true } )
		{
			var ms = (float)bridge.ContinueSpreadCapture( credit );
			credit -= ms;
			CaptureMs += ms;
			return;
		}

		if ( Pick( scene, camera ) is { } best ) Capture( best, spread: true );
	}

	/// <summary>
	/// The dirty probe <paramref name="camera"/> needs most: of those whose influence its frustum touches, the largest on
	/// screen, a first capture counting four times a refresh; null when it sees none.
	/// </summary>
	internal EnvmapProbe Pick( Scene scene, CameraComponent camera )
	{
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

		return best;
	}

	/// <summary>Count <paramref name="envmap"/> as captured: its next capture is a refresh. For tests.</summary>
	internal void MarkCaptured( EnvmapProbe envmap ) => captured.AddOrUpdate( envmap, null );

	void Capture( EnvmapProbe envmap, bool spread = false )
	{
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		if ( bridge is not null ) bridge.SpreadCaptureMs = spread && SpreadFaces ? Math.Max( credit, 0 ) : -1;
		try
		{
			envmap.RenderCubemap();
		}
		finally
		{
			if ( bridge is not null ) bridge.SpreadCaptureMs = -1;
		}

		var ms = (float)System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
		captured.AddOrUpdate( envmap, null );
		lastCost = ms;
		credit -= ms;
		Captures++;
		CaptureMs += ms;
	}
}
