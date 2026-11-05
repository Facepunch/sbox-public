using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Filters direct light passing through a textured rectangle or infinite plane in the local Y/Z plane.
/// This changes lighting on receivers; add a separate renderer to display the glass itself.
/// </summary>
[Expose, Title( "Light Modulator" ), Category( "Light" ), Icon( "filter" )]
public sealed class LightModulator : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// Limits how the processed texture can change each colour channel of direct light.
	/// </summary>
	[Expose]
	public enum ModulationBlendMode
	{
		/// <summary>
		/// Multiply light by the processed texture, allowing both attenuation and amplification.
		/// </summary>
		Multiply,

		/// <summary>
		/// Keep multipliers at or above one. Raise brightness above one to produce highlights.
		/// </summary>
		Lighten,

		/// <summary>
		/// Keep multipliers at or below one, allowing shadows without boosting light.
		/// </summary>
		Darken
	}

	/// <summary>
	/// Built-in animation of the projected texture, evaluated in the shader.
	/// </summary>
	public enum ModulationEffect
	{
		/// <summary>Sample the texture once using its existing mapping controls.</summary>
		None,
		/// <summary>Combine two moving, distorted samples into changing caustic highlights.</summary>
		Caustics,
		/// <summary>Layer drifting texture samples into cloud shadows.</summary>
		CloudShadows,
		/// <summary>Rotate the texture continuously around its center.</summary>
		Rotate,
		/// <summary>Fold the texture into rotating mirrored sectors.</summary>
		Kaleidoscope,
		/// <summary>Separate red and blue samples around the green sample.</summary>
		ChromaticSplit,
		/// <summary>Distort the texture with expanding circular waves.</summary>
		Ripple,
		/// <summary>Cycle the texture's brightness smoothly.</summary>
		Pulse,
		/// <summary>Move the texture continuously along U and V.</summary>
		Scroll
	}

	/// <summary>
	/// Multiply applies the texture directly; Lighten only boosts light and Darken only reduces it.
	/// Applied per RGB channel after contrast, brightness and tint, before strength and alpha.
	/// </summary>
	[Property, Title( "Mode" ), Order( -100 ), EnumButtonGroup]
	public ModulationBlendMode BlendMode
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Blend between original light at zero and full texture modulation at one.
	/// Multiplies texture alpha and edge feathering rather than dimming the light itself.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float Strength
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1;

	/// <summary>
	/// Fade inward from each rectangle edge, as a fraction of its width or height.
	/// Zero gives a hard boundary; 0.1 fades across the outer ten percent on each axis.
	/// Uses the physical rectangle, independent of texture tiling or scrolling.
	/// </summary>
	[Property, Range( 0, 0.5f ), HideIf( nameof( Infinite ), true )]
	public float EdgeFeather
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Fade in modulation over this many world units from the rectangle's plane.
	/// Uses perpendicular distance for both surfaces and fog, independent of light angle or object scale.
	/// Zero disables distance feathering; full strength is reached at this distance and beyond.
	/// </summary>
	[Property, Range( 0, 1024 )]
	public float DistanceFeather
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// RGB transmission through the rectangle. Alpha blends between unfiltered and filtered light.
	/// Sampling uses the texture's own colour space.
	/// </summary>
	[Property]
	public Texture Texture
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Texture repeats across the rectangle, or per 128 world units with World Sized UVs enabled.
	/// Negative values mirror the texture.
	/// Scaling and rotation are centered on the rectangle.
	/// </summary>
	[Property, Group( "Texture Mapping" )]
	public Vector2 Tiling
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = Vector2.One;

	/// <summary>
	/// Keep texture density independent of rectangle size and object scale.
	/// Tiling specifies repeats per 128 world units along the plane's axes, centered on its origin.
	/// </summary>
	[Property, Group( "Texture Mapping" ), Title( "World Sized UVs" )]
	public bool WorldSizedUVs
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Texture-coordinate offset in repeats, applied after tiling and rotation.
	/// </summary>
	[Property, Group( "Texture Mapping" )]
	public Vector2 Offset
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Scroll effect movement in repeats per second along U and V. Zero leaves the projection still.
	/// Runs in the shader without rebuilding textures or light candidate lists each frame.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Speed (repeats/sec)" ), Range( -1, 1 ), ShowIf( nameof( Effect ), ModulationEffect.Scroll )]
	public Vector2 ScrollSpeed
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = new( 0.1f, 0 );

	/// <summary>
	/// Animate the projected texture before contrast, brightness, tint and blending.
	/// None preserves the existing texture mapping.
	/// </summary>
	[Property, Group( "Animation" )]
	public ModulationEffect Effect
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Animation speed multiplier. Zero freezes caustic motion; negative values reverse it.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Speed" ), Range( -10, 10 ), ShowIf( nameof( Effect ), ModulationEffect.Caustics )]
	public float CausticsSpeed
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1;

	/// <summary>
	/// Maximum UV displacement in texture repeats.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Wobble" ), Range( 0, 0.25f ), ShowIf( nameof( Effect ), ModulationEffect.Caustics )]
	public float CausticsWobble
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 0.025f;

	/// <summary>
	/// Scale of the second caustic sample relative to the first.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Layer Scale" ), Range( 0.1f, 4 ), ShowIf( nameof( Effect ), ModulationEffect.Caustics )]
	public float CausticsLayerScale
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1.13f;

	/// <summary>
	/// Offset in caustic animation time, in seconds at the default speed.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Time Offset" ), Range( -60, 60 ), ShowIf( nameof( Effect ), ModulationEffect.Caustics )]
	public float CausticsTimeOffset
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Cloud drift speed multiplier. Zero freezes the clouds; negative values reverse them.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Speed" ), Range( -10, 10 ), ShowIf( nameof( Effect ), ModulationEffect.CloudShadows )]
	public float CloudShadowSpeed
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1;

	/// <summary>
	/// Blend from white transmission at zero to the full cloud texture at one, before colour adjustments.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Shadow Depth" ), Range( 0, 1 ), ShowIf( nameof( Effect ), ModulationEffect.CloudShadows )]
	public float CloudShadowDepth
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1;

	/// <summary>
	/// Scale of the second cloud sample relative to the first.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Layer Scale" ), Range( 0.1f, 4 ), ShowIf( nameof( Effect ), ModulationEffect.CloudShadows )]
	public float CloudShadowLayerScale
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1.7f;

	/// <summary>
	/// Offset in cloud animation time, in seconds at the default speed.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Time Offset" ), Range( -60, 60 ), ShowIf( nameof( Effect ), ModulationEffect.CloudShadows )]
	public float CloudShadowTimeOffset
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Clockwise rotation speed in degrees per second. Negative values rotate counterclockwise.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Speed (degrees/sec)" ), Range( -360, 360 ), ShowIf( nameof( Effect ), ModulationEffect.Rotate )]
	public float RotationSpeed
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 36;

	/// <summary>
	/// Offset in rotation animation time, in seconds at the default speed.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Time Offset" ), Range( -60, 60 ), ShowIf( nameof( Effect ), ModulationEffect.Rotate )]
	public float RotationTimeOffset
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Rotation speed of the mirrored pattern in degrees per second.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Speed (degrees/sec)" ), Range( -360, 360 ), ShowIf( nameof( Effect ), ModulationEffect.Kaleidoscope )]
	public float KaleidoscopeSpeed
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 36;

	/// <summary>
	/// Blend from the original UV mapping at zero to fully mirrored sectors at one.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Fold Amount" ), Range( 0, 1 ), ShowIf( nameof( Effect ), ModulationEffect.Kaleidoscope )]
	public float KaleidoscopeBlend
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1;

	/// <summary>
	/// Number of mirrored sectors around the center of the texture.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Sector Count" ), Range( 1, 64 ), ShowIf( nameof( Effect ), ModulationEffect.Kaleidoscope )]
	public int KaleidoscopeSegments
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 6;

	/// <summary>
	/// Offset in kaleidoscope animation time, in seconds at the default speed.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Time Offset" ), Range( -60, 60 ), ShowIf( nameof( Effect ), ModulationEffect.Kaleidoscope )]
	public float KaleidoscopeTimeOffset
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Rotation speed of the red/blue separation direction in radians per second.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Speed (radians/sec)" ), Range( -10, 10 ), ShowIf( nameof( Effect ), ModulationEffect.ChromaticSplit )]
	public float ChromaticSplitSpeed
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1;

	/// <summary>
	/// UV offset in texture repeats for the red and blue samples on either side of green.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Channel Separation" ), Range( 0, 0.1f ), ShowIf( nameof( Effect ), ModulationEffect.ChromaticSplit )]
	public float ChromaticSplitSeparation
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 0.01f;

	/// <summary>
	/// Offset in chromatic split animation time, in seconds at the default speed.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Time Offset" ), Range( -60, 60 ), ShowIf( nameof( Effect ), ModulationEffect.ChromaticSplit )]
	public float ChromaticSplitTimeOffset
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Wave animation speed multiplier. Zero freezes the ripples; negative values reverse them.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Speed" ), Range( -10, 10 ), ShowIf( nameof( Effect ), ModulationEffect.Ripple )]
	public float RippleSpeed
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1;

	/// <summary>
	/// Maximum radial UV displacement in texture repeats.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Displacement" ), Range( 0, 0.25f ), ShowIf( nameof( Effect ), ModulationEffect.Ripple )]
	public float RippleDisplacement
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 0.025f;

	/// <summary>
	/// Radial wave frequency multiplier. Higher values produce more closely spaced waves.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Wave Frequency" ), Range( 0.1f, 10 ), ShowIf( nameof( Effect ), ModulationEffect.Ripple )]
	public float RippleFrequency
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1;

	/// <summary>
	/// Offset in ripple animation time, in seconds at the default speed.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Time Offset" ), Range( -60, 60 ), ShowIf( nameof( Effect ), ModulationEffect.Ripple )]
	public float RippleTimeOffset
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Brightness cycles per second. Zero freezes the pulse; negative values reverse it.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Speed (cycles/sec)" ), Range( -10, 10 ), ShowIf( nameof( Effect ), ModulationEffect.Pulse )]
	public float PulseSpeed
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1;

	/// <summary>
	/// Brightness variation, from constant brightness at zero to a full zero-to-one pulse at one.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Pulse Depth" ), Range( 0, 1 ), ShowIf( nameof( Effect ), ModulationEffect.Pulse )]
	public float PulseDepth
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1;

	/// <summary>
	/// Shape of the brightness pulse. One gives a sine wave; higher values narrow the bright interval.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Sharpness" ), Range( 0.1f, 8 ), ShowIf( nameof( Effect ), ModulationEffect.Pulse )]
	public float PulseSharpness
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1;

	/// <summary>
	/// Offset in pulse animation time, in seconds at the default speed.
	/// </summary>
	[Property, Group( "Animation" ), Title( "Time Offset" ), Range( -60, 60 ), ShowIf( nameof( Effect ), ModulationEffect.Pulse )]
	public float PulseTimeOffset
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Clockwise rotation of the projected texture in degrees, around its center.
	/// </summary>
	[Property, Group( "Texture Mapping" )]
	public float TextureRotation
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// RGB contrast around linear 0.5, before brightness and tint. One preserves the texture.
	/// Alpha is unaffected; negative RGB results are clamped to zero.
	/// </summary>
	[Property, Group( "Texture Adjustments" ), Range( 0, 8 )]
	public float Contrast
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1;

	/// <summary>
	/// Linear brightness multiplier after contrast. One preserves brightness; values above one boost light.
	/// Alpha is unaffected. This is an artistic light multiplier, not physical light focusing.
	/// </summary>
	[Property, Group( "Texture Adjustments" ), Range( 0, 16 )]
	public float Brightness
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = 1;

	/// <summary>
	/// Extend the rectangle to an infinite plane, without aperture clipping or edge feathering.
	/// Sidedness and distance feathering still apply.
	/// </summary>
	[Property, Title( "Infinite Plane" )]
	public bool Infinite
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Rectangle width along local Y and height along local Z, before object scaling.
	/// For an infinite plane, this is the UV reference size when World Sized UVs is disabled.
	/// </summary>
	[Property, HideIf( nameof( Infinite ), true )]
	public Vector2 Size
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = new( 64, 96 );

	/// <summary>
	/// Linear RGB multiplier, including values above one for artistic caustics.
	/// </summary>
	[Property, ColorUsage( false, true )]
	public Color Tint
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = Color.White;

	/// <summary>
	/// Applies the filter in both directions. Otherwise light travels along local positive X only.
	/// </summary>
	[Property]
	public bool TwoSided
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	} = true;

	/// <summary>
	/// Explicit texture mip used by divergent light queries; larger values soften the projection.
	/// </summary>
	[Property, Range( 0, 12 )]
	public float MipLevel
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Softens transmission as light travels from the glass to the receiving surface or fog sample.
	/// Adds this many mip levels at 128 world units, twice as many at 384 units. Zero disables distance softening.
	/// </summary>
	[Property, Range( 0, 4 )]
	public float DistanceMipBias
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			InvalidateData();
		}
	}

	/// <summary>
	/// Build orthogonal world-space axes and reciprocal extents for intersection and UV mapping.
	/// </summary>
	internal bool BuildData( out LightModulatorData data )
	{
		var texture = Texture;
		texture?.MarkUsed();
		var textureIndex = texture?.Index ?? 0;
		if ( dataDirty || textureIndex != cachedTextureIndex )
		{
			cachedDataValid = PackData( out cachedData );
			cachedTextureIndex = textureIndex;
			dataDirty = false;
			DataBuildCount++;
		}

		data = cachedData;
		if ( AnimationTime is float time && float.IsFinite( time ) )
		{
			data.EffectParameters.x = 0;
			data.EffectParameters.w = time;
		}
		return cachedDataValid;
	}

	/// <summary>
	/// Optional procedural-effect clock supplied by an owner, independent of the viewing camera's render time.
	/// Null uses the camera's render clock. Does not affect texture scrolling.
	/// Geometry and appearance packing remain cached.
	/// </summary>
	internal float? AnimationTime { get; set; }

	bool dataDirty = true;
	bool cachedDataValid;
	int cachedTextureIndex;
	LightModulatorData cachedData;

	/// <summary>
	/// Number of packed rectangle snapshots, for diagnosing redundant render preparation.
	/// </summary>
	internal int DataBuildCount { get; private set; }

	void InvalidateData()
	{
		dataDirty = true;
	}

	/// <summary>
	/// Track transform changes, including movement inherited from a parent.
	/// </summary>
	protected override void OnEnabled()
	{
		Transform.OnTransformChanged += InvalidateData;
		InvalidateData();
	}

	/// <summary>
	/// Stop tracking transforms while inactive and discard the cached snapshot.
	/// </summary>
	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= InvalidateData;
		InvalidateData();
	}

	bool PackData( out LightModulatorData data )
	{
		data = default;
		if ( !Active )
			return false;

		var texture = Texture;
		if ( texture is null )
			return false;

		var size = Infinite && WorldSizedUVs ? new Vector2( 128, 128 ) : Size;
		if ( size.x <= 0 || size.y <= 0 )
			return false;

		var width = MathF.Abs( size.x * WorldScale.y );
		var height = MathF.Abs( size.y * WorldScale.z );
		if ( !float.IsFinite( width ) || !float.IsFinite( height ) || width < 0.001f || height < 0.001f )
			return false;

		data.Origin = WorldPosition;
		data.TextureIndex = texture.Index;
		data.U = WorldRotation.Left * MathF.Sign( WorldScale.y ) / width;
		data.V = WorldRotation.Up * MathF.Sign( WorldScale.z ) / height;
		data.Normal = WorldRotation.Forward;
		data.TwoSided = TwoSided ? 1 : 0;
		data.HalfWidth = width * 0.5f;
		data.HalfHeight = height * 0.5f;
		data.WorldSizedUVs = WorldSizedUVs ? 1 : 0;
		data.Infinite = Infinite ? 1 : 0;
		data.Tint = new Vector3( Tint.r, Tint.g, Tint.b );
		data.MipLevel = FiniteClamp( MipLevel, 0, 12 );
		data.DistanceMipBias = FiniteClamp( DistanceMipBias, 0, 4 );
		data.Tiling = new Vector2( FiniteClamp( Tiling.x, -1024, 1024, 1 ), FiniteClamp( Tiling.y, -1024, 1024, 1 ) );
		data.Offset = new Vector2( FiniteClamp( Offset.x, -1000000, 1000000 ), FiniteClamp( Offset.y, -1000000, 1000000 ) );
		data.ScrollSpeed = Effect == ModulationEffect.Scroll
			? new Vector2( FiniteClamp( ScrollSpeed.x, -1024, 1024 ), FiniteClamp( ScrollSpeed.y, -1024, 1024 ) )
			: Vector2.Zero;
		var angle = float.IsFinite( TextureRotation ) ? TextureRotation % 360 * (MathF.PI / 180) : 0;
		data.Rotation = new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) );
		data.Contrast = FiniteClamp( Contrast, 0, 8, 1 );
		data.Brightness = FiniteClamp( Brightness, 0, 16, 1 );
		data.BlendMode = Enum.IsDefined( BlendMode ) ? (int)BlendMode : 0;
		data.Strength = FiniteClamp( Strength, 0, 1, 1 );
		data.EdgeFeather = FiniteClamp( EdgeFeather, 0, 0.5f );
		data.DistanceFeather = FiniteClamp( DistanceFeather, 0, 1000000 );
		data.Effect = Enum.IsDefined( Effect ) ? (int)Effect : 0;
		data.EffectParameters = Effect switch
		{
			ModulationEffect.Caustics => new(
				FiniteClamp( CausticsSpeed, -10, 10, 1 ),
				FiniteClamp( CausticsWobble, 0, 0.25f, 0.025f ) / 0.025f,
				FiniteClamp( CausticsLayerScale, 0.1f, 4, 1.13f ) / 1.13f,
				FiniteClamp( CausticsTimeOffset, -60, 60 ) ),
			ModulationEffect.CloudShadows => new(
				FiniteClamp( CloudShadowSpeed, -10, 10, 1 ),
				FiniteClamp( CloudShadowDepth, 0, 1, 1 ),
				FiniteClamp( CloudShadowLayerScale, 0.1f, 4, 1.7f ) / 1.7f,
				FiniteClamp( CloudShadowTimeOffset, -60, 60 ) ),
			ModulationEffect.Rotate => new(
				FiniteClamp( RotationSpeed, -360, 360, 36 ) / 36,
				1, 1, FiniteClamp( RotationTimeOffset, -60, 60 ) ),
			ModulationEffect.Kaleidoscope => new(
				FiniteClamp( KaleidoscopeSpeed, -360, 360, 36 ) / 36,
				FiniteClamp( KaleidoscopeBlend, 0, 1, 1 ),
				Math.Clamp( KaleidoscopeSegments, 1, 64 ) / 6.0f,
				FiniteClamp( KaleidoscopeTimeOffset, -60, 60 ) ),
			ModulationEffect.ChromaticSplit => new(
				FiniteClamp( ChromaticSplitSpeed, -10, 10, 1 ),
				FiniteClamp( ChromaticSplitSeparation, 0, 0.1f, 0.01f ) / 0.01f,
				1, FiniteClamp( ChromaticSplitTimeOffset, -60, 60 ) ),
			ModulationEffect.Ripple => new(
				FiniteClamp( RippleSpeed, -10, 10, 1 ),
				FiniteClamp( RippleDisplacement, 0, 0.25f, 0.025f ) / 0.025f,
				FiniteClamp( RippleFrequency, 0.1f, 10, 1 ),
				FiniteClamp( RippleTimeOffset, -60, 60 ) ),
			ModulationEffect.Pulse => new(
				FiniteClamp( PulseSpeed, -10, 10, 1 ),
				FiniteClamp( PulseDepth, 0, 1, 1 ),
				FiniteClamp( PulseSharpness, 0.1f, 8, 1 ),
				FiniteClamp( PulseTimeOffset, -60, 60 ) ),
			_ => new( 1, 1, 1, 0 )
		};
		return data.TextureIndex > 0;
	}

	/// <summary>
	/// Keep shader controls finite even when assigned directly by scripts.
	/// </summary>
	static float FiniteClamp( float value, float minimum, float maximum, float fallback = 0 )
	{
		return float.IsFinite( value ) ? Math.Clamp( value, minimum, maximum ) : fallback;
	}

	/// <summary>
	/// Shows the physical rectangle or a grid for an infinite plane.
	/// </summary>
	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = Color.Cyan;
		if ( Infinite )
		{
			for ( int i = -128; i <= 128; i += 64 )
			{
				Gizmo.Draw.Line( new Vector3( 0, i, -128 ), new Vector3( 0, i, 128 ) );
				Gizmo.Draw.Line( new Vector3( 0, -128, i ), new Vector3( 0, 128, i ) );
			}
			return;
		}
		Gizmo.Draw.LineBBox( new BBox( new Vector3( -0.1f, -Size.x * 0.5f, -Size.y * 0.5f ), new Vector3( 0.1f, Size.x * 0.5f, Size.y * 0.5f ) ) );
	}
}

