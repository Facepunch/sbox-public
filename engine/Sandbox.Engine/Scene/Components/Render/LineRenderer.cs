using System.Buffers;

namespace Sandbox;

/// <summary>
/// Renders a line between a list of points
/// </summary>
[Expose]
[Title( "Line Renderer" )]
[Category( "Rendering" )]
[Icon( "show_chart" )]
public sealed class LineRenderer : Renderer, Component.ExecuteInEditor
{
	SceneLineObject _so;

	[Group( "Points" )]
	[Property] public bool UseVectorPoints { get; set; }

	[Group( "Points" ), ShowIf( "UseVectorPoints", false )]
	[Property] public List<GameObject> Points { get; set; }

	[Group( "Points" ), ShowIf( "UseVectorPoints", true )]
	[Property] public List<Vector3> VectorPoints { get; set; }

	[Group( "Points" )]
	[Property] public SceneLineObject.FaceMode Face { get; set; }

	[Group( "Appearance" )]
	[Property] public Gradient Color { get; set; } = global::Color.Cyan;

	[Group( "Appearance" )]
	[Property] public Curve Width { get; set; } = 5;

	[Group( "Appearance" )]
	[Property, InlineEditor( Label = false )] public TrailTextureConfig Texturing { get; set; } = TrailTextureConfig.Default;

	[Group( "Spline" )]
	[Property, Range( 1, 32 )] public int SplineInterpolation { get; set; }

	[Group( "Spline" )]
	[Property, Range( -1, 1 )] public float SplineTension { get; set; }

	[Group( "Spline" )]
	[Property, Range( -1, 1 )] public float SplineContinuity { get; set; }

	[Group( "Spline" )]
	[Property, Range( -1, 1 )] public float SplineBias { get; set; }

	[Group( "Spline" ), HideIf( nameof( Face ), SceneLineObject.FaceMode.Camera )]
	[Property] public bool AutoCalculateNormals { get; set; } = true;

	[Group( "End Caps" ), HideIf( nameof( Face ), SceneLineObject.FaceMode.Cylinder )]
	[Property] public SceneLineObject.CapStyle StartCap { get; set; }

	[Group( "End Caps" ), HideIf( nameof( Face ), SceneLineObject.FaceMode.Cylinder )]
	[Property] public SceneLineObject.CapStyle EndCap { get; set; }

	[Group( "Rendering" )]
	[Property] public bool Wireframe { get; set; }

	[Group( "Rendering" )]
	[Property] public bool Opaque { get; set; } = true;

	[Group( "Rendering" )]
	[Property] public bool Additive { get; set; }

	[Group( "Rendering" )]
	[Property] public bool CastShadows { get; set; } = true;

	[Group( "Rendering" )]
	[Property] public float DepthFeather { get; set; }

	[Group( "Rendering" )]
	[Property, Range( 0, 1 )] public float FogStrength { get; set; } = 1.0f;

	[Group( "Rendering" )]
	[Property] public bool Lighting { get; set; }

	[Group( "Rendering" ), ShowIf( nameof( Face ), SceneLineObject.FaceMode.Cylinder )]
	[Property, Range( 3, 32 )] public int CylinderSegments { get; set; } = 12;

	private float scrollTime;

	protected override void OnEnabled()
	{
		if ( Application.IsHeadless )
			return;

		// Legacy support for old texture based renderers
		_defaultMaterial = Material.Load( "materials/default/default_line.vmat" ).CreateCopy();

		_so = new SceneLineObject( Scene.SceneWorld );
		_so.Transform = WorldTransform;
		_so.RenderingEnabled = false;
		_so.Tags.SetFrom( Tags );
		OnSceneObjectCreated( _so );

		scrollTime = 0;
	}

	protected override void OnDisabled()
	{
		BackupRenderAttributes( _so?.Attributes );
		_so?.Delete();
		_so = null;
	}

	List<Vector3> _points;
	List<Vector3> _normals;

	private Material _defaultMaterial;

