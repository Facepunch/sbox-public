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
		if ( d.LightModulatorOffset == offset && d.LightModulatorCount == count ) return;
		d.LightModulatorOffset = offset;
		d.LightModulatorCount = count;
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
		set { Change().Color = value; Recalculate(); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
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
			if ( HasLight ) lightNative.SetRadius( value );
			NotifyChanged( Rendering.SceneObjectChange.Settings );
			BoundsVersion++;
		}
	}

	/// <summary>
	/// The light attenuation constant term
	/// </summary>
	public float ConstantAttenuation
	{
		get { return Description.Attenuation0; }
		set { Change().Attenuation0 = value; Recalculate(); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	/// <summary>
	/// The light attenuation linear term
	/// </summary>
	public float LinearAttenuation
	{
		get { return Description.Attenuation1; }
		set { Change().Attenuation1 = value; Recalculate(); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	/// <summary>
	/// The light attenuation quadratic term
	/// </summary>
	public float QuadraticAttenuation
	{
		// Note: to make these numbers sane I'm doing some calculation here
		get { return Description.Attenuation2 * 10000.0f; }
		set { Change().Attenuation2 = value / 10000.0f; Recalculate(); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
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

		NotifyChanged( Rendering.SceneObjectChange.Settings );
	}

	/// <summary>
	/// Get or set the resolution of the shadow map. If this is zero the engine will decide what it should use.
	/// </summary>
	public int ShadowTextureResolution
	{
		get { return Description.ShadowWidth; }
		set { ref var d = ref Change(); d.ShadowWidth = value; d.ShadowHeight = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	/// <summary>
	/// Enable or disable shadow rendering
	/// </summary>
	public bool ShadowsEnabled
	{
		get { return Description.CastShadows != 0; }
		set { Change().CastShadows = value ? 1 : 0; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	private Texture _lightCookie;

	/// <summary>
	/// Access the LightCookie - which is a texture that gets drawn over the light
	/// </summary>
	public Texture LightCookie
	{
		get => _lightCookie ??= HasLight ? Texture.FromNative( lightNative.GetLightCookie() ) : null;
		set
		{
			_lightCookie = value;
			NotifyChanged( Rendering.SceneObjectChange.Settings );
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
			NotifyChanged( Rendering.SceneObjectChange.Settings );
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
			NotifyChanged( Rendering.SceneObjectChange.Settings );
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
			NotifyChanged( Rendering.SceneObjectChange.Settings );
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
		set { Change().Shape = (int)value; Recalculate(); if ( HasLight ) lightNative.SetLightShape( (LightSourceShape_t)value ); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public Vector2 ShapeSize
	{
		set
		{
			ref var d = ref Change();
			d.LightSourceDim0 = value.x;
			d.LightSourceDim1 = value.y;
			Recalculate();
			if ( HasLight )
			{
				lightNative.SetLightSourceDim0( value.x );
				lightNative.SetLightSourceDim1( value.y );
			}

			NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}

	public FogLightingMode FogLighting
	{
		get => (FogLightingMode)Description.FogLightingMode;
		set { Change().FogLightingMode = (int)value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	public float FogStrength
	{
		get => Description.FogContributionStrength;
		set { Change().FogContributionStrength = value; NotifyChanged( Rendering.SceneObjectChange.Settings ); }
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

		if ( HasLight )
		{
			lightNative.SetWorldDirection( tx.Rotation );
			lightNative.SetWorldPosition( tx.Position );
		}

		BoundsVersion++;
	}

	[Obsolete( "Use ScenePointLight (or stop fucking using SceneObjects at all)" )]
	public SceneLight( SceneWorld sceneWorld, Vector3 position, float radius, Color color )
	{
		Assert.IsValid( sceneWorld );

		MakeManagedLight( sceneWorld, NewDescription( TypePoint ) );

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

	/// <summary>
	/// Made by managed code: in its world, its renderer and its world's lights as <see cref="OnNativeInit"/> puts one native
	/// code made, with the description and flags native's <c>CSceneSystem::CreateLight</c> gave a new light, and its bounds
	/// worked out here from then on (<see cref="DescriptionBounds"/>).
	/// </summary>
	private protected void MakeManagedLight( SceneWorld world, in LightDescription description,
		Rendering.SceneObjectFlags flags = Rendering.SceneObjectFlags.IsLight | Rendering.SceneObjectFlags.IsDirectLight )
	{
		desc = description;
		described = true;
		MakeManaged( world, null, flags );

		// CreateLight: a directional light's bounds infinite, the others' from the description
		if ( desc.Type == TypeDirectional ) KeepFixedBounds( true );
		else KeepBounds( DescriptionBounds( desc ) );

		lock ( World.InternalSceneObjects )
		{
			World.InternalSceneLights.Add( this );
		}
	}

	/// <summary>Whether it has a native light: one native code made (a tool's, Hammer's).</summary>
	private protected bool HasLight => !lightNative.IsNull;

	internal override void Destroyed()
	{
		lock ( World.InternalSceneObjects )
		{
			World.InternalSceneLights.Remove( this );
		}

		base.Destroyed();
	}

	internal override void OnNativeDestroy()
	{
		lightNative = IntPtr.Zero;

		base.OnNativeDestroy();
	}
}
