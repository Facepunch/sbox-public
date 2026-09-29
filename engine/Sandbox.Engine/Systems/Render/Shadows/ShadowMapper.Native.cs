using NativeEngine;

namespace Sandbox.Rendering;

/// <summary>
/// The native scenesystem side of <see cref="ShadowMapper"/>: lights and views come from native, and each
/// shadow view is added to the scenesystem as a child of the view the shadows are for.
/// </summary>
internal partial class ShadowMapper
{
	/// <summary>
	/// The native view this mapper's light binner is working on.
	/// </summary>
	ISceneView SceneView { get; set; }

	/// <summary>Directional light for this view.</summary>
	SceneLight DirectionalLight { get; set; }

	bool nativeViewInfoValid;

	internal static ShadowMapper CreateNative()
	{
		var mapper = new ShadowMapper();
		mapper.Renderer = new NativeShadowRenderer( mapper );
		return mapper;
	}

	internal void InitForView( ISceneView sceneView )
	{
		SceneView = sceneView;
		DirectionalLight = null;
		nativeViewInfoValid = false;

		InitForView();
	}

	/// <summary>
	/// Read what the shadows need from the native view, once per view - on first use, when the view is set up.
	/// </summary>
	void UseNativeView( ISceneView view )
	{
		if ( nativeViewInfoValid && view == SceneView ) return;

		SceneView = view;
		nativeViewInfoValid = true;

		var mainViewport = view.GetMainViewport();
		View = new ShadowViewInfo
		{
			InverseViewProjection = view.GetFrustum().GetInvReverseZViewProjTranspose()._numerics,
			CameraPosition = view.GetCameraPosition(),
			ViewportSize = new Vector2( mainViewport.Rect.Width, mainViewport.Rect.Height ),
			IsSkybox = view.GetRenderAttributesPtr().GetBoolValue( "IsSkybox", false ),
		};
	}

	static ShadowLight FromNative( SceneLight light )
	{
		var native = light.lightNative;
		var type = (ShadowLightType)native.GetLightType();

		var shadowLight = new ShadowLight
		{
			Key = light,
			Type = type,
			Position = light.Position,
			TransformVersion = light.TransformVersion,
			Radius = light.Radius,
			Hardness = light.ShadowHardness,
			Bias = light.ShadowBias,
			Baked = (native.GetLightFlags() & 32) != 0, // LIGHTTYPE_FLAGS_BAKED
			Static = light.GameObject.IsValid() && light.GameObject.IsStatic,
			ShadowsEnabled = light.ShadowsEnabled,
		};

		if ( type == ShadowLightType.Spot )
		{
			shadowLight.Rotation = light.Rotation;
			shadowLight.ConeOuter = native.GetPhi();
		}

		if ( type == ShadowLightType.Directional )
		{
			// Native's WorldDirection points at the light
			shadowLight.Direction = -light.WorldDirection;
			shadowLight.Color = light.LightColor;
			shadowLight.FogStrength = light.FogStrength;
			shadowLight.Cascades = native.GetShadowCascades();
			shadowLight.CascadeSplitRatio = native.GetShadowCascadeSplitRatio();
		}

		return shadowLight;
	}

	/// <summary>
	/// Find a cached shadow map or create a new one for the light and view.
	/// Returns an index to the shadow maps structured buffer
	/// </summary>
	internal uint FindOrCreateShadowMaps( SceneLight sceneObject, ISceneView view, float flScreenSize )
	{
		if ( !LocalShadowsEnabled )
			return InvalidShadowIndex;

		UseNativeView( view );
		return FindOrCreateShadowMaps( FromNative( sceneObject ), flScreenSize );
	}

	internal int DoDirectionalLight( SceneLight sceneObject, ISceneView view )
	{
		DirectionalLight = sceneObject;
		UseNativeView( view );
		return DoDirectionalLight( FromNative( sceneObject ) );
	}

	/// <summary>
	/// Renders shadow views through the native scenesystem, each a child of the mapper's current view.
	/// </summary>
	sealed class NativeShadowRenderer : IShadowRenderer
	{
		readonly ShadowMapper mapper;
		readonly CFrustum frustum = CFrustum.Create();
		readonly CFrustum exclusionFrustum = CFrustum.Create();

		public NativeShadowRenderer( ShadowMapper mapper )
		{
			this.mapper = mapper;
		}

		public System.Numerics.Matrix4x4 RenderShadowView( in ShadowViewDesc view )
		{
			if ( view.Orthographic )
				frustum.InitOrthoCamera( view.Position, view.Rotation.Angles(), view.ZNear, view.ZFar, view.Width, view.Height );
			else
				frustum.BuildFrustumFromVectors( view.Position, view.ZNear, view.ZFar, view.FieldOfView, 1.0f, view.Rotation.Forward, view.Rotation.Left, view.Rotation.Up );

			var exclusion = default( CFrustum );
			if ( view.HasExclusion )
			{
				var size = view.ExclusionSize;
				exclusionFrustum.InitOrthoCamera( view.ExclusionCenter, view.Rotation.Angles(), -size * 0.5f, size * 0.5f, size, size );
				exclusion = exclusionFrustum;
			}

			CSceneSystem.AddShadowView( view.Name,
				mapper.SceneView,
				frustum,
				new( 0, 0, view.Resolution, view.Resolution ),
				view.Target.Texture.native,
				view.Slice,
				view.RequiredFlags,
				view.ExcludedFlags,
				view.DepthBias,
				view.SlopeScaledDepthBias,
				exclusion,
				cachedShadowTexture: view.CachedStatic is null ? default : view.CachedStatic.Texture.native );

			return frustum.GetReverseZViewProjTranspose()._numerics;
		}

		public ShadowMap GetCascadeTarget( int cascade, int resolution )
		{
			// Temporary targets are loaned for the frame, so a second camera this frame gets its own
			var rt = RenderTarget.GetTemporary( resolution, resolution, ImageFormat.None, ImageFormat.D32 );
			return new ShadowMap( rt.DepthTarget );
		}

		public uint GetShadowMaskIndex( object light )
		{
			if ( !ContactShadows.Enabled || light is not SceneLight sceneLight || !sceneLight.ContactShadows )
				return 0;

			return sceneLight.GetShadowMask( mapper.SceneView ) is { } mask ? (uint)mask.Index : 0;
		}
	}
}
