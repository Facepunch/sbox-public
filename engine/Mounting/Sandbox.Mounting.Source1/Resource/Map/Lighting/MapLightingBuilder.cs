using System;
using System.IO;
using static Sandbox.Mounting.Source1.BspWorldLightType;

namespace Sandbox.Mounting.Source1;

internal sealed class MapLightingBuilder( BspFile bsp, MapLoader loader, BspLightingContext lighting )
{
	private static float IndexedLightScale( PropWorldLight light )
	{
		float scale = light.Type switch
		{
			SkyLight => 1,
			BspWorldLightType.Surface => 10000,
			_ => light.Constant + light.Linear * 100 + light.Quadratic * 10000
		};
		if ( !float.IsFinite( scale ) || scale <= 0 )
			throw new InvalidDataException( "BSP world light has invalid attenuation at the 100-unit reference distance." );
		return light.Type == SkyLight ? scale : Math.Max( scale, Math.Max( light.Intensity.x, Math.Max( light.Intensity.y, light.Intensity.z ) ) );
	}

	public void BuildLightProbes( GameObject world, bool skybox )
	{
		string FullPath = loader.FullPath;
		if ( bsp.ambientLighting is null )
		{
			Log.Warning( $"{FullPath}: BSP has no leaf ambient lighting; native light probes were not created." );
			return;
		}

		var root = new GameObject( false, "Source BSP lighting" )
		{
			Parent = world
		};
		var worldIndices = CreateIndexedWorldLights( root, skybox );
		int volumes = 0;
		int totalProbes = 0;
		Span<Vector3> cube = stackalloc Vector3[6];
		Span<int> indices = stackalloc int[4];
		Span<float> strengths = stackalloc float[4];
		Span<float> scales = stackalloc float[4];
		for ( int leafIndex = 0; leafIndex < bsp.leaves.Length; leafIndex++ )
		{
			var leaf = bsp.leaves[leafIndex];
			if ( (leaf.Contents & 1) != 0 || (skybox ? !bsp.IsActiveSkyboxLeaf( leaf ) : bsp.skyboxAreas.Contains( leaf.Area )) )
				continue;
			var bounds = leaf.Bounds;
			if ( bounds.Size.x <= 0 || bounds.Size.y <= 0 || bounds.Size.z <= 0 )
				continue;
			var candidates = bsp.ProbeLightCandidates( leaf, lighting );
			var selected = bsp.SelectProbeLights( leafIndex, candidates, worldIndices, lighting );
			indices.Fill( -1 );
			for ( int channel = 0; channel < selected.Length; channel++ )
			{
				indices[channel] = worldIndices[selected[channel]];
				scales[channel] = IndexedLightScale( bsp.worldLights[selected[channel]] );
			}

			var counts = bsp.ProbeCounts( leafIndex, candidates.Length > 0 );
			var textures = new BspProbeTextureData( counts );
			for ( int z = 0; z < counts.z; z++ )
				for ( int y = 0; y < counts.y; y++ )
					for ( int x = 0; x < counts.x; x++ )
					{
						var position = bounds.Mins + bounds.Size * new Vector3( (x + 0.5f) / counts.x, (y + 0.5f) / counts.y, (z + 0.5f) / counts.z );
						bsp.ambientLighting.Evaluate( leafIndex, position, cube );
						strengths.Clear();
						foreach ( int index in candidates )
						{
							var light = bsp.worldLights[index];
							float strength = bsp.ProbeLightStrength( light, position, leaf, lighting );
							if ( strength <= 0 )
								continue;
							int channel = Array.IndexOf( selected, index );
							if ( channel >= 0 )
								strengths[channel] = strength * scales[channel];
							else
							{
								var direction = light.Type == SkyLight ? -light.Normal : (light.Origin - position).Normal;
								var color = light.Intensity * strength;
								for ( int side = 0; side < 6; side++ )
									cube[side] += color * Math.Max( 0, Vector3.Dot( direction, PropLightingState.Axes[side] ) );
							}
						}

						textures.Write( x + counts.x * (y + counts.y * z), cube, indices, strengths );
					}

			var go = new GameObject( false, $"leaf light probes {leafIndex}" )
			{
				Parent = root,
				LocalPosition = bounds.Center
			};
			var component = go.AddComponent<LightProbeVolume>();
			component.Bounds = new BBox( bounds.Mins - bounds.Center, bounds.Maxs - bounds.Center );
			component.Priority = -(int)Math.Ceiling( Math.Log2( Math.Max( 1, bounds.Size.LengthSquared ) ) );
			int probes = counts.x * counts.y * counts.z;
			totalProbes += probes;
			component.Texture = loader.CreateProbeTexture( counts, counts.z * 6, ImageFormat.RGBA16161616F, textures.Data.AsSpan( 0, probes * 48 ) );
			component.IndicesTexture = loader.CreateProbeTexture( counts, counts.z, ImageFormat.RGBA8888, textures.Data.AsSpan( probes * 48, probes * 4 ) );
			component.ScalarsTexture = loader.CreateProbeTexture( counts, counts.z, ImageFormat.RGBA16161616F, textures.Data.AsSpan( probes * 52, probes * 8 ) );
			go.Enabled = true;
			volumes++;
		}

		root.Enabled = true;
		Log.Info( $"{FullPath}: created {volumes} native leaf probe volumes ({totalProbes} probes, {loader.ProbeTextureCount} unique textures, {loader.ProbeTextureBytes / (1024f * 1024f):F1} MiB payload instead of {totalProbes * 60L / (1024f * 1024f):F1} MiB) with {worldIndices.Count( index => index >= 0 )} indexed world lights." );
	}

