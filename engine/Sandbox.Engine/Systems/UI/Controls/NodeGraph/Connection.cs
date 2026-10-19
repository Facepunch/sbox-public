namespace Sandbox.UI.NodeGraph;

/// <summary>
/// A wire between a <see cref="PlugOutPanel"/> and a <see cref="PlugInPanel"/>, or the preview wire
/// while one end is being dragged. Geometry is in canvas units.
/// </summary>
public class Connection
{
	public Color SelectedColor => Graph.SelectionColor;

	public NodeGraphPanel Graph { get; }
	public PlugOutPanel Output { get; private set; }
	public PlugInPanel Input { get; private set; }

	/// <summary>
	/// True for the wire shown while dragging from a plug.
	/// </summary>
	public bool IsPreview { get; }

	public bool Selected { get; internal set; }
	public bool Hovered { get; internal set; }

	/// <summary>
	/// The wire is being dragged off its input, so it isn't drawn.
	/// </summary>
	public bool Dragging { get; internal set; }

	public float WidthScale { get; set; } = 1f;
	public Color ColorTint { get; set; }

	/// <summary>
	/// Per-<see cref="NodeGraph.ConnectionStyle"/> data.
	/// </summary>
	public object StyleData { get; set; }

	public NodeHandleConfig Config => Output.IsValid() ? Output.HandleConfig : Input.HandleConfig;
	public ConnectionStyle ConnectionStyle => Graph.ConnectionStyle;

	public Vector2 OutputPosition { get; private set; }
	public Vector2 InputPosition { get; private set; }

	/// <summary>
	/// Encloses the wire, in canvas units.
	/// </summary>
	public Rect Bounds { get; private set; }

	/// <summary>
	/// The wire's path as a polyline, in canvas units.
	/// </summary>
	public IReadOnlyList<Vector2> Points => _points;

	/// <summary>
	/// The draggable handles the style asked for.
	/// </summary>
	public IReadOnlyList<ConnectionHandleConfig> Handles => _handles;

	/// <summary>
	/// Both plugs still exist.
	/// </summary>
	public bool IsValid => !IsPreview && Output.IsValid() && Input.IsValid();

	internal int HoveredHandle { get; set; } = -1;
	internal int DraggedHandle { get; set; } = -1;

	internal bool ShowHandles => Output.IsValid() && Input.IsValid() && (Hovered || Selected) && !Dragging;

	/// <summary>
	/// Draw order: hovered on top, then selected, then the rest.
	/// </summary>
	internal int Rank => Hovered ? 2 : Selected ? 1 : 0;

	/// <summary>
	/// The node that paints this wire, so the wire sits at its depth: the output's, or the input's
	/// while a wire dragged from an input has no output yet.
	/// </summary>
	internal NodePanel PaintOwner => Output.IsValid() ? Output.Node : Input.IsValid() ? Input.Node : null;

	readonly List<Vector2> _points = new();
	readonly List<ConnectionHandleConfig> _handles = new();
	Vector2[] _screen = new Vector2[64];

	internal Connection( NodeGraphPanel graph, PlugOutPanel output, PlugInPanel input )
	{
		Graph = graph;
		Output = output;
		Input = input;
	}

	internal Connection( NodeGraphPanel graph, PlugPanel source )
	{
		Graph = graph;
		IsPreview = true;
		Input = source as PlugInPanel;
		Output = source as PlugOutPanel;
	}

	public void Clear()
	{
		_points.Clear();
	}

	public void MoveTo( Vector2 point )
	{
		_points.Clear();
		_points.Add( point );
	}

	public void LineTo( Vector2 point )
	{
		_points.Add( point );
	}

	public void CubicLineTo( Vector2 control1, Vector2 control2, Vector2 end )
	{
		if ( _points.Count == 0 ) _points.Add( control1 );

		var start = _points[^1];
		var length = (control1 - start).Length + (control2 - control1).Length + (end - control2).Length;
		var segments = Math.Clamp( (int)MathF.Ceiling( length / 6f ), 4, 64 );

		for ( var i = 1; i <= segments; i++ )
		{
			var t = i / (float)segments;
			var u = 1f - t;

			_points.Add( start * (u * u * u) + control1 * (3f * u * u * t) + control2 * (3f * u * t * t) + end * (t * t * t) );
		}
	}

	internal void UpdateBounds( Rect rect )
	{
		Bounds = rect;
	}

	internal void SetHandles( List<ConnectionHandleConfig> configs )
	{
		_handles.Clear();
		_handles.AddRange( configs );
	}

	/// <summary>
	/// Lay the wire out between its plugs.
	/// </summary>
	public void Layout()
	{
		OutputPosition = Output.ConnectionPosition;
		InputPosition = Input.ConnectionPosition;

		ConnectionStyle.Layout( this, OutputPosition, InputPosition );
	}

	internal void LayoutForPreview( PlugPanel plug, Vector2 position, PlugPanel dropTarget )
	{
		Output = plug as PlugOutPanel ?? dropTarget as PlugOutPanel;
		Input = plug as PlugInPanel ?? dropTarget as PlugInPanel;

		if ( Output.IsValid() && Input.IsValid() && Output.Node != Input.Node )
		{
			Layout();
			return;
		}

		OutputPosition = Output?.ConnectionPosition ?? position;
		InputPosition = Input?.ConnectionPosition ?? position;

		ConnectionStyle.Layout( this, OutputPosition, InputPosition );
	}

