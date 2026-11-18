using System.Runtime.InteropServices;

namespace Borealis;

/// <summary>
/// Phase and segment profiler zones, through <see cref="RenderHost.BeginZone"/>. Avoid per-draw overhead.
/// </summary>
internal static class Zones
{
	public static readonly Zone Collect = new( "Borealis Collect", Color.Cyan );
	public static readonly Zone Prepare = new( "Borealis Prepare", Color.Green );
	public static readonly Zone MeshPrepare = new( "Borealis Mesh Prepare", Color.Green );
	public static readonly Zone AggregatePrepare = new( "Borealis Aggregate Prepare", Color.Green );
	public static readonly Zone ResidentRefresh = new( "Borealis Resident Refresh", Color.Green );
	public static readonly Zone LightPrepare = new( "Borealis Light Prepare", Color.Green );
	public static readonly Zone ShadowPrepare = new( "Borealis Shadow Prepare", Color.Green );
	public static readonly Zone SkyboxPrepare = new( "Borealis Skybox Prepare", Color.Green );
	public static readonly Zone ShadowCull = new( "Borealis Shadow Cull", Color.Yellow );
	public static readonly Zone TreeQuery = new( "Borealis Tree Query", Color.Yellow );
	public static readonly Zone Setup = new( "Borealis Setup", Color.Orange );
	public static readonly Zone ResidentCull = new( "Borealis Resident Cull", Color.Orange );
	public static readonly Zone Record = new( "Borealis Record", Color.Red );
	public static readonly Zone GpuDrivenDraw = new( "Borealis GPU-driven Draw", Color.Red );
	public static readonly Zone OverlayDraw = new( "Borealis Overlay Draw", Color.Red );
	public static readonly Zone Segment = new( "Borealis Segment", Color.Magenta );
	public static readonly Zone RecordWait = new( "Borealis Record Wait", Color.Gray );
	public static readonly Zone Submit = new( "Borealis Submit", Color.Blue );
	public static readonly Zone Stage = new( "Borealis Stage", Color.White );
	public static readonly Zone Reflections = new( "Borealis Screen-space Reflections", Color.White );
	public static readonly Zone CustomObject = new( "Borealis Custom Object", Color.White );
	public static readonly Zone MeshletBuild = new( "Borealis Meshlet Build", Color.Yellow );
	public static readonly Zone MeshletUpload = new( "Borealis Meshlet Upload", Color.Yellow );
	public static readonly Zone MeshletCull = new( "Borealis Meshlet Cull", Color.Yellow );
	public static readonly Zone LodBuild = new( "Borealis LOD Build", Color.Yellow );
	public static readonly Zone LodUpload = new( "Borealis LOD Upload", Color.Yellow );
	public static readonly Zone CubeCapture = new( "Borealis Cube Capture", Color.Blue );
	public static readonly Zone SurfaceCompile = new( "Borealis Surface Compile", Color.Orange );
	public static readonly Zone PassCompile = new( "Borealis Pass Module Compile", Color.Orange );
	public static readonly Zone PipelineCreate = new( "Borealis Pipeline Create", Color.Orange );
	public static readonly Zone VariantLoad = new( "Borealis Variant Load", Color.Orange );
	public static readonly Zone StencilCheck = new( "Borealis Stencil Check", Color.Orange );
	public static readonly Zone MapLightmaps = new( "Borealis Map Lightmaps", Color.Green );
	public static readonly Zone ModelMake = new( "Borealis Model Make", Color.Green );
	public static readonly Zone ModelRead = new( "Borealis Model Read", Color.Green );
	public static readonly Zone PassModulesRead = new( "Borealis Pass Modules Read", Color.Orange );
	public static readonly Zone MirrorSync = new( "Borealis Mirror Sync", Color.Cyan );
	public static readonly Zone VisibilityMake = new( "Borealis Visibility Make", Color.Orange );
	public static readonly Zone MirrorLoaded = new( "Borealis Mirror Loaded", Color.Cyan );
	public static readonly Zone MirrorApply = new( "Borealis Mirror Apply", Color.Cyan );
	public static readonly Zone MirrorLighting = new( "Borealis Mirror Lighting", Color.Cyan );

	/// <summary>Sandbox.ShaderCompiler's phases (<c>CompilePhase</c>), by value.</summary>
	public static readonly Zone[] Slang =
	[
		new( "Slang Surface", Color.Yellow ),
		new( "Slang Pass Stage", Color.Yellow ),
		new( "Slang Reflect", Color.Yellow ),
		new( "Slang Stage", Color.Yellow ),
		new( "Slang Compile", Color.Yellow ),
		new( "Slang Optimize", Color.Yellow ),
		new( "Slang Describe", Color.Yellow ),
		new( "Slang Session Wait", Color.Gray ),
		new( "Slang Stages Wait", Color.Gray ),
		new( "Slang Cache Load", Color.Gray ),
		new( "Slang Cache Save", Color.Gray ),
		new( "Slang Preprocess", Color.Gray ),
	];
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
