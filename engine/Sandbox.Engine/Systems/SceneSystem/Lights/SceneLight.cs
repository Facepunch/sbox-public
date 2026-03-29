using NativeEngine;

namespace Sandbox;

/// <summary>
/// Base class for light scene objects for use with a <see cref="SceneWorld"/>.
/// </summary>
[Expose]
public class SceneLight : SceneObject
{
	internal CSceneLightObject lightNative;

	internal SceneLight() { }
	internal SceneLight( HandleCreationData _ ) { }

	/// <summary>
	/// Color and brightness of the light
	/// </summary>
	public Color LightColor
	{
		get { return lightNative.GetColor(); }
		set { lightNative.SetColor( value ); }
	}

	/// <summary>
	/// Radius of the light in units
	/// </summary>
	public float Radius
	{
		get { return lightNative.GetRadius(); }
		set
		{
			if ( Radius == value ) return;
			lightNative.SetRadius( value );
		}
	}

	/// <summary>
	/// The light attenuation constant term
	/// </summary>
	public float ConstantAttenuation
	{
		get { return lightNative.GetConstantAttn(); }
		set { lightNative.SetConstantAttn( value ); }
	}

	/// <summary>
	/// The light attenuation linear term
	/// </summary>
	public float LinearAttenuation
	{
		get { return lightNative.GetLinearAttn(); }
		set { lightNative.SetLinearAttn( value ); }
	}

	/// <summary>
	/// The light attenuation quadratic term
	/// </summary>
	public float QuadraticAttenuation
	{
		// Note: to make these numbers sane I'm doing some calculation here
		get { return lightNative.GetQuadraticAttn() * 10000.0f; }
		set { lightNative.SetQuadraticAttn( value / 10000.0f ); }
	}

	/// <summary>
	/// Get or set the resolution of the shadow map. If this is zero the engine will decide what it should use.
	/// </summary>
	public int ShadowTextureResolution
	{
		get { return lightNative.GetShadowTextureResolution(); }
		set { lightNative.SetShadowTextureResolution( value ); }
	}

	/// <summary>
	/// Enable or disable shadow rendering
	/// </summary>
	public bool ShadowsEnabled
	{
		get { return lightNative.GetShadows(); }
		set { lightNative.SetShadows( value ); }
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
			lightNative.SetLightCookie( value == null ? default : value.native );
		}
	}

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
		get => (LightShape)lightNative.GetLightShape();
		set => lightNative.SetLightShape( (LightSourceShape_t)value );
	}

	public Vector2 ShapeSize
	{
		set
		{
			lightNative.SetLightSourceDim0( value.x );
			lightNative.SetLightSourceDim1( value.y );
		}
	}

	public FogLightingMode FogLighting
	{
		get => (FogLightingMode)lightNative.GetFogLightingMode();
		set => lightNative.SetFogLightingMode( (int)value );
	}

	public float FogStrength
	{
		get => lightNative.GetFogContributionStength();
		set => lightNative.SetFogContributionStength( value );
	}

	/// <summary>
	/// Stupid fucking shit
	/// </summary>
	internal Vector3 WorldDirection => lightNative.GetWorldDirection();

	public float ShadowBias { get; set; } = 0.0005f;

	public float ShadowHardness { get; set; } = 0.0f;

	internal override void OnTransformChanged( in Transform tx )
	{
		base.OnTransformChanged( tx );

		lightNative.SetWorldDirection( tx.Rotation );
		lightNative.SetWorldPosition( tx.Position );
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
	}

	internal override void OnNativeDestroy()
	{
		lightNative = IntPtr.Zero;

		base.OnNativeDestroy();
	}
}