	internal bool IsAttachedTo( NodePanel node )
	{
		return node == Output?.Node || node == Input?.Node;
	}

	/// <summary>
	/// Distance from <paramref name="point"/> to the wire, in canvas units.
	/// </summary>
	internal float DistanceTo( Vector2 point )
	{
		var best = float.MaxValue;

		for ( var i = 1; i < _points.Count; i++ )
		{
			var a = _points[i - 1];
			var b = _points[i];
			var ab = b - a;
			var lengthSquared = ab.LengthSquared;
			var t = lengthSquared <= 0f ? 0f : Math.Clamp( Vector2.Dot( point - a, ab ) / lengthSquared, 0f, 1f );

			best = MathF.Min( best, (point - (a + ab * t)).Length );
		}

		return best;
	}

	internal int HandleAt( Vector2 point )
	{
		for ( var i = 0; i < _handles.Count; i++ )
		{
			var center = _handles[i].GetPosition( this );

			if ( MathF.Abs( point.x - center.x ) <= 6f && MathF.Abs( point.y - center.y ) <= 6f )
			{
				return i;
			}
		}

		return -1;
	}

	/// <summary>
	/// Remove the wire and disconnect the input.
	/// </summary>
	public void Disconnect()
	{
		Graph.RemoveConnection( this );
	}

	Color GetColor( out float width )
	{
		var config = Config;
		var outNode = Output.IsValid() ? Output.Node : null;
		var inNode = Input.IsValid() ? Input.Node : null;
		var color = Color.Lerp( config.Color, ColorTint.WithAlpha( 1f ), ColorTint.a );

		width = 2.5f;

		if ( !Input.IsValid() )
		{
			color = color.WithAlpha( 0.6f );
		}
		else if ( outNode?.Node.IsReachable is false || inNode?.Node.IsReachable is false )
		{
			color = color.Desaturate( 0.5f ).Darken( 0.25f ).WithAlpha( 0.6f );
		}

		if ( Selected )
		{
			color = SelectedColor;
			width = 3.5f;
		}

		if ( Hovered )
		{
			color = Selected ? SelectedColor.Lighten( 0.2f ) : color.Lighten( 0.25f ).WithAlpha( 1f );
			width = 3.5f;
		}

		return color;
	}

	/// <summary>
	/// Draw in the painting node's space: a canvas point lands at its offset from
	/// <paramref name="origin"/> times <paramref name="scale"/>, the node's pixels per canvas unit.
	/// </summary>
	internal void Paint( Painter painter, Vector2 origin, float scale )
	{
		if ( Dragging || _points.Count < 2 ) return;
		if ( !Output.IsValid() && !Input.IsValid() ) return;

		var color = GetColor( out var width );

		if ( _screen.Length < _points.Count ) _screen = new Vector2[_points.Count * 2];

		for ( var i = 0; i < _points.Count; i++ )
		{
			_screen[i] = (_points[i] - origin) * scale;
		}

		painter.Stroke = Stroke.Solid( color, width * WidthScale * scale );
		painter.Line( _screen.AsSpan( 0, _points.Count ) );
	}

	internal void PaintHandles( Painter painter, Vector2 origin, float scale )
	{
		if ( !ShowHandles ) return;

		for ( var i = 0; i < _handles.Count; i++ )
		{
			var config = _handles[i];
			var isDefault = config.GetValue( this ).AlmostEqual( config.Default );
			var baseColor = isDefault ? SelectedColor : Color.White;
			var active = HoveredHandle == i || DraggedHandle == i;

			var size = config.Direction == DragDirection.Horizontal ? new Vector2( 8f, 12f ) : new Vector2( 12f, 8f );
			var center = (config.GetPosition( this ) - origin) * scale;

			painter.Fill = active ? baseColor : baseColor.Darken( 0.2f );
			painter.Stroke = Stroke.Solid( SelectedColor, 2f * scale );
			painter.Rect( new Rect( center - size * 0.5f * scale, size * scale ), 2f * scale );
		}
	}
}

public enum DragDirection
{
	Horizontal,
	Vertical
}

public enum ConnectionPlug
{
	Output,
	Input
}

/// <summary>
/// A handle on a wire that the user drags along one axis; the value is stored on the input.
/// </summary>
public record struct ConnectionHandleConfig(
	string Name,
	DragDirection Direction,
	ConnectionPlug RelativePlug,
	Vector2 Origin,
	float Default,
	float? Min = null,
	float? Max = null )
{
	/// <summary>
	/// Where the handle is, in canvas units.
	/// </summary>
	public Vector2 GetPosition( Connection connection )
	{
		var value = GetValue( connection );
		var axis = Direction == DragDirection.Horizontal ? new Vector2( 1f, 0f ) : new Vector2( 0f, 1f );
		return Origin + axis * value;
	}

	public float GetValue( Connection connection )
	{
		var value = connection.Input?.Inner.GetHandleOffset( Name ) ?? Default;
		return Math.Clamp( value, Min ?? float.NegativeInfinity, Max ?? float.PositiveInfinity );
	}
}