	private int[] CreateIndexedWorldLights( GameObject root, bool skybox )
	{
		var indices = new int[bsp.worldLights.Length];
		Array.Fill( indices, -1 );
		int sun = bsp.ImportedSunIndex;
		bool sunShadows = bsp.SunShadowsEnabled( skybox );
		var eligible = Enumerable.Range( 0, bsp.worldLights.Length )
			.Where( index => CanIndexWorldLight( index, sun, skybox ) )
			.OrderByDescending( index => bsp.worldLights[index].Type == SkyLight ? float.MaxValue : bsp.worldLights[index].Intensity.Length )
			.ThenBy( index => index )
			.ToArray();
		int indexedLightCount = eligible.Length;
		if ( indexedLightCount > 255 )
			Log.Warning( $"BSP has {indexedLightCount} world lights; lights beyond the native 255-index limit are folded into ambient cubes." );
		int nextBakeIndex = 0;
		foreach ( int index in eligible )
		{
			var light = bsp.worldLights[index];
			if ( nextBakeIndex >= 255 )
				continue;
			int bake = nextBakeIndex++;
			indices[index] = bake;
			float scale = IndexedLightScale( light );
			var go = new GameObject( false, $"baked world light {index}" )
			{
				Parent = root,
				LocalPosition = light.Origin
			};
			go.LocalRotation = light.Type == SkyLight ? Rotation.LookAt( light.Normal ) : Rotation.Identity;
			Light component;
			if ( light.Type == SkyLight )
			{
				var directional = go.AddComponent<DirectionalLight>();
				directional.ContactShadows = true;
				directional.IncludeStaticShadowCasters = true;
				if ( !sunShadows )
					directional.ShadowCascadeCount = 0;
				component = directional;
			}
			else
			{
				var point = go.AddComponent<PointLight>();
				point.Radius = light.Radius;
				point.ConstantAttenuation = light.Type == BspWorldLightType.Surface ? 0 : light.Constant / scale;
				point.LinearAttenuation = light.Type == BspWorldLightType.Surface ? 0 : light.Linear / scale;
				point.Attenuation = (light.Type == BspWorldLightType.Surface ? 1 : light.Quadratic) * 10000f / scale;
				component = point;
			}

			component.BakeLightIndex = bake;
			component.MixedShadows = light.Type == SkyLight;
			float intensityScale = (light.Type == SkyLight ? bsp.lightingStyles[light.Style] : 1) / scale;
			component.LightColor = new Color( light.Intensity.x * intensityScale, light.Intensity.y * intensityScale, light.Intensity.z * intensityScale );
			component.Shadows = light.Type == SkyLight;
			component.FogMode = Light.FogInfluence.Disabled;
			go.Enabled = true;
		}

		return indices;
	}

	private bool CanIndexWorldLight( int index, int sun, bool skybox )
	{
		var light = bsp.worldLights[index];
		if ( !bsp.UsesImportedLightstyle( light ) || (light.Flags & BspWorldLightFlags.InAmbientCube) != 0
			|| light.Type is < BspWorldLightType.Surface or > SkyLight )
			return false;
		if ( light.Type == SkyLight )
			return index == sun;
		return skybox ? bsp.IsActiveSkyboxPosition( light.Origin ) : !bsp.IsSkyboxPosition( light.Origin );
	}

	public SourceMaterialOverrides CreateBakedShadowBindings( bool skybox )
	{
		bool enabled = bsp.SunShadowsEnabled( skybox ) && bsp.worldLights.Any( light => light.Type == SkyLight && bsp.UsesImportedLightstyle( light ) && (light.Flags & BspWorldLightFlags.InAmbientCube) == 0 );
		var tint = (bsp.ShadowControl?.Vector( "color", new Vector3( 187 ) ) ?? new Vector3( 187 )).Clamp( 0, 255 ) / 255;
		var bindings = new SourceMaterialOverrides { MapSurface = true };
		bindings.Flags["g_flSourceBakedSunShadows"] = enabled;
		bindings.Vectors["g_vSourceBakedShadowTint"] = tint;
		return bindings;
	}
}
