using System.Runtime.InteropServices;
using NativeEngine;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Phase and segment profiler zones via <c>PerformanceTrace</c>. Avoid per-draw overhead.
/// </summary>
internal static class Zones
{
	public static readonly Zone Collect = new( "SceneRenderer Collect", Color.Cyan );
	public static readonly Zone Prepare = new( "SceneRenderer Prepare", Color.Green );
	public static readonly Zone MeshPrepare = new( "SceneRenderer Mesh Prepare", Color.Green );
	public static readonly Zone ShadowCull = new( "SceneRenderer Shadow Cull", Color.Yellow );
	public static readonly Zone Setup = new( "SceneRenderer Setup", Color.Orange );
	public static readonly Zone Record = new( "SceneRenderer Record", Color.Red );
	public static readonly Zone Segment = new( "SceneRenderer Segment", Color.Magenta );
	public static readonly Zone RecordWait = new( "SceneRenderer Record Wait", Color.Gray );
	public static readonly Zone Submit = new( "SceneRenderer Submit", Color.Blue );
}

/// <summary>
/// Scoped profiler zone; a no-op without the native engine.
/// </summary>
internal sealed class Zone
{
	readonly IntPtr name;
	readonly uint color;

	public Zone( string name, Color color )
	{
		// Process-lifetime storage.
		this.name = Marshal.StringToCoTaskMemUTF8( name );
		Color32 c = color;
		this.color = (uint)c.r << 24 | (uint)c.g << 16 | (uint)c.b << 8 | 0xFF;
	}

	public unsafe Scope Start()
	{
		if ( PerformanceTrace.__N.PerformanceTrace_BeginEvent == null ) return default;
		PerformanceTrace.BeginEvent( name, null, color, null, 0, null );
		return new Scope( true );
	}

	public readonly struct Scope : IDisposable
	{
		readonly bool started;

		public Scope( bool started ) => this.started = started;

		public void Dispose()
		{
			if ( started ) PerformanceTrace.EndEvent();
		}
	}
}
