using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Blends synchronized poses positioned at values along a one-dimensional parameter axis.
/// <br/><br/><b>Use when:</b>&#160;Blending ordered poses such as idle, walk, and run from one value.
/// <br/><b>Important:</b>&#160;Requires at least two points with unique values. Invalid configurations output the reference pose.
/// </summary>
[Title( "Blend 1D" ), Group( "Blend" ), Icon( "linear_scale" )]
public class Blend1DNode : Node
{
	/// <summary>
	/// A pose placed at a value on the blend axis.
	/// </summary>
	public class BlendPoint
	{
		/// <summary>
		/// Optional label — falls back to the connected node's name.
		/// </summary>
		[Property]
		public string Name { get; set; } = "";

		[Property]
		public float Value { get; set; }

		/// <summary>
		/// Pose graph sampled at this value.
		/// </summary>
		[Hide]
		public Data Graph { get; set; } = Data.CreateSubGraph<PoseResultNode>();
	}

	[Input( typeof( FloatLink ) )]
	public NodeInput Parameter { get; set; }

	[Property, Hide]
	public List<BlendPoint> Points { get; set; } =
	[
		new(),
		new() { Value = 1 },
	];

	[Property]
	public bool Loop { get; set; } = true;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Blue;

	private const float PreviewHeight = 56;
	private static readonly Color LiveBlendColor = new( 0.68f, 1.0f, 0.18f );
	[JsonIgnore]
	internal bool HasLiveBlendDebug { get; private set; }

	[JsonIgnore]
	internal int LiveSourcePoint0 { get; private set; } = -1;

	[JsonIgnore]
	internal int LiveSourcePoint1 { get; private set; } = -1;

	[JsonIgnore]
	internal float LiveBlendWeight { get; private set; }

	public override IEnumerable<Data> SubGraphs
	{
		get
		{
			foreach ( var point in Points )
				yield return point.Graph;
		}
	}

	internal void InitializePointGraphs()
	{
		foreach ( var point in Points )
		{
			point.Graph ??= Data.CreateSubGraph<PoseResultNode>();
			point.Graph.SetRoot( Root );
		}
	}

	internal BlendPoint AddPoint( float value )
	{
		var point = new BlendPoint { Value = value };
		point.Graph.SetRoot( Root );
		Points.Add( point );
		Update();
		return point;
	}

	internal void RemovePoint( BlendPoint point )
	{
		if ( point is null || Points.Count <= 2 )
			return;

		Points.Remove( point );
		Update();
	}

	public string PointTitle( int i )
	{
		if ( i < 0 || i >= Points.Count )
			return $"Point {i + 1}";

		var point = Points[i];
		var name = point.Name;

		if ( string.IsNullOrWhiteSpace( name )
			&& point.Graph?.Nodes.OfType<PoseResultNode>().FirstOrDefault() is { } output
			&& output.GetConnectedNode( output.Pose ) is { } source )
			name = source.DisplayInfo.Name;

		if ( string.IsNullOrWhiteSpace( name ) )
			name = $"Point {i + 1}";

		return $"{name}  ({point.Value:0.##})";
	}

	internal override void BuildDebugWidgets( Widget parent )
	{
		parent.Layout.Add( new PreviewCanvas( parent, this ) );
		base.BuildDebugWidgets( parent );
	}

	private sealed class PreviewCanvas( Widget parent, Blend1DNode node ) : LiveCanvas( parent, PreviewHeight )
	{
		protected override void PaintContent( Rect rect ) => node.PaintDebugPreview( rect );
	}