	protected override void OnPreRender()
	{
		if ( !_so.IsValid() )
			return;

		if ( !UseVectorPoints )
		{
			if ( Points is null )
			{
				_so.RenderingEnabled = false;
				return;
			}

			_points ??= new List<Vector3>();
			_normals ??= new List<Vector3>();

			_points.Clear();
			_normals.Clear();

			_points.AddRange( Points
					.Where( x => x.IsValid() && x.Active )
					.Select( x => x.WorldPosition ) );

			_normals.AddRange( Points
					.Where( x => x.IsValid() && x.Active )
					.Select( x => x.WorldRotation.Up ) );
		}
		else
		{
			if ( VectorPoints is null )
			{
				_so.RenderingEnabled = false;
				return;
			}

			_points ??= new List<Vector3>();
			_points.Clear();
			_points.AddRange( VectorPoints );

			_normals = null;
		}

		var count = _points.Count();
		if ( count <= 1 )
		{
			_so.RenderingEnabled = false;
			return;
		}

		var transform = WorldTransform;

		_so.StartCap = StartCap;
		_so.EndCap = EndCap;
		_so.Face = Face;
		_so.Wireframe = Wireframe;
		_so.SamplerState = new() { Filter = Texturing.FilterMode, AddressModeU = Texturing.TextureAddressMode, AddressModeV = Texturing.TextureAddressMode };

		_so.RenderingEnabled = true;
		_so.Transform = transform;
		_so.Flags.CastShadows = CastShadows;
		_so.Lighting = Lighting;

		if ( Texturing.Material.IsValid() )
		{
			_so.Material = Texturing.Material;
		}
		else
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
			_so.Material = _defaultMaterial;
		}

		_so.Attributes.Set( "g_DepthFeather", DepthFeather );
		_so.Attributes.Set( "g_FogStrength", FogStrength );
		_so.Attributes.SetCombo( "D_BLEND", Additive ? 1 : 0 );
		_so.Attributes.SetCombo( "D_OPAQUE", Opaque ? 1 : 0 );
		_so.Attributes.Set( "g_bNonDirectionalDiffuseLighting", true );

		_so.Flags.IsOpaque = Opaque;
		_so.Flags.IsTranslucent = !Opaque;
		_so.Flags.WantsPrePass = Opaque && Lighting && DepthFeather == 0.0f;

		RenderOptions.Apply( _so );

		_so.StartLine();

		scrollTime += Time.Delta * Texturing.Scroll;

		int interpolation = SplineInterpolation.Clamp( 1, 100 );

		_so.TessellationLevel = Face == SceneLineObject.FaceMode.Cylinder ? CylinderSegments.Clamp( 3, 32 ) : 1;

