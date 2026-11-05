
namespace Sandbox;

/// <summary>
/// Renders a trail for each particle in the effect.
/// </summary>
[Title( "Particle Trail Renderer" )]
[Category( "Effects" )]
[Icon( "category" )]
public sealed class ParticleTrailRenderer : ParticleController, Component.ExecuteInEditor
{
	[Property, Order( -100 ), InlineEditor( Label = false ), Group( "Advanced Rendering", StartFolded = true )] public RenderOptions RenderOptions { get; } = new RenderOptions( null );

	[Group( "Trail" )]
	[Property] public int MaxPoints { get; set; } = 64;

	[Group( "Trail" )]
	[Property] public float PointDistance { get; set; } = 8;

	[Group( "Trail" )]
	[Property] public float LifeTime { get; set; } = 2;

	[Group( "Appearance" )]
	[Property, InlineEditor( Label = false )] public TrailTextureConfig Texturing { get; set; } = TrailTextureConfig.Default;

	[Group( "Appearance" )]
	[Property] public Gradient Color { get; set; } = global::Color.Cyan;

	[Group( "Appearance" )]
	[Property] public Curve Width { get; set; } = 5;

	[Group( "Particles" )]
	[Property] public bool TintFromParticle { get; set; } = true;

	[Group( "Particles" )]
	[Property] public bool ScaleFromParticle { get; set; } = true;

	[Group( "Rendering" )]
	[Property] public bool Wireframe { get; set; }

	[Group( "Rendering" )]
	[Property] public bool Opaque { get; set; } = true;

	[ShowIf( "Opaque", true )]
	[Group( "Rendering" )]
	[Property] public bool CastShadows { get; set; } = false;

	[ShowIf( "Opaque", false )]
	[Group( "Rendering" )]
	[Property] public BlendMode BlendMode { get; set; } = BlendMode.Normal;

	/// <summary>
	/// Every trail we draw. Trails on live particles, plus the ones whose particle died that are still fading out.
	/// </summary>
	readonly List<TrailData> _trails = new();

	/// <summary>
	/// All of our trails are drawn by this one object, as a single dispatch and draw.
	/// </summary>
	SceneLineObject _so;

	int[] _offsets = [];
	int[] _counts = [];
	BBox[] _bounds = [];

	internal Material _defaultMaterial;

	protected override void OnParticleCreated( Particle p )
	{
		if ( !Graphics.IsAvailable )
			return;

		p.AddListener( new ParticleTrail( this ), this );
	}

	/// <summary>
	/// Create a trail for a particle. Called from the particle threads.
	/// </summary>
	internal TrailData CreateTrail()
	{
		var trail = new TrailData
		{
			MaxPoints = MaxPoints,
			PointDistance = PointDistance,
			LifeTime = LifeTime,
			Texturing = Texturing,
			TrailColor = Color,
			Width = Width,
		};

		lock ( _trails )
		{
			_trails.Add( trail );
		}

		return trail;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( !Texturing.Material.IsValid() )
		{
#pragma warning disable CS0618
			if ( Texturing.Texture.IsValid() )
			{
				_defaultMaterial.Set( "g_tColor", Texturing.Texture );
#pragma warning restore CS0618
			}
			else
			{
				_defaultMaterial.Set( "g_tColor", Texture.White );
			}
		}
	}

	/// <summary>
	/// The particle step is done, so the live trails have their new points. Fade the orphaned
	/// trails and rebuild the batch.
	/// </summary>
	protected override void OnAfterStep( float delta )
	{
		base.OnAfterStep( delta );

		if ( !_so.IsValid() )
			return;

		lock ( _trails )
		{
			var dt = Time.Delta;

			System.Threading.Tasks.Parallel.For( 0, _trails.Count, i =>
			{
				var trail = _trails[i];
				if ( trail.Orphaned ) trail.AdvanceTime( dt );
			} );

			_trails.RemoveAll( static t => t.Orphaned && t.IsEmpty );

			Build();
		}
	}

	void Build()
	{
		var trailCount = _trails.Count;

		if ( _offsets.Length < trailCount )
		{
			var size = Math.Max( trailCount, _offsets.Length * 2 );
			Array.Resize( ref _offsets, size );
			Array.Resize( ref _counts, size );
			Array.Resize( ref _bounds, size );
		}

		int total = 0;
		for ( int i = 0; i < trailCount; i++ )
		{
			_offsets[i] = total;
			total += _trails[i].PointCount;
		}

		var points = _so.ReservePoints( total );

		System.Threading.Tasks.Parallel.For( 0, trailCount, i =>
		{
			var trail = _trails[i];
			_counts[i] = trail.WriteStrip( points.AsSpan( _offsets[i], trail.PointCount ), ref _bounds[i] );
		} );

		int pointCount = 0;
		BBox bounds = default;

		for ( int i = 0; i < trailCount; i++ )
		{
			var count = _counts[i];
			if ( count == 0 ) continue;

			if ( _offsets[i] != pointCount )
			{
				Array.Copy( points, _offsets[i], points, pointCount, count );
			}

			bounds = pointCount == 0 ? _bounds[i] : bounds.AddBBox( _bounds[i] );
			pointCount += count;
		}

		if ( pointCount > 0 )
		{
			_so.Transform = new Transform( bounds.Center );
		}

		_so.Tags.SetFrom( GameObject.Tags );
		_so.Material = Texturing.Material.IsValid() ? Texturing.Material : _defaultMaterial;
		_so.Flags.CastShadows = Opaque && CastShadows;
		_so.Opaque = Opaque;
		_so.Attributes.SetComboEnum( "D_BLENDMODE", BlendMode );
		_so.Wireframe = Wireframe;

		var samplerState = new Rendering.SamplerState { Filter = Texturing.FilterMode, AddressModeU = Texturing.TextureAddressMode, AddressModeV = Texturing.TextureAddressMode };
		if ( _so.SamplerState != samplerState )
		{
			_so.SamplerState = samplerState;
		}

		RenderOptions.Apply( _so );

		_so.SetStrips( pointCount, bounds );
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();

		_defaultMaterial = Material.Load( "materials/default/default_line.vmat" ).CreateCopy();

		if ( Graphics.IsAvailable )
		{
			_so = new SceneLineObject( Scene.SceneWorld );
		}
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		lock ( _trails )
		{
			_trails.Clear();
		}

		_so?.Delete();
		_so = null;
	}
}


class ParticleTrail : Particle.BaseListener
{
	public ParticleTrailRenderer Renderer;

	TrailData trail;

	public ParticleTrail( ParticleTrailRenderer renderer )
	{
		Renderer = renderer;
	}

	public override void OnEnabled( Particle p )
	{
		trail = Renderer.CreateTrail();
	}

	public override void OnDisabled( Particle p )
	{
		if ( trail is null ) return;

		if ( !Renderer.Active )
		{
			trail.Clear();
		}

		trail.Orphaned = true;
	}

	public override void OnUpdate( Particle p, float dt )
	{
		if ( trail is null ) return;

		if ( Renderer.TintFromParticle )
			trail.LineTint = p.Color;

		if ( Renderer.ScaleFromParticle )
			trail.LineScale = p.Size.x;

		trail.TryAddPosition( p.Position );
		trail.AdvanceTime( Time.Delta );
	}
}
