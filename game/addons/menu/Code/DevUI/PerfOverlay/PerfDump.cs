using Sandbox.Diagnostics;

namespace Sandbox.UI.Dev;

/// <summary>
/// Samples per-frame performance stats for a fixed duration and writes a JSON
/// report to the game's logs folder. Ticked every frame by <see cref="PerformanceOverlay"/>.
/// </summary>
public static class PerfDump
{
	static List<double> _frameMs;
	static List<double> _gpuMs;
	static List<double> _drawCalls;
	static List<double> _triangles;
	static float _duration;
	static float _remaining;
	static bool _quitWhenDone;
	static float _quitCountdown = -1;

	public static bool Active => _frameMs is not null;

	[MenuConCmd( "perf_dump", Help = "Sample performance every frame for N seconds (default 10), write a JSON report to logs/ and take a screenshot. Pass 'quit' as the second argument to exit the game afterwards." )]
	public static void Run( float seconds = 10, string then = "" )
	{
		if ( Active )
		{
			Log.Warning( "perf_dump: already sampling, ignoring" );
			return;
		}

		if ( seconds <= 0 ) seconds = 10;
		seconds = Math.Clamp( seconds, 1, 300 );

		_duration = seconds;
		_remaining = seconds;
		_quitWhenDone = string.Equals( then, "quit", StringComparison.OrdinalIgnoreCase );
		_frameMs = new();
		_gpuMs = new();
		_drawCalls = new();
		_triangles = new();

		Log.Info( $"perf_dump: sampling for {seconds:0.#} seconds..." );
	}

	public static void Tick()
	{
		// Grace period after finishing so the screenshot capture completes before quitting.
		if ( _quitCountdown > 0 )
		{
			_quitCountdown -= RealTime.Delta;
			if ( _quitCountdown <= 0 )
			{
				Log.Info( "perf_dump: quitting" );
				ConsoleSystem.Run( "quit" );
			}
		}

		if ( !Active )
			return;

		_frameMs.Add( PerformanceStats.FrameTime * 1000.0 );
		_gpuMs.Add( PerformanceStats.GpuFrametime );
		_drawCalls.Add( FrameStats.Current.DrawCalls );
		_triangles.Add( FrameStats.Current.TrianglesRendered );

		_remaining -= RealTime.Delta;
		if ( _remaining <= 0 )
			Finish();
	}

	static void Finish()
	{
		var frameMs = _frameMs;
		var gpuMs = _gpuMs;
		var drawCalls = _drawCalls;
		var triangles = _triangles;

		_frameMs = null;
		_gpuMs = null;
		_drawCalls = null;
		_triangles = null;

		if ( frameMs.Count == 0 )
		{
			Log.Warning( "perf_dump: no samples collected" );
			return;
		}

		var avgFrameMs = frameMs.Average();
		var p99FrameMs = Percentile( frameMs, 99 );

		var report = new Dictionary<string, object>
		{
			["timestamp"] = DateTime.Now.ToString( "yyyy-MM-dd HH:mm:ss" ),
			["durationSeconds"] = Math.Round( _duration, 2 ),
			["sampleCount"] = frameMs.Count,
			["fps"] = new Dictionary<string, object>
			{
				["avg"] = Round( 1000.0 / avgFrameMs ),
				["min"] = Round( 1000.0 / frameMs.Max() ),
				["max"] = Round( 1000.0 / frameMs.Min() ),
				["onePercentLow"] = Round( 1000.0 / p99FrameMs ),
			},
			["frameMs"] = Summarize( frameMs ),
			["gpuMs"] = Summarize( gpuMs ),
			["drawCalls"] = Summarize( drawCalls ),
			["trianglesRendered"] = Summarize( triangles ),
			["vram"] = new Dictionary<string, object>
			{
				["usedMB"] = Round( Graphics.VideoMemoryUsed / (1024.0 * 1024.0) ),
				["budgetMB"] = Round( Graphics.VideoMemoryBudget / (1024.0 * 1024.0) ),
			},
			// Include the active graphics settings so reports are comparable
			["settings"] = SampleSettings(),
		};

		try
		{
			// The engine runs with game/ as the working directory, next to logs/sbox.log.
			var dir = System.IO.Path.GetFullPath( "logs" );
			System.IO.Directory.CreateDirectory( dir );

			var path = System.IO.Path.Combine( dir, $"perf_dump_{DateTime.Now:yyyyMMdd_HHmmss}.json" );
			var json = System.Text.Json.JsonSerializer.Serialize( report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } );
			System.IO.File.WriteAllText( path, json );

			Log.Info( $"perf_dump: {frameMs.Count} samples over {_duration:0.#}s, avg {1000.0 / avgFrameMs:0.#} fps - written to {path}" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"perf_dump: failed to write report - {e.Message}" );
		}

		// Take a screenshot so the numbers come with a visual reference
		ConsoleSystem.Run( "screenshot" );

		if ( _quitWhenDone )
			_quitCountdown = 3;
	}

	static Dictionary<string, object> SampleSettings()
	{
		string[] convars =
		[
			"r.shadows.csm.enabled", "r.shadows.local.enabled", "r.shadows.contact.enabled",
			"r.shadows.csm.distance", "r_ao_quality", "r_ssr_downsample_ratio",
			"r_bloom", "maxdecals", "r_upscaling", "r_upscaler_render_scale",
			"volume_fog_width", "volume_fog_height", "r_texture_stream_max_resolution",
		];

		var result = new Dictionary<string, object>();
		foreach ( var name in convars )
			result[name] = ConsoleSystem.GetValue( name, "<unset>" );

		return result;
	}

	static Dictionary<string, object> Summarize( List<double> samples )
	{
		return new Dictionary<string, object>
		{
			["min"] = Round( samples.Min() ),
			["avg"] = Round( samples.Average() ),
			["max"] = Round( samples.Max() ),
			["p95"] = Round( Percentile( samples, 95 ) ),
			["p99"] = Round( Percentile( samples, 99 ) ),
		};
	}

	static double Percentile( List<double> samples, double percentile )
	{
		var sorted = samples.OrderBy( x => x ).ToList();
		var index = (int)Math.Ceiling( percentile / 100.0 * sorted.Count ) - 1;
		return sorted[Math.Clamp( index, 0, sorted.Count - 1 )];
	}

	static double Round( double value ) => Math.Round( value, 2 );
}
