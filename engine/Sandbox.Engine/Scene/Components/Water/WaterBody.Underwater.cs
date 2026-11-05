using Sandbox.Rendering;
using Sandbox.Volumes;

namespace Sandbox;

public sealed partial class WaterBody
{
	/// <summary>
	/// Apply this body's underwater fog, colour grading, vignette and blur beneath the rendered surface.
	/// Each camera selects its containing water body independently.
	/// </summary>
	[Property, FeatureEnabled( nameof( Underwater ), Icon = "water" )]
	public bool Underwater { get; set; } = true;

	/// <summary>
	/// Tint underwater fog and the vignette with <see cref="UnderwaterFogColor"/> instead of the material's water
	/// fog colour, so the view below the surface can differ from the view down through it.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Fog" ), Order( -81 ), Title( "Custom Fog Color" )]
	public bool OverrideUnderwaterFogColor { get; set; }

	/// <summary>
	/// Tint of underwater fog and the vignette, when <see cref="OverrideUnderwaterFogColor"/> is on.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Fog" ), Order( -80 ), Title( "Fog Color" ), ColorUsage( hasAlpha: false, isHDR: false )]
	[ShowIf( nameof( OverrideUnderwaterFogColor ), true )]
	public Color UnderwaterFogColor { get; set; } = new( 0.01499f, 0.03256f, 0.02787f );

	/// <summary>
	/// Clear distance before underwater fog starts, in world inches.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Fog" ), Order( -79 ), Title( "Fog Start Distance" ), Range( 0, 4096 )]
	public float UnderwaterFogNearDistance
	{
		get;
		set => field = float.IsFinite( value ) ? MathF.Max( value, 0 ) : 0;
	}

	/// <summary>
	/// Distance beyond the fog start that removes approximately 63 percent of background light, in world inches.
	/// Smaller values give denser fog, independently of the screen effects and surface opacity.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Fog" ), Order( -78 ), Title( "Fog Attenuation Distance" ), Range( 1, 4096 )]
	public float UnderwaterFogAttenuationDistance
	{
		get;
		set => field = float.IsFinite( value ) ? MathF.Max( value, 1 ) : 128;
	} = 128;

