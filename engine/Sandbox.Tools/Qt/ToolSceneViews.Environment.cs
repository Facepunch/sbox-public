using Sandbox.Rendering;
using System;
using System.Runtime.InteropServices;

namespace Editor;

internal static partial class ToolSceneViews
{
	/// <summary>
	/// A native view's 3D skybox and fog (<see cref="Request.SkyboxWorldCount"/> on): what native's view gives its Standard
	/// pipeline, put where a camera's frame reads them. The skybox is the camera's main world's
	/// (<see cref="SceneSkybox3D"/>), seen from the place native computes for the view; gradient fog is the main world's;
	/// cubemap and volumetric fog the camera's.
	/// </summary>
	internal sealed unsafe class ToolEnvironment : IDisposable
	{
		SceneSkybox3D skybox;
		string cubemapFogName;
		Texture cubemapFogTexture;

		/// <summary>What the last request asked for, for checks: skybox worlds, gradient, cubemap and volumetric fog.</summary>
		internal static (int SkyboxWorlds, float SkyboxScale, bool Gradient, bool Cubemap, bool Volumetric) Last;

		/// <summary>
		/// Set <paramref name="camera"/> and its main world <paramref name="world"/> up from the request, after its camera is placed.
		/// </summary>
		public void Apply( SceneCamera camera, SceneWorld world, ref Request request )
		{
			Last = (request.SkyboxWorldCount, request.SkyboxScale, request.GradientFog != 0, request.CubemapFog != 0, request.VolumetricFog != 0);
			ApplySkybox( camera, world, ref request );
			ApplyFog( camera, world, ref request );
		}

		void ApplySkybox( SceneCamera camera, SceneWorld world, ref Request request )
		{
			// The skybox map's own world: its other worlds (its lights, envmaps and view overlays) aren't drawn
			var skyWorld = request.SkyboxWorldCount > 0 && request.SkyboxScale > 0 ? Sandbox.HandleIndex.Get<SceneWorld>( request.SkyboxWorlds[0] ) : null;
			if ( !skyWorld.IsValid() )
			{
				ClearSkybox();
				return;
			}

			if ( skybox is null || skybox.SkyboxWorld != skyWorld || skybox.ParentWorld != world || !skybox.IsValid )
			{
				ClearSkybox();
				skybox = new SceneSkybox3D( world, skyWorld );
			}

			Poll( skyWorld );

			// Native's skybox camera is the view's turned and moved by the sky_reference entity: the rotation and origin that
			// put the main camera there (Borealis's skybox view: origin + rotation⁻¹ · position / scale, rotation⁻¹ · rotation)
			var scale = request.SkyboxScale;
			var skyRotation = Rotation.From( request.SkyboxAngles );
			var toSky = skyRotation * camera.Rotation.Inverse;
			skybox.Scale = scale;
			skybox.Rotation = toSky.Inverse;
			skybox.Origin = request.SkyboxOrigin - toSky * camera.Position / scale;
		}

		void ClearSkybox()
		{
			skybox?.Delete();
			skybox = null;
		}

		void ApplyFog( SceneCamera camera, SceneWorld world, ref Request request )
		{
			if ( request.GradientFog != 0 )
			{
				fixed ( float* p = request.GradientFogParams )
				{
					world.GradientFog = new GradientFogSetup
					{
						Enabled = true,
						StartDistance = p[0],
						EndDistance = p[1],
						StartHeight = p[2],
						EndHeight = p[3],
						MaximumOpacity = p[4],
						DistanceFalloffExponent = p[5],
						VerticalFalloffExponent = p[6],
						Color = new Color( p[8], p[9], p[10], 1 ),
					};
				}
			}
			else
			{
				world.GradientFog = default;
			}

			var cubemap = camera.CubemapFog;
			var texture = request.CubemapFog != 0 ? CubemapFogTexture( request.CubemapFogTexture ) : null;
			cubemap.Enabled = texture is not null;
			if ( texture is not null )
			{
				fixed ( float* p = request.CubemapFogParams )
				{
					cubemap.Texture = texture;
					cubemap.StartDistance = p[0];
					cubemap.EndDistance = p[1];
					cubemap.LodBias = p[2];
					cubemap.FalloffExponent = p[3];
					cubemap.HeightWidth = p[4];
					cubemap.HeightStart = p[5];
					cubemap.HeightExponent = p[6];
				}

				// As the CubemapFog component gives it: the entity's place, its rotation inverted
				cubemap.Transform = new Transform( request.CubemapFogOrigin, Rotation.From( request.CubemapFogAngles ).Conjugate );
				cubemap.Tint = Color.White;
			}

			var volumetric = camera.VolumetricFog;
			volumetric.Enabled = request.VolumetricFog != 0;
			if ( volumetric.Enabled )
			{
				fixed ( float* p = request.VolumetricFogParams )
				{
					volumetric.Anisotropy = p[0];
					volumetric.Scattering = p[1];
					volumetric.DrawDistance = p[2];
					volumetric.FadeInStart = p[3];
					volumetric.FadeInEnd = p[4];
					volumetric.IndirectStrength = p[5];
				}
			}
		}

		Texture CubemapFogTexture( IntPtr utf8 )
		{
			var name = utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8( utf8 );
			if ( string.IsNullOrEmpty( name ) ) return null;
			if ( name == cubemapFogName ) return cubemapFogTexture;

			cubemapFogName = name;
			cubemapFogTexture = Texture.Load( name, false );
			if ( cubemapFogTexture is not null && cubemapFogTexture.IsError ) cubemapFogTexture = null;
			return cubemapFogTexture;
		}

		public void Dispose()
		{
			ClearSkybox();
			cubemapFogTexture = null;
			cubemapFogName = null;
		}
	}
}
