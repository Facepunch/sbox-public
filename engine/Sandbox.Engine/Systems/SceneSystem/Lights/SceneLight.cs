using NativeEngine;

namespace Sandbox;

/// <summary>
/// Base class for light scene objects for use with a <see cref="SceneWorld"/>.
/// </summary>
[Expose]
public partial class SceneLight : SceneObject
{
	internal CSceneLightObject lightNative;

	/// <summary>
	/// Changes when the light moves or its range changes, invalidating cached spatial queries.
	/// </summary>
	internal int BoundsVersion { get; private set; }

	/// <summary>
	/// Updates the scene-owned modulation candidates and the managed renderer's light mirror.
	/// </summary>
	internal void SetModulatorRange( uint offset, uint count )
	{
		ref var d = ref Change();
		d.LightModulatorOffset = offset;
		d.LightModulatorCount = count;
		if ( WritesNative ) lightNative.SetLightModulatorRange( offset, count );
		NotifyChanged( Rendering.SceneObjectChange.Settings );
	}

	internal SceneLight() { }
	internal SceneLight( HandleCreationData _ ) { }

	/// <summary>
	/// Color and brightness of the light
	/// </summary>
	public Color LightColor
	{
		get { return Description.Color; }
		set { Change().Color = value; Recalculate(); if ( WritesNative ) lightNative.SetColor( value ); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	/// <summary>
	/// Radius of the light in units
	/// </summary>
	public float Radius
	{
		get { return Description.Range; }
		set
		{
			if ( Radius == value ) return;
			Change().Range = value; Recalculate();
			lightNative.SetRadius( value ); NotifyChanged( Rendering.SceneObjectChange.Settings );
			BoundsVersion++;
		}
	}

	/// <summary>
	/// The light attenuation constant term
	/// </summary>
	public float ConstantAttenuation
	{
		get { return Description.Attenuation0; }
		set { Change().Attenuation0 = value; Recalculate(); if ( WritesNative ) lightNative.SetConstantAttn( value ); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	/// <summary>
	/// The light attenuation linear term
	/// </summary>
	public float LinearAttenuation
	{
		get { return Description.Attenuation1; }
		set { Change().Attenuation1 = value; Recalculate(); if ( WritesNative ) lightNative.SetLinearAttn( value ); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	/// <summary>
	/// The light attenuation quadratic term
	/// </summary>
	public float QuadraticAttenuation
	{
		// Note: to make these numbers sane I'm doing some calculation here
		get { return Description.Attenuation2 * 10000.0f; }
		set { Change().Attenuation2 = value / 10000.0f; Recalculate(); if ( WritesNative ) lightNative.SetQuadraticAttn( value / 10000.0f ); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	/// <summary>
	/// Makes this a baked light addressed by probe/lightmap index textures (0-254).
	/// Pass -1 to restore dynamic lighting. Index 255 is reserved for an empty texture channel.
	/// </summary>
	public void SetBakedLightIndex( int index, bool mixedShadows = false )
	{
		if ( index is < -1 or > 254 )
			throw new ArgumentOutOfRangeException( nameof( index ) );
		ref var d = ref Change();
		d.BakeLightIndex = index;
		d.BakeLightIndexScale = 1.0f;
		d.UsesIndexedBakedLighting = index >= 0 ? 1 : 0;
		d.Flags &= ~(FlagMixedShadows | FlagBaked);
		if ( index >= 0 ) d.Flags |= FlagBaked | (mixedShadows ? FlagMixedShadows : 0u);

		if ( WritesNative )
		{
			lightNative.SetBakeLightIndex( index );
			lightNative.SetBakeLightIndexScale( 1.0f );
			lightNative.SetUsesIndexedBakedLighting( index >= 0 );
			lightNative.SetLightFlags( d.Flags );
		}

		NotifyChanged( Rendering.SceneObjectChange.Settings );
	}

	/// <summary>
	/// Get or set the resolution of the shadow map. If this is zero the engine will decide what it should use.
	/// </summary>
	public int ShadowTextureResolution
	{
		get { return Description.ShadowWidth; }
		set { ref var d = ref Change(); d.ShadowWidth = value; d.ShadowHeight = value; if ( WritesNative ) lightNative.SetShadowTextureResolution( value ); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	/// <summary>
	/// Enable or disable shadow rendering
	/// </summary>
	public bool ShadowsEnabled
	{
		get { return Description.CastShadows != 0; }
		set { Change().CastShadows = value ? 1 : 0; if ( WritesNative ) lightNative.SetShadows( value ); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	private Texture _lightCookie;

	/// <summary>
	/// Access the LightCookie - which is a texture that gets drawn over the light
	/// </summary>
	public Texture LightCookie
	{
		get => _lightCookie ??= Texture.FromNative( lightNative.GetLightCookie() );
		set
		{
			_lightCookie = value;
			if ( WritesNative ) lightNative.SetLightCookie( value == null ? default : value.native ); NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}

	/// <summary>
	/// Should this light contribute diffuse lighting?
	/// </summary>
	public bool RenderDiffuse
	{
		get => _renderDiffuse;
		set
		{
			_renderDiffuse = value;
			Change().RenderDiffuse = value ? 1 : 0;
			if ( WritesNative ) lightNative.SetRenderDiffuse( value ); NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}
	private bool _renderDiffuse = true;

	/// <summary>
	/// Should this light contribute specular highlights?
	/// </summary>
	public bool RenderSpecular
	{
		get => _renderSpecular;
		set
		{
			_renderSpecular = value;
			Change().RenderSpecular = value ? 1 : 0;
			if ( WritesNative ) lightNative.SetRenderSpecular( value ); NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}
	private bool _renderSpecular = true;

	/// <summary>
	/// Should this light contribute transmissive lighting (light passing through surfaces)?
	/// </summary>
	public bool RenderTransmissive
	{
		get => _renderTransmissive;
		set
		{
			_renderTransmissive = value;
			Change().RenderTransmissive = value ? 1 : 0;
			if ( WritesNative ) lightNative.SetRenderTransmissive( value ); NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}
	private bool _renderTransmissive = true;

	public enum FogLightingMode
	{
		None,
		Baked,
		Dynamic,
		DynamicNoShadows
	}

	public enum LightShape
	{
		Sphere,
		Capsule,
		Rectangle
	}

	public LightShape Shape
	{
		get => (LightShape)Description.Shape;
		set { Change().Shape = (int)value; Recalculate(); lightNative.SetLightShape( (LightSourceShape_t)value ); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public Vector2 ShapeSize
	{
		set
		{
			ref var d = ref Change();
			d.LightSourceDim0 = value.x;
			d.LightSourceDim1 = value.y;
			Recalculate();
			lightNative.SetLightSourceDim0( value.x );
			lightNative.SetLightSourceDim1( value.y );
			NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}

	public FogLightingMode FogLighting
	{
		get => (FogLightingMode)Description.FogLightingMode;
		set { Change().FogLightingMode = (int)value; if ( WritesNative ) lightNative.SetFogLightingMode( (int)value ); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public float FogStrength
	{
		get => Description.FogContributionStrength;
		set { Change().FogContributionStrength = value; if ( WritesNative ) lightNative.SetFogContributionStength( value ); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	/// <summary>
	/// Stupid fucking shit
	/// </summary>
	internal Vector3 WorldDirection => Description.Direction;

	public float ShadowBias
	{
		get;
		set { field = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public float ShadowHardness
	{
		get;
		set { field = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	/// <summary>
	/// Should this light generate screen-space contact shadows on top of its shadow maps?
	/// </summary>
	internal bool ContactShadows
	{
		get;
		set { field = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	/// <summary>
	/// Get or create screen-space shadow mask for this light and view, valid for the current frame only.
	/// </summary>
	internal Texture GetShadowMask( ISceneView view )
	{
		// Only managed cameras reach OnRenderStage, so only they ever clear/dispatch into the mask.
		// Purely native views (cubemap bakes, capture views) must get no mask, or the CSM pass
		// samples uninitialized contents instead of "fully lit".
		var cameraId = view.m_ManagedCameraId;
		if ( cameraId == 0 )
			return null;

		// Our SSS assumes a perspective light coordinate, so orthographic views get no mask.
		if ( view.GetFrustum().IsOrthographic() )
			return null;

		var vp = view.GetMainViewport();

		int width = (int)vp.Rect.Width;
		int height = (int)vp.Rect.Height;

		if ( width < 1 || height < 1 )
			return null;

		// the loan is released immediately - the stable id means both same-frame calls
		// (CSM setup + the mask pass) resolve to the same cached RT and bindless index. Hold the
		// loan for the frame if anything else ever starts requesting same-size temporaries by name.
		using var rt = RenderTarget.GetTemporary( width, height, ImageFormat.A8, ImageFormat.None,
			MultisampleAmount.MultisampleNone, 1, "ShadowMask", HashCode.Combine( (nint)lightNative, cameraId ) );
		return rt.ColorTarget;
	}

	internal override void OnTransformChanged( in Transform tx )
	{
		base.OnTransformChanged( tx );

		// As native's SetWorldDirection then SetWorldPosition; both reach native, since its bounds come from them
		ref var d = ref Change();
		var forward = tx.Rotation.Forward;
		if ( d.Type == TypeDirectional ) forward = -forward;
		d.Direction = NormalizedSafe( forward, new Vector3( 1, 0, 0 ) );
		d.Up = NormalizedSafe( tx.Rotation.Up, new Vector3( 0, 0, 1 ) );
		d.Position = tx.Position;
		Recalculate();

		lightNative.SetWorldDirection( tx.Rotation );
		lightNative.SetWorldPosition( tx.Position );
		BoundsVersion++;
	}

	[Obsolete( "Use ScenePointLight (or stop fucking using SceneObjects at all)" )]
	public SceneLight( SceneWorld sceneWorld, Vector3 position, float radius, Color color )
	{
		Assert.IsValid( sceneWorld );

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CSceneSystem.CreatePointLight( sceneWorld );
		}

		Position = position;
		Radius = radius;
		LightColor = color;
		QuadraticAttenuation = 1.0f;
	}

	[Obsolete( "Use ScenePointLight (or stop fucking using SceneObjects at all)" )]
	public SceneLight( SceneWorld sceneWorld ) : this( sceneWorld, Vector3.Zero, 100, Color.White * 10.0f )
	{

	}

	internal override void OnNativeInit( CSceneObject ptr )
	{
		base.OnNativeInit( ptr );

		lightNative = (CSceneLightObject)ptr;

		lock ( World.InternalSceneObjects )
		{
			World.InternalSceneLights.Add( this );
		}
	}

	internal override void OnNativeDestroy()
	{
		lock ( World.InternalSceneObjects )
		{
			World.InternalSceneLights.Remove( this );
		}

		lightNative = IntPtr.Zero;

		base.OnNativeDestroy();
	}
}