	/// <summary>
	/// Maximum fog coverage. Zero removes fog coverage without disabling the other underwater effects.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Fog" ), Order( -77 ), Title( "Fog Maximum Opacity" ), Range( 0, 1 )]
	public float UnderwaterFogMaximumOpacity
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, 0, 1 ) : 1;
	} = 1;

	/// <summary>
	/// How far the view below the surface is desaturated, darkened and given contrast. Zero removes the grade.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Screen Effects" ), Order( -72 ), Title( "Color Grade Strength" ), Range( 0, 1 )]
	public float UnderwaterColorGrade
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, 0, 1 ) : 0.5f;
	} = 0.5f;

	/// <summary>
	/// Darkening toward the screen's edges in the fog colour. Zero removes it.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Screen Effects" ), Order( -71 ), Title( "Vignette Strength" ), Range( 0, 1 )]
	public float UnderwaterVignette
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, 0, 1 ) : 0.5f;
	} = 0.5f;

	/// <summary>
	/// Blur of the view below the surface. Zero removes it.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Screen Effects" ), Order( -70 ), Title( "Blur Strength" ), Range( 0, 1 )]
	public float UnderwaterBlur
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, 0, 1 ) : 0.5f;
	} = 0.5f;

	GameObject _underwaterObject;
	WaterPostProcessVolume _underwaterVolume;
	Fog _underwaterFog;
	Vignette _underwaterVignette;
	ColorAdjustments _underwaterColors;
	Blur _underwaterBlur;
	UnderwaterPostProcess _underwaterDistortion;
	static readonly List<WaterBody> _underwaterQueries = [];

	/// <summary>
	/// Test the volume against the rendered wave surface, respecting client rendering quality.
	/// Audio can exclude the exact surface boundary while sharing the same containment query.
	/// </summary>
	internal bool ContainsView( Vector3 position, bool includeSurface = true )
	{
		if ( !ContainsViewBounds( position ) )
			return false;

		float depth = ViewSurfaceDepth( position );
		return includeSurface ? depth >= 0 : depth > 0;
	}

	/// <summary>
	/// Test active volume bounds before evaluating the rendered wave surface.
	/// </summary>
	internal bool ContainsViewBounds( Vector3 position )
	{
		return Active && WorldScale.x != 0 && WorldScale.y != 0 && WorldScale.z != 0
			&& InFootprint( position ) && SurfaceDistance( position ) >= -WorldDepth;
	}

	/// <summary>
	/// Select one underwater appearance per camera position, preferring finite water over an ocean.
	/// The optional bounded body has already passed its underwater and volume bounds tests at this position.
	/// </summary>
	internal static WaterBody FindUnderwaterView( Scene scene, Vector3 position, WaterBody boundedBody = null )
	{
		WaterBody result = null;
		_underwaterQueries.Clear();
		scene.GetAll( _underwaterQueries );
		foreach ( var water in _underwaterQueries )
		{
			if ( !water.Underwater )
				continue;

			bool contains = water == boundedBody ? water.ViewSurfaceDepth( position ) >= 0 : water.ContainsView( position );
			if ( !contains )
				continue;

			result ??= water;
			if ( !water.Ocean )
			{
				result = water;
				break;
			}
		}
		_underwaterQueries.Clear();
		return result;
	}

	/// <summary>
	/// Configure native fog and post-processing beneath an owned volume before enabling it.
	/// </summary>
	void CreateUnderwater()
	{
		_underwaterObject = new GameObject( GameObject, false, "Water underwater volume" );
		_underwaterObject.Flags = GameObjectFlags.NotSaved | GameObjectFlags.Hidden | GameObjectFlags.NotNetworked;
		_underwaterVolume = _underwaterObject.Components.Create<WaterPostProcessVolume>();
		_underwaterVolume.Body = this;
		_underwaterVolume.Priority = 10;
		_underwaterVolume.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };
		_underwaterVolume.BlendDistance = 0;
		_underwaterVolume.EditorPreview = true;
		_underwaterFog = _underwaterObject.Components.Create<Fog>();
		_underwaterFog.Mode = FogMode.Exponential;
		_underwaterFog.SunLightingEnabled = false;
		_underwaterVignette = _underwaterObject.Components.Create<Vignette>();
		_underwaterVignette.Smoothness = 1;
		_underwaterVignette.Roundness = 0.335f;
		_underwaterColors = _underwaterObject.Components.Create<ColorAdjustments>();
		_underwaterBlur = _underwaterObject.Components.Create<Blur>();
		_underwaterDistortion = _underwaterObject.Components.Create<UnderwaterPostProcess>();
	}

	/// <summary>
	/// Follow the body and apply its fog and screen effects. Native volume tests select effects per camera.
	/// </summary>
	void UpdateUnderwater()
	{
		if ( !Underwater || Application.IsDedicatedServer
			|| WorldScale.x == 0 || WorldScale.y == 0 || WorldScale.z == 0 )
		{
			DestroyUnderwater();
			return;
		}

		if ( !_underwaterObject.IsValid() )
			CreateUnderwater();

		_underwaterObject.WorldTransform = new Transform( SurfaceOrigin, WorldRotation, 1 );
		var color = OverrideUnderwaterFogColor ? UnderwaterFogColor.WithAlpha( 1 ) : WaterFogColor( ActiveMaterial );
		_underwaterFog.Color = color;
		_underwaterFog.StartDistance = UnderwaterFogNearDistance;
		_underwaterFog.MaximumOpacity = UnderwaterFogMaximumOpacity;
		_underwaterFog.AttenuationDistance = UnderwaterFogAttenuationDistance;
		_underwaterVignette.Color = color;
		_underwaterVignette.Intensity = 0.9f * UnderwaterVignette;
		_underwaterColors.Saturation = 1 - 0.6f * UnderwaterColorGrade;
		_underwaterColors.Brightness = 1 - 0.35f * UnderwaterColorGrade;
		_underwaterColors.Contrast = 1 + 0.12f * UnderwaterColorGrade;
		_underwaterBlur.Size = 0.17f * UnderwaterBlur;
		_underwaterDistortion.Strength = UnderwaterDistortion;
		_underwaterDistortion.Speed = UnderwaterDistortionSpeed;
		_underwaterDistortion.TimeOffset = UnderwaterDistortionTimeOffset;
		_underwaterObject.Enabled = true;
	}

	/// <summary>
	/// Disable immediately and release the owned effects when the body, volume or underwater feature is disabled.
	/// </summary>
	void DestroyUnderwater()
	{
		if ( _underwaterObject.IsValid() )
		{
			_underwaterObject.Enabled = false;
			_underwaterObject.Destroy();
		}
		_underwaterObject = null;
		_underwaterVolume = null;
		_underwaterFog = null;
		_underwaterVignette = null;
		_underwaterColors = null;
		_underwaterBlur = null;
		_underwaterDistortion = null;
	}
}