	private void PaintDebugPreview( Rect area )
	{
		Paint.Antialiasing = true;

		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( area, 4 );

		if ( Points.Count < 2 )
			return;

		var inner = area.Shrink( 20.0f, 0.0f );
		var axisY = area.Center.y - 4.0f;

		var min = Points.Min( p => p.Value );
		var max = Points.Max( p => p.Value );
		var range = MathF.Max( max - min, 0.001f );

		float ToScreenX( float value ) => inner.Left + (value - min) / range * inner.Width;

		Paint.ClearBrush();
		Paint.SetPen( Theme.Green.WithAlpha( 0.4f ) );
		Paint.DrawLine( new Vector2( inner.Left, axisY ), new Vector2( inner.Right, axisY ) );

		for ( var i = 0; i < Points.Count; i++ )
		{
			var x = ToScreenX( Points[i].Value );
			var weight = PointWeight( i );

			if ( weight > 0.001f )
			{
				Paint.ClearPen();
				Paint.SetBrush( LiveBlendColor.WithAlpha( 0.15f + weight * 0.35f ) );
				Paint.DrawCircle( new Vector2( x, axisY ), 6.0f + weight * 10.0f );
			}

			Paint.ClearPen();
			Paint.SetBrush( weight > 0.001f ? Color.Lerp( Theme.Green, LiveBlendColor, weight ) : Theme.Green );
			Paint.DrawCircle( new Vector2( x, axisY ), 4.0f + weight * 3.0f );
		}

		if ( !HasLiveBlendDebug || LiveSourcePoint0 < 0 )
			return;

		var start = ToScreenX( Points[LiveSourcePoint0].Value );
		var markerX = LiveSourcePoint1 < 0
			? start
			: start + (ToScreenX( Points[LiveSourcePoint1].Value ) - start) * LiveBlendWeight;

		Paint.SetPen( Theme.WidgetBackground, 2 );
		Paint.SetBrush( LiveBlendColor );
		Paint.DrawCircle( new Vector2( markerX, axisY ), 6.0f );

		var text = string.Join( "\n", LiveContributions().Select( c => $"{PointTitle( c.Point )}  {c.Weight * 100.0f:0}%" ) );
		DebugMarker.PaintReadout( area, new Vector2( markerX, axisY ), text );
	}

	private float PointWeight( int index )
	{
		if ( !HasLiveBlendDebug )
			return 0.0f;

		if ( index == LiveSourcePoint0 )
			return LiveSourcePoint1 < 0 ? 1.0f : 1.0f - LiveBlendWeight;

		if ( index == LiveSourcePoint1 )
			return LiveBlendWeight;

		return 0.0f;
	}

	private List<(int Point, float Weight)> LiveContributions()
	{
		if ( !HasLiveBlendDebug || LiveSourcePoint0 < 0 )
			return [];

		var points = LiveSourcePoint1 < 0 || LiveSourcePoint1 == LiveSourcePoint0
			? new[] { LiveSourcePoint0 }
			: new[] { LiveSourcePoint0, LiveSourcePoint1 };

		return points
			.Select( i => (i, PointWeight( i )) )
			.Where( c => c.Item2 > 0.001f )
			.OrderByDescending( c => c.Item2 )
			.ToList();
	}

	public bool SetLiveBlendDebug( bool hasDebug, int sourcePoint0 = -1, int sourcePoint1 = -1, float blendWeight = 0.0f )
	{
		if ( HasLiveBlendDebug == hasDebug
			&& LiveSourcePoint0 == sourcePoint0
			&& LiveSourcePoint1 == sourcePoint1
			&& LiveBlendWeight == blendWeight )
		{
			return false;
		}

		HasLiveBlendDebug = hasDebug;
		LiveSourcePoint0 = sourcePoint0;
		LiveSourcePoint1 = sourcePoint1;
		LiveBlendWeight = blendWeight;
		return true;
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		if ( Points.Count < 2 )
		{
			compiler.Warn( "Blend 1D needs at least 2 points" );
			return compiler.ReferencePose();
		}

		if ( Points.GroupBy( p => p.Value ).Any( g => g.Count() > 1 ) )
		{
			compiler.Warn( "Blend 1D points must have unique values" );
			return compiler.ReferencePose();
		}

		var sources = Points.Select( ( point, index ) => EmitPointGraph( compiler, point, index ) ).ToList();
		var parameter = compiler.EmitValue( this, Parameter );
		if ( parameter is null )
			parameter = compiler.ConstFloat( 0 );


		var sorted = Points
			.Select( ( p, i ) => (Index: i, p.Value) )
			.OrderBy( p => p.Value )
			.ToList();

		var ranges = new List<(int Input0, int Input1, float Start, float End)>();

		for ( var i = 0; i < sorted.Count - 1; i++ )
		{
			ranges.Add( (
				sorted[i].Index,
				sorted[i + 1].Index,
				sorted[i].Value,
				sorted[i + 1].Value) );
		}

		return compiler.Builder.BlendSpace(
			 parameter,
			[.. sources.Select( index => index )],
			ranges,
			Loop );
	}


	private static GraphBuilder.Node EmitPointGraph( Compiler compiler, BlendPoint point, int index )
	{
		var title = string.IsNullOrWhiteSpace( point.Name ) ? $"Blend 1D point {index + 1}" : $"Blend 1D point '{point.Name}'";

		if ( point.Graph?.Nodes.OfType<PoseResultNode>().FirstOrDefault() is not { } output )
		{
			compiler.Warn( $"{title} has no output node" );
			return compiler.ReferencePose();
		}

		return compiler.EmitPose( output, output.Pose, title, synchronized: true );
	}

}
