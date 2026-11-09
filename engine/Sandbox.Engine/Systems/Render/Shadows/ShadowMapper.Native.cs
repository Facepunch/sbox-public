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

		// Main-world fog lighting is inherited by the 3D skybox view.
		if ( !sceneView.GetRenderAttributesPtr().GetBoolValue( "IsSkybox", false ) )
		{
			sceneView.GetRenderAttributesPtr().SetVectorValue( "FogSunColor", Vector3.Zero );
			sceneView.GetRenderAttributesPtr().SetVectorValue( "FogSunDirection", Vector3.Zero );
		}
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
		var frustum = view.GetFrustum();

		// Native matrices are column-vector, so the projection is transposed for row-vector use. The view matrix's
		// rotation is orthonormal: read row-vector, its world to view rotation turns view space back into world.
		var viewToWorldRotation = frustum.GetView()._numerics;
		viewToWorldRotation.M14 = viewToWorldRotation.M24 = viewToWorldRotation.M34 = 0.0f;

		View = new ShadowViewInfo
		{
			ProjectionToView = System.Numerics.Matrix4x4.Transpose( frustum.GetInvReverseZProj()._numerics ),
			ViewToWorldRotation = viewToWorldRotation,
			CameraPosition = view.GetCameraPosition(),
			ViewportSize = new Vector2( mainViewport.Rect.Width, mainViewport.Rect.Height ),
			IsSkybox = view.GetRenderAttributesPtr().GetBoolValue( "IsSkybox", false ),
			IsReflection = view.GetRenderAttributesPtr().GetBoolValue( "IsReflection", false ),
			NoShadows = view.GetRenderAttributesPtr().GetBoolValue( "IsReflection", false )
				&& !view.GetRenderAttributesPtr().GetBoolValue( "ReflectionShadows", false ),
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
			Baked = (native.GetLightFlags() & 32) != 0 // LIGHTTYPE_FLAGS_BAKED
				&& light is not SceneDirectionalLight { IncludeStaticShadowCasters: true },
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

		if ( !View.IsSkybox )
		{
			view.GetRenderAttributesPtr().SetVectorValue( "FogSunColor", sceneObject.LightColor );
			view.GetRenderAttributesPtr().SetVectorValue( "FogSunDirection", sceneObject.WorldDirection );
		}

		return DoDirectionalLight( FromNative( sceneObject ) );
	}

	/// <summary>
	/// Renders shadow views through the native scenesystem, each a child of the mapper's current view.
	/// </summary>
	sealed class NativeShadowRenderer : IShadowRenderer
	{
		readonly ShadowMapper mapper;
		readonly CFrustum frustum = CFrustum.Create();

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
			// Reflections don't render contact shadows, and the main view's mask doesn't line up with them
			if ( !ContactShadows.Enabled || mapper.View.IsReflection || light is not SceneLight sceneLight || !sceneLight.ContactShadows )
				return 0;

			return sceneLight.GetShadowMask( mapper.SceneView ) is { } mask ? (uint)mask.Index : 0;
		}
	}
}