		if ( count == 2 || interpolation == 1 )
		{
			Vector3[] rmfNormals = null;
			if ( AutoCalculateNormals && Face != SceneLineObject.FaceMode.Camera )
			{
				rmfNormals = ArrayPool<Vector3>.Shared.Rent( _points.Count );
				CalculateRMFNormals( _points.ToArray(), rmfNormals.AsSpan( 0, _points.Count ) );
			}

			int i = 0;
			var distance = 0.0f;

			for ( i = 0; i < _points.Count; i++ )
			{
				var p = _points[i];
				if ( i > 0 ) distance += _points[i - 1].Distance( p );

				var delta = i / (float)(count - 1);
				var uv = 0.0f;

				if ( !Texturing.WorldSpace ) uv = delta * Texturing.Scale;
				else if ( Texturing.UnitsPerTexture != 0.0f ) uv = distance / Texturing.UnitsPerTexture;

				uv += scrollTime + Texturing.Offset;

				Vector3 normal;
				if ( AutoCalculateNormals && rmfNormals != null )
				{
					normal = rmfNormals[i];
				}
				else if ( _normals != null )
				{
					normal = _normals[i];
				}
				else
				{
					normal = transform.Up;
				}

				_so.AddLinePoint( p, normal, Color.Evaluate( delta ), Width.Evaluate( delta ), uv );
			}

			if ( rmfNormals != null ) ArrayPool<Vector3>.Shared.Return( rmfNormals );
		}
		else
		{
			int i = 0;
			var distance = 0.0f;
			Vector3? previousPoint = null;
			int totalPoints = (count - 1) * interpolation + 1;

			var points = ArrayPool<Vector3>.Shared.Rent( totalPoints );
			var totalLength = 0.0f;

			foreach ( var p in _points.TcbSpline( interpolation, SplineTension, SplineContinuity, SplineBias ) )
			{
				if ( previousPoint.HasValue )
					totalLength += previousPoint.Value.Distance( p );

				points[i++] = p;
				previousPoint = p;
			}

			Vector3[] rmfNormals = null;
			if ( AutoCalculateNormals && Face != SceneLineObject.FaceMode.Camera )
			{
				rmfNormals = ArrayPool<Vector3>.Shared.Rent( totalPoints );
				CalculateRMFNormals( points.AsSpan( 0, totalPoints ), rmfNormals.AsSpan( 0, totalPoints ) );
			}

			for ( i = 0; i < totalPoints; i++ )
			{
				var p = points[i];
				if ( i > 0 ) distance += points[i - 1].Distance( p );

				var delta = totalLength > 0.0f ? distance / totalLength : 0.0f;
				var uv = 0.0f;

				if ( !Texturing.WorldSpace ) uv = delta * Texturing.Scale;
				else if ( Texturing.UnitsPerTexture != 0.0f ) uv = distance / Texturing.UnitsPerTexture;

				uv += scrollTime + Texturing.Offset;

				Vector3 normal;

				if ( AutoCalculateNormals && rmfNormals != null )
				{
					normal = rmfNormals[i];
				}
				else if ( _normals is not null )
				{
					var segmentIndex = (int)(delta * (count - 1));

					var fromNormal = _normals[segmentIndex];
					var toNormal = _normals[Math.Min( segmentIndex + 1, _normals.Count - 1 )];

					var localDelta = (delta * (count - 1)) - segmentIndex;
					normal = fromNormal.SlerpTo( toNormal, localDelta );
				}
				else
				{
					// Fallback
					normal = transform.Up;
				}

				_so.AddLinePoint( p, normal, Color.Evaluate( delta ), Width.Evaluate( delta ), uv );
			}

			ArrayPool<Vector3>.Shared.Return( points );
			if ( rmfNormals != null ) ArrayPool<Vector3>.Shared.Return( rmfNormals );
		}

		_so.EndLine();
	}

	/// <summary>
	/// Calculates Rotation Minimizing Frame normals for a sequence of points in a single pass
	/// </summary>
	private void CalculateRMFNormals( Span<Vector3> points, Span<Vector3> outNormals )
	{
		if ( points.Length < 2 )
		{
			outNormals[0] = Vector3.Up;
			return;
		}

		Vector3 tangent = (points[1] - points[0]).Normal;
		Vector3 up = Vector3.Up;

		if ( Math.Abs( Vector3.Dot( tangent, up ) ) > 0.999f )
			up = Vector3.Right;

		outNormals[0] = (up - tangent * Vector3.Dot( up, tangent )).Normal;

		Vector3 prevPoint = points[0];
		Vector3 prevTangent = tangent;
		Vector3 prevNormal = outNormals[0];

		for ( int i = 1; i < points.Length; i++ )
		{
			Vector3 currPoint = points[i];

			if ( i < points.Length - 1 )
			{
				tangent = (points[i + 1] - points[i - 1]).Normal;
			}
			else
			{
				tangent = (currPoint - points[i - 1]).Normal;
			}

			outNormals[i] = Spline.GetRotationMinimizingNormal(
				prevPoint, prevTangent, prevNormal,
				currPoint, tangent );

			prevPoint = currPoint;
			prevTangent = tangent;
			prevNormal = outNormals[i];
		}
	}

	protected override void OnTagsChanged()
	{
		_so?.Tags.SetFrom( Tags );
	}
}