/// <summary>
/// Scalar-layout GPU rectangle. Keep synchronized with common/classes/LightModulators.hlsl.
/// </summary>
[StructLayout( LayoutKind.Sequential, Pack = 4 )]
internal struct LightModulatorData
{
	internal Vector3 Origin;
	internal int TextureIndex;
	internal Vector3 U;
	internal float HalfWidth;
	internal Vector3 V;
	internal float HalfHeight;
	internal Vector3 Normal;
	internal int TwoSided;
	internal Vector3 Tint;
	internal float MipLevel;
	internal float DistanceFeather;
	internal float DistanceMipBias;
	internal Vector2 Tiling;
	internal Vector2 Offset;
	internal Vector2 ScrollSpeed;
	internal Vector2 Rotation;
	internal float Contrast;
	internal float Brightness;
	internal int BlendMode;
	internal float Strength;
	internal float EdgeFeather;
	internal int Effect;
	internal Vector4 EffectParameters;
	internal int WorldSizedUVs;
	internal int Infinite;

	/// <summary>
	/// Conservative point/spot broad phase: distance to the rectangle or infinite plane versus light range.
	/// A spot's enclosing range sphere deliberately permits false positives, never false negatives.
	/// </summary>
	internal readonly bool IntersectsLight( Vector3 position, float radius )
	{
		var delta = position - Origin;
		if ( Infinite != 0 )
			return MathF.Abs( Vector3.Dot( delta, Normal ) ) <= radius;

		var u = U * (2 * HalfWidth);
		var v = V * (2 * HalfHeight);
		var closest = Origin
			+ u * Math.Clamp( Vector3.Dot( delta, u ), -HalfWidth, HalfWidth )
			+ v * Math.Clamp( Vector3.Dot( delta, v ), -HalfHeight, HalfHeight );

		return (closest - position).LengthSquared <= radius * radius;
	}
}
