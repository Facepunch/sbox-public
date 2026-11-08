using Basalt.Gpu.Source2;
using Basalt.Shadows;
using NativeEngine;
using Mapper = Basalt.Shadows.ShadowMapper;

namespace Sandbox.Rendering;

/// <summary>
/// The native scenesystem side of Basalt's <see cref="Mapper"/>, one per native light binner: lights and views come from
/// native, each shadow view is added to the scenesystem as a child of the view the shadows are for, and the shadow data
/// goes into the binner's buffers. Native and Basalt share the mapper's cache and time slicing.
/// </summary>
internal sealed class NativeShadowMapper
{
	readonly Mapper mapper = new();

	/// <summary>
	/// The native view this mapper's light binner is working on.
	/// </summary>
	ISceneView SceneView { get; set; }

	/// <summary>Directional light for this view.</summary>
	SceneLight DirectionalLight { get; set; }

	bool nativeViewInfoValid;

	GpuBuffer<Mapper.GPUProjectedShadow> projectedShadows;
	GpuBuffer<Mapper.GPUProjectedCubeShadow> projectedCubeShadows;

	public NativeShadowMapper()
	{
		mapper.Renderer = new NativeShadowRenderer( this );
	}

	internal void InitForView( ISceneView sceneView )
	{
		SceneView = sceneView;
		DirectionalLight = null;
		nativeViewInfoValid = false;

		mapper.InitForView();

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

		mapper.View = new ShadowViewInfo
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
		if ( !ShadowMapper.LocalShadowsEnabled )
			return Mapper.InvalidShadowIndex;

		UseNativeView( view );
		return mapper.FindOrCreateShadowMaps( FromNative( sceneObject ), flScreenSize );
	}

	internal int DoDirectionalLight( SceneLight sceneObject, ISceneView view )
	{
		DirectionalLight = sceneObject;
		UseNativeView( view );

		if ( !mapper.View.IsSkybox )
		{
			view.GetRenderAttributesPtr().SetVectorValue( "FogSunColor", sceneObject.LightColor );
			view.GetRenderAttributesPtr().SetVectorValue( "FogSunDirection", sceneObject.WorldDirection );
		}

		return mapper.DoDirectionalLight( FromNative( sceneObject ) );
	}

	internal unsafe void SetShaderAttributes( CRenderAttributes attributes )
	{
		if ( !attributes.IsValid )
			return;

		mapper.ResolveTextureIndices();
		EnsureBuffers();

		attributes.SetBufferValue( "ProjectedShadows", projectedShadows.native );
		attributes.SetBufferValue( "ProjectedCubeShadows", projectedCubeShadows.native );

		var directionalLight = mapper.DirectionalLightData;
		RenderTools.SetDynamicConstantBufferData( attributes, "DirectionalLightCB", Graphics.Context, (IntPtr)(&directionalLight), sizeof( GPUDirectionalLight ) );

		attributes.SetBoolValue( "DirectionalLightDebug", ShadowMapper.DebugEnabled );
	}

	/// <summary>
	/// Called as we submit display lists, upload our shadow map buffers.
	/// </summary>
	internal void UploadToGPU()
	{
		mapper.ResolveTextureIndices();
		EnsureBuffers();

		projectedShadows.SetData( mapper.ProjectedShadowData );
		projectedCubeShadows.SetData( mapper.ProjectedCubeShadowData );
	}

	/// <summary>
	/// This ties our buffers to a specific lightbinner. They're all the same size at the end of the day, we
	/// could pool them normally. Created on first use.
	/// </summary>
	void EnsureBuffers()
	{
		projectedShadows ??= new( Mapper.ProjectedShadowBufferSize );
		projectedCubeShadows ??= new( Mapper.ProjectedCubeShadowBufferSize );
	}

	internal void DisposeBuffers()
	{
		projectedShadows?.Dispose();
		projectedCubeShadows?.Dispose();
		projectedShadows = null;
		projectedCubeShadows = null;
	}

	/// <summary>
	/// <see cref="ContactShadows"/> for native's views. Called after Hi-Z (depth chain ready); bindless index set in CSM setup.
	/// </summary>
	internal void RenderScreenSpaceShadows( ISceneView view )
	{
		if ( SceneView != view || DirectionalLight is null || mapper.View.IsReflection )
			return;

		var light = DirectionalLight;
		if ( !ContactShadows.Enabled || !light.IsValid() || !light.ContactShadows )
			return;

		var mask = light.GetShadowMask( view );
		if ( mask is null )
			return;

		// Directional WorldDirection is already -Forward (CSceneLightObject::SetWorldDirection).
		ContactShadows.Render( mask, view.GetFrustum().GetReverseZViewProjTranspose(), light.WorldDirection, light.ShadowHardness );
	}

	/// <summary>
	/// Renders shadow views through the native scenesystem, each a child of the mapper's current view.
	/// </summary>
	sealed class NativeShadowRenderer : IShadowRenderer
	{
		readonly NativeShadowMapper owner;
		readonly CFrustum frustum = CFrustum.Create();

		public NativeShadowRenderer( NativeShadowMapper owner )
		{
			this.owner = owner;
		}

		public System.Numerics.Matrix4x4 RenderShadowView( in ShadowViewDesc view )
		{
			if ( view.Orthographic )
				frustum.InitOrthoCamera( view.Position, view.Rotation.Angles(), view.ZNear, view.ZFar, view.Width, view.Height );
			else
				frustum.BuildFrustumFromVectors( view.Position, view.ZNear, view.ZFar, view.FieldOfView, 1.0f, view.Rotation.Forward, view.Rotation.Left, view.Rotation.Up );

			CSceneSystem.AddShadowView( view.Name,
				owner.SceneView,
				frustum,
				new( 0, 0, view.Resolution, view.Resolution ),
				view.Target.Texture.Native(),
				view.Slice,
				view.StaticFilter == Basalt.StaticFilter.OnlyStatic ? SceneObjectFlags.StaticObject : SceneObjectFlags.None,
				view.StaticFilter == Basalt.StaticFilter.NoStatic ? SceneObjectFlags.StaticObject : SceneObjectFlags.None,
				view.DepthBias,
				view.SlopeScaledDepthBias,
				cachedShadowTexture: view.CachedStatic is null ? default : view.CachedStatic.Texture.Native() );

			return frustum.GetReverseZViewProjTranspose()._numerics;
		}

		public ShadowMap GetCascadeTarget( int cascade, int resolution )
		{
			// Temporary targets are loaned for the frame, so a second camera this frame gets its own
			var rt = RenderTarget.GetTemporary( resolution, resolution, ImageFormat.None, ImageFormat.D32 );
			return new ShadowMap( Source2Texture.From( rt.DepthTarget ) );
		}

		public uint GetShadowMaskIndex( object light )
		{
			// Reflections don't render contact shadows, and the main view's mask doesn't line up with them
			if ( !ContactShadows.Enabled || owner.mapper.View.IsReflection || light is not SceneLight sceneLight || !sceneLight.ContactShadows )
				return 0;

			return sceneLight.GetShadowMask( owner.SceneView ) is { } mask ? (uint)mask.Index : 0;
		}
	}
}
