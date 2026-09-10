namespace Sandbox;

/// <summary>
/// A line effect that travels from this component's position to <see cref="EndPoint"/> - a bullet
/// tracer or beam. Drives a <see cref="SceneLineObject"/> and an optional light that travels with the
/// line. Implements <see cref="ITargetedEffect"/> so a weapon model can aim it at the hit point.
/// </summary>
[Title( "Tracer Effect" )]
[Category( "Effects" )]
[Icon( "north_east" )]
[Alias( "Tracer" )]
public sealed class TracerEffect : Renderer, Component.ExecuteInEditor, Component.ITemporaryEffect, ITargetedEffect
{
	/// <summary>
	/// Where the line travels to - a fixed world position, or anchored to an object it follows.
	/// </summary>
	[Property, Feature( "Tracer" ), Header( "Position" )] public SceneAnchor EndPoint { get; set; }

	/// <summary>
	/// <see cref="ITargetedEffect"/>: the destination the line travels to.
	/// </summary>
	public void SetTarget( SceneAnchor target ) => EndPoint = target;

	/// <summary>
	/// <see cref="ITargetedEffect"/>: where the line starts from.
	/// </summary>
	public void SetStartPoint( SceneAnchor start ) => WorldPosition = start.Position;

	/// <summary>How fast the line travels, in units per second.</summary>
	[Property, Feature( "Tracer" ), Header( "Speed" )] public float DistancePerSecond { get; set; } = 5000f;

	/// <summary>Length of the visible line.</summary>
	[Property, Feature( "Tracer" )] public float Length { get; set; } = 100f;

	/// <summary>
	/// How far along the travel the line starts - skips the stretch right out of the muzzle.
	/// </summary>
	[Property, Feature( "Tracer" )] public float StartDistance { get; set; } = 200f;

	/// <summary>Colour along the line - 0 is the leading edge, 1 the tail.</summary>
	[Property, Feature( "Tracer" ), Header( "Rendering" )] public Gradient LineColor { get; set; } = new Gradient( new Gradient.ColorFrame( 0, Color.White ), new Gradient.ColorFrame( 1, Color.White.WithAlpha( 0 ) ) );

	/// <summary>Width along the line - 0 is the leading edge, 1 the tail.</summary>
	[Property, Feature( "Tracer" )] public Curve LineWidth { get; set; } = new Curve( new Curve.Frame( 0, 2 ), new Curve.Frame( 1, 0 ) );

	/// <summary>Cap shape at the leading end of the line.</summary>
	[Property, Feature( "Tracer" )] public SceneLineObject.CapStyle StartCap { get; set; }

	/// <summary>Cap shape at the tail end of the line.</summary>
	[Property, Feature( "Tracer" )] public SceneLineObject.CapStyle EndCap { get; set; }

	/// <summary>Render the line opaque rather than blended.</summary>
	[Property, Feature( "Tracer" )] public bool Opaque { get; set; } = true;

	/// <summary>The line casts shadows.</summary>
	[Property, Feature( "Tracer" )] public bool CastShadows { get; set; } = true;

	/// <summary>Material for the line. Empty uses the default line material.</summary>
	[Property, Feature( "Tracer" )] public Material Material { get; set; }

	/// <summary>A point light that travels with the line.</summary>
	[Property, FeatureEnabled( "Light", Icon = "💡" )]
	public bool EnableLight { get; set; }

	/// <summary>Light colour over the travel - 0 at the start, 1 at the end point.</summary>
	[Property, Feature( "Light" )]
	public Gradient LightColor { get; set; } = new Gradient( new Gradient.ColorFrame( 0, Color.White ), new Gradient.ColorFrame( 1, Color.White ) );

	/// <summary>Light radius over the travel - 0 at the start, 1 at the end point.</summary>
	[Property, Feature( "Light" )]
	public Curve LightRadius { get; set; } = new Curve( new Curve.Frame( 0, 128 ), new Curve.Frame( 0.5f, 256 ), new Curve.Frame( 1, 128 ) );

