using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// A decal. Use the Component.
/// </summary>
/// <remarks>
/// Managed only: it has no native object, and its settings live here.
/// </remarks>
internal sealed class DecalSceneObject : SceneObject
{
	Texture colorTexture, normalTexture, rmoTexture, heightTexture, emissionTexture;
	Vector4 color;
	uint sortOrder, exclusionBitMask, sequenceIndex;
	float attenuationAngle, colorMix, emissionEnergy, parallaxStrength, coverageAmount, coverageRange;
	int samplerIndex;

	public DecalBlendMode BlendMode
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			Attributes.Set( "DecalBlendMode", (int)value );
			NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}

	internal DecalSceneObject() { }

	public Texture ColorTexture
	{
		get => colorTexture;
		set => Set( ref colorTexture, value );
	}

	public Texture NormalTexture
	{
		get => normalTexture;
		set => Set( ref normalTexture, value );
	}

	public Texture RMOTexture
	{
		get => rmoTexture;
		set => Set( ref rmoTexture, value );
	}

	public Texture HeightTexture
	{
		get => heightTexture;
		set => Set( ref heightTexture, value );
	}

	public Color Color
	{
		get => color;
		set => Set( ref color, value );
	}

	public uint SortOrder
	{
		get => sortOrder;
		set => Set( ref sortOrder, value );
	}

	public uint ExclusionBitMask
	{
		get => exclusionBitMask;
		set => Set( ref exclusionBitMask, value );
	}

	public Texture EmissionTexture
	{
		get => emissionTexture;
		set => Set( ref emissionTexture, value );
	}

	public float AttenuationAngle
	{
		get => attenuationAngle;
		set => Set( ref attenuationAngle, value );
	}

	public float ColorMix
	{
		get => colorMix;
		set => Set( ref colorMix, value );
	}

	public float EmissionEnergy
	{
		get => emissionEnergy;
		set => Set( ref emissionEnergy, value );
	}

	public uint SequenceIndex
	{
		get => sequenceIndex;
		set => Set( ref sequenceIndex, value );
	}

	public float ParallaxStrength
	{
		get => parallaxStrength;
		set => Set( ref parallaxStrength, value );
	}

	public int SamplerIndex
	{
		get => samplerIndex;
		set => Set( ref samplerIndex, value );
	}

	public float CoverageAmount
	{
		get => coverageAmount;
		set => Set( ref coverageAmount, value );
	}

	public float CoverageRange
	{
		get => coverageRange;
		set => Set( ref coverageRange, value );
	}

	// Decal sets every setting every frame: only a change is written, or each would rewrite the render object
	void Set<T>( ref T field, T value )
	{
		if ( EqualityComparer<T>.Default.Equals( field, value ) ) return;
		field = value;
		SettingsChanged();
	}

	public DecalSceneObject( SceneWorld world )
	{
		Assert.IsValid( world );

		MakeDecal( world );
	}

	/// <summary>
	/// What native's decal object started as (opaque, visible, not batchable, no bounds until it's placed), with its defaults for
	/// the settings.
	/// </summary>
	void MakeDecal( SceneWorld world )
	{
		color = new Vector4( 1, 1, 1, 1 );
		attenuationAngle = 1.0f;
		emissionEnergy = 1.0f;
		colorMix = 1.0f;
		parallaxStrength = 0.25f;
		coverageAmount = 1.0f;
		coverageRange = 0.07f;

		MakeManaged( world, null, Rendering.SceneObjectFlags.IsOpaque, batchable: false );
		KeepFixedBounds( false );
	}

	/// <summary>A unit box around its transform.</summary>
	internal override void OnTransformChanged( in Transform tx )
	{
		base.OnTransformChanged( tx );
		KeepBounds( TransformBounds( tx, new BBox( new Vector3( -0.5f ), new Vector3( 0.5f ) ) ) );
	}

	void SettingsChanged() => NotifyChanged( Rendering.SceneObjectChange.Settings );

	internal override bool OwnsRenderObject => true;

	internal override Borealis.RenderObject MakeRenderObject( Rendering.ISceneWorldRender render ) => new Borealis.DecalObject( Transform );

	/// <summary>
	/// Its box and everything it projects, as <c>CLightBinnerStandard</c> reads a <c>CDecalSceneObject</c>.
	/// </summary>
	internal override void WriteRender( Borealis.RenderObject obj, Rendering.SceneObjectChange change, Rendering.ISceneWorldRender render )
	{
		base.WriteRender( obj, change, render );
		var decal = (Borealis.DecalObject)obj;

		if ( (change & Rendering.SceneObjectChange.Transform) != 0 )
			decal.Transform = Transform == default ? Transform.Zero : Transform;

		if ( (change & Rendering.SceneObjectChange.Visibility) != 0 )
			decal.Visible = RenderingEnabled;

		if ( (change & Rendering.SceneObjectChange.Settings) != 0 )
		{
			decal.Color = render.Texture( colorTexture );
			decal.Normal = render.Texture( normalTexture );
			decal.RoughnessMetalnessOcclusion = render.Texture( rmoTexture );
			decal.Height = render.Texture( heightTexture );
			decal.Emission = render.Texture( emissionTexture );
			decal.Tint = Color;
			decal.SortOrder = sortOrder;
			decal.ExclusionBitMask = exclusionBitMask;
			decal.AttenuationAngle = attenuationAngle;
			decal.EmissionEnergy = emissionEnergy;
			decal.SequenceIndex = sequenceIndex;
			decal.ColorMix = colorMix;
			decal.BlendMode = (Borealis.DecalBlendMode)BlendMode;
			decal.ParallaxStrength = parallaxStrength;
			decal.SamplerIndex = samplerIndex;
			decal.CoverageAmount = coverageAmount;
			decal.CoverageRange = coverageRange;
		}
	}
}
