using Borealis;
using Borealis.Backend;
using Borealis.Source2.World;
using NumericsVector3 = System.Numerics.Vector3;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// The map's environment map probes, as the game makes them: every <c>env_combined_light_probe_volume</c> and
/// <c>env_cubemap_box</c> becomes an <c>EnvmapProbe</c> (<c>EnvmapProbe.InitializeFromLegacy</c>) that renders its cubemap
/// once when it's enabled, since maps no longer compile them: 128 pixels, 7 mips, RGBA16F, box projected over the entity's
/// box, feathered by 8 times <c>cubemap_feathering</c>, from the entity's origin and angles with a near plane of 16 and a far
/// plane of 4096. A realtime probe's scene object is flipped (scale -1, its box mirrored), as <c>UpdateSceneObject</c> sets it.
/// The game captures one probe a frame in the order the scene finds them, so each sees the ones before it.
/// </summary>
internal sealed partial class MapWorld
{
	/// <summary><c>EnvmapProbe.CubemapResolution.Small</c>, the resolution a legacy probe keeps.</summary>
	const int ProbeSize = 128;

	readonly List<(EnvMapObject Probe, GpuTexture Cube, Vector3 Position, Rotation Rotation)> envMapProbes = new();

	/// <summary>Environment map probes made from the entity lump.</summary>
	public int EnvMapProbes => envMapProbes.Count;

	/// <summary>Probes captured so far.</summary>
	public int EnvMapProbesCaptured { get; private set; }

	/// <summary>The main world's probes, each with a black cube until <see cref="CaptureProbes"/> renders it.</summary>
	void AddEnvMapProbes( Level level )
	{
		foreach ( var e in level.Entities )
		{
			if ( e.ClassName is not ("env_combined_light_probe_volume" or "env_cubemap_box") )
			{
				if ( e.ClassName == "env_cubemap" ) Problems.Add( "env_cubemap (sphere projected) isn't drawn" );
				continue;
			}

			var mins = V( e.Vector( "box_mins", new NumericsVector3( -72 ) ) );
			var maxs = V( e.Vector( "box_maxs", new NumericsVector3( 72 ) ) );
			var position = V( e.Origin );
			var rotation = RotationOf( e );
			var cube = CubeCapture.CreateCube( Backend, ProbeSize, $"{e.ClassName} {envMapProbes.Count}" );
			var probe = new EnvMapObject( cube, new BBox( -maxs, -mins ), new Transform( position, rotation ).WithScale( -1 ) )
			{
				Feathering = e.Float( "cubemap_feathering", 0.25f ) * 8,
				Priority = 0,
			};

			World.Add( probe );
			envMapProbes.Add( (probe, cube, position, rotation) );
		}
	}

	/// <summary>
	/// Render every probe's cubemap in turn, a frame each, as the game does when the map loads (<c>EnvmapProbe.RenderCubemap</c>).
	/// </summary>
	public void CaptureProbes()
	{
		if ( envMapProbes.Count == 0 ) return;

		using var capture = new CubeCapture( Backend, ProbeSize );
		if ( !capture.CanFilter ) Problems.Add( "envmap_filtering_cs isn't built: probe cubemaps aren't filtered" );

		// EnvmapProbe's ZNear and ZFar, clamped as RenderCubemap clamps them
		const float ZNear = 16, ZFar = 4096;
		foreach ( var (_, cube, position, rotation) in envMapProbes )
		{
			Backend.BeginFrame();
			RenderHost.Current.NextFrame();
			capture.Render( World, position, rotation, ZNear, ZFar, cube );
			EnvMapProbesCaptured++;
		}

		gpu.WaitIdle();
		Phase( $"Probe cubemaps ({EnvMapProbesCaptured} of {ProbeSize}px)" );
	}
}