	/// <summary>Where along the visible line the light sits - 0 the leading edge, 1 the tail.</summary>
	[Property, Feature( "Light" ), Range( 0, 1 )]
	public float LightPosition { get; set; } = 0;

	bool ITemporaryEffect.IsActive => !_finished;

	float _distance;
	bool _finished;

	SceneLineObject _so;
	SceneLight _light;

	// Legacy support for old texture based renderers. Shared - every tracer sets the same values on
	// it, so there's no point copying the material per tracer.
	static Material _defaultMaterial;

	static Material DefaultMaterial
	{
		get
		{
			if ( _defaultMaterial is null )
			{
				_defaultMaterial = Material.Load( "materials/default/default_line.vmat" )?.CreateCopy();
				_defaultMaterial?.Set( "Color", Texture.White );
			}

			return _defaultMaterial;
		}
	}

	protected override void OnEnabled()
	{
		_so = new SceneLineObject( Scene.SceneWorld );
		_so.Transform = WorldTransform;

		_distance = StartDistance;
		_finished = false;
	}

	protected override void OnDisabled()
	{
		_so?.Delete();
		_so = null;

		_light?.Delete();
		_light = null;
	}

	protected override void OnUpdate()
	{
		_distance += DistancePerSecond * Time.Delta;

		// Finished once the tail has passed the end point. Editor previews loop instead.
		if ( _distance - Length > WorldPosition.Distance( EndPoint.Position ) )
		{
			if ( Scene.IsEditor )
			{
				_distance = StartDistance;
				return;
			}

			_finished = true;
		}
	}

	protected override void OnPreRender()
	{
		if ( !_so.IsValid() )
			return;

		var start = WorldPosition;
		var travel = EndPoint.Position - start;
		var maxlen = travel.Length;

		// The whole line has arrived - hide it until we're deleted (or loop again, in the editor).
		if ( _distance - Length > maxlen )
		{
			_so.RenderingEnabled = false;

			_light?.Delete();
			_light = null;
			return;
		}

		var direction = travel.Normal;

		UpdateLight( start, direction, maxlen );

		_so.RenderingEnabled = true;
		_so.Transform = WorldTransform;
		_so.Flags.CastShadows = CastShadows;
		_so.StartCap = StartCap;
		_so.EndCap = EndCap;
		_so.Opaque = Opaque;
		_so.Material = Material.IsValid() ? Material : DefaultMaterial;
		_so.Attributes.SetCombo( "D_BLEND", Opaque ? 0 : 1 );

		// Build the line leading edge (0) to tail (1), each point clamped to the travel range so the
		// line squashes against the ends instead of overshooting.
		const int segments = 10;

		_so.StartLine();

		for ( var i = 0; i <= segments; i++ )
		{
			var x = i / (float)segments;
			var along = (_distance - Length * x).Clamp( 0, maxlen );

			_so.AddLinePoint( start + direction * along, LineColor.Evaluate( x ), LineWidth.Evaluate( x ) );
		}

		_so.EndLine();
	}

	void UpdateLight( Vector3 start, Vector3 direction, float maxlen )
	{
		if ( !EnableLight )
		{
			_light?.Delete();
			_light = null;
			return;
		}

		// Fade colour and radius by how far along the travel the middle of the line is.
		var delta = maxlen <= 0f ? 1f : (_distance - Length * 0.5f).Clamp( 0, maxlen ) / maxlen;

		_light ??= new ScenePointLight( Scene.SceneWorld );
		_light.Transform = WorldTransform;
		_light.QuadraticAttenuation = 10;
		_light.ShadowsEnabled = false;
		_light.LightColor = LightColor.Evaluate( delta );
		_light.Radius = LightRadius.Evaluate( delta );
		_light.Position = start + direction * (_distance - Length * LightPosition).Clamp( 0, MathF.Max( 0, maxlen - 5 ) );
	}
}
