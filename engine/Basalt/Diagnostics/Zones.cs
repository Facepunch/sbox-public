using System.Runtime.InteropServices;

namespace Basalt;

/// <summary>
/// Phase and segment profiler zones, through <see cref="RenderHost.BeginZone"/>. Avoid per-draw overhead.
/// </summary>
internal static class Zones
{
	public static readonly Zone Collect = new( "Basalt Collect", Color.Cyan );
	public static readonly Zone Prepare = new( "Basalt Prepare", Color.Green );
	public static readonly Zone MeshPrepare = new( "Basalt Mesh Prepare", Color.Green );
	public static readonly Zone ShadowCull = new( "Basalt Shadow Cull", Color.Yellow );
	public static readonly Zone Setup = new( "Basalt Setup", Color.Orange );
	public static readonly Zone Record = new( "Basalt Record", Color.Red );
	public static readonly Zone Segment = new( "Basalt Segment", Color.Magenta );
	public static readonly Zone RecordWait = new( "Basalt Record Wait", Color.Gray );
	public static readonly Zone Submit = new( "Basalt Submit", Color.Blue );
}

/// <summary>
/// Scoped profiler zone; a no-op while the host isn't profiling.
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

	public Scope Start()
	{
		var host = RenderHost.Current;
		if ( !host.ProfilesZones ) return default;
		host.BeginZone( name, color );
		return new Scope( host );
	}

	public readonly struct Scope : IDisposable
	{
		readonly RenderHost host;

		public Scope( RenderHost host ) => this.host = host;

		public void Dispose() => host?.EndZone();
	}
}
