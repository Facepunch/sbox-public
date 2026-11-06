namespace Sandbox.UI.NodeGraph;

/// <summary>
/// Base class for wire styles, e.g. curvy or angular. Positions are in canvas units.
/// </summary>
public abstract class ConnectionStyle
{
	/// <summary>
	/// Used unless <see cref="NodeGraphPanel.ConnectionStyle"/> is overridden.
	/// </summary>
	public static ConnectionStyle Default { get; } = new ClassicConnectionStyle();

	[ThreadStatic]
	static List<ConnectionHandleConfig> HandleConfigs;

	/// <summary>
	/// Updates the handles, bounds and path of a connection.
	/// </summary>
	public void Layout( Connection connection, Vector2 start, Vector2 end )
	{
		var configs = HandleConfigs ??= new();
		configs.Clear();

		try
		{
			OnPreLayout( connection, start, end );
			OnConfigureHandles( connection, start, end );

			connection.UpdateBounds( OnGetBounds( connection, start, end ) );
			connection.SetHandles( configs );
			connection.Clear();

			OnLayout( connection, start, end );
		}
		finally
		{
			configs.Clear();
		}
	}

	/// <summary>
	/// Add a handle the user can drag horizontally or vertically. Only call this from <see cref="OnConfigureHandles"/>.
	/// </summary>
	protected void AddHandle( ConnectionHandleConfig config )
	{
		HandleConfigs.Add( config );
	}

	protected void ResetHandle( Connection connection, string name )
	{
		if ( connection.IsPreview || connection.Input is not { } input ) return;
		if ( input.Inner.GetHandleOffset( name ) is null ) return;

		input.Inner.SetHandleOffset( name, null );
	}

	/// <summary>
	/// Start of <see cref="Layout"/>. A good time to set <see cref="Connection.StyleData"/>.
	/// </summary>
	protected virtual void OnPreLayout( Connection connection, Vector2 start, Vector2 end ) { }

	/// <summary>
	/// Call <see cref="AddHandle"/> here.
	/// </summary>
	protected virtual void OnConfigureHandles( Connection connection, Vector2 start, Vector2 end ) { }

	/// <summary>
	/// A rectangle, in canvas units, that fully encloses the wire.
	/// </summary>
	protected abstract Rect OnGetBounds( Connection connection, Vector2 start, Vector2 end );

	/// <summary>
	/// Describe the wire with <see cref="Connection.MoveTo"/>, <see cref="Connection.LineTo"/> and <see cref="Connection.CubicLineTo"/>.
	/// </summary>
	protected abstract void OnLayout( Connection connection, Vector2 start, Vector2 end );

	protected static float SnapToGrid( float value, float gridSize )
	{
		return MathF.Floor( value / gridSize ) * gridSize;
	}

	protected static Vector2 SnapToGrid( Vector2 pos, float gridSize )
	{
		return new Vector2( SnapToGrid( pos.x, gridSize ), SnapToGrid( pos.y, gridSize ) );
	}

	protected static Rect SnapToGrid( Rect rect, float gridSize )
	{
		var min = SnapToGrid( rect.Position, gridSize );
		var max = SnapToGrid( rect.Position + rect.Size, gridSize );

		return new Rect( min, max - min );
	}
}

/// <summary>
/// Original curvy cubic wire style.
/// </summary>
public sealed class ClassicConnectionStyle : ConnectionStyle
{
	protected override Rect OnGetBounds( Connection connection, Vector2 start, Vector2 end )
	{
		return Rect.FromPoints( start, end ).Grow( 64f );
	}

	protected override void OnLayout( Connection connection, Vector2 start, Vector2 end )
	{
		var dir = end - start;
		var length = dir.Length;
		dir = dir.Normal;

		var ease = Sandbox.Utility.Easing.ExpoOut( 1.0f - MathF.Abs( Vector2.Dot( dir, Vector2.Up ) ) );
		var dist = (64 * ease).Clamp( 0, length * 0.5f );
		var ledge = new Vector2( (16 * ease).Clamp( 0, (length * 0.5f) - 16 ), 0 );

		end -= ledge;
		connection.MoveTo( start );
		start += ledge;
		connection.LineTo( start );
		connection.CubicLineTo( start + new Vector2( dist, 0 ), end + new Vector2( -dist, 0 ), end );
		end += ledge;
		connection.LineTo( end );
	}
}

/// <summary>
/// Right-angled wires with rounded corners and draggable handles.
/// </summary>
public sealed class GridConnectionStyle : ConnectionStyle
{
	public static GridConnectionStyle Instance { get; } = new();

	enum PathKind
	{
		Straight,
		TwoCorners,
		FourCorners
	}

	class Data
	{
		public float GridSize;
		public Vector2 Start;
		public Vector2 End;
		public Rect StartRect;
		public Rect EndRect;
		public PathKind PathKind;

		public float A;
		public float B;
		public float Y;
	}

	static float GetGridSize( Connection connection ) => connection.Graph?.GridSize ?? 12f;

	static Rect NodeRect( NodePanel node ) => new( node.Position, node.Size );

	protected override void OnPreLayout( Connection connection, Vector2 start, Vector2 end )
	{
		if ( connection.StyleData is not Data data )
		{
			connection.StyleData = data = new Data();
		}

		var gridSize = GetGridSize( connection );

		start = SnapToGrid( start, gridSize );
		end = SnapToGrid( end, gridSize );

		var startRect = new Rect( start, Vector2.Zero );
		var endRect = new Rect( end, Vector2.Zero );

		if ( connection.Output?.Node is { } startNode )
		{
			startRect = SnapToGrid( NodeRect( startNode ), gridSize );
		}

		if ( connection.Input?.Node is { } endNode )
		{
			endRect = SnapToGrid( NodeRect( endNode ), gridSize );
		}

		startRect = startRect.Grow( gridSize );
		endRect = endRect.Grow( gridSize );

		data.GridSize = gridSize;
		data.Start = start;
		data.End = end;
		data.StartRect = startRect;
		data.EndRect = endRect;

		data.PathKind = startRect.Right <= endRect.Left
			? start.y.AlmostEqual( end.y ) ? PathKind.Straight : PathKind.TwoCorners
			: PathKind.FourCorners;
	}

	protected override void OnConfigureHandles( Connection connection, Vector2 start, Vector2 end )
	{
		var data = (Data)connection.StyleData;

		if ( data.PathKind == PathKind.Straight )
		{
			data.A = 0f;
			data.B = 0f;
			data.Y = 0f;

			ResetHandle( connection, "x" );
			ResetHandle( connection, "y" );
			ResetHandle( connection, "a" );
			ResetHandle( connection, "b" );

			return;
		}

		var hintStart = GetPlugIndex( connection, connection.Output );
		var hintStartOffset = hintStart.Index * data.GridSize;

		if ( data.PathKind == PathKind.TwoCorners )
		{
			var config = new ConnectionHandleConfig( "x",
				DragDirection.Horizontal, ConnectionPlug.Output,
				new Vector2( data.StartRect.Right, (data.Start.y + data.End.y) * 0.5f ),
				hintStartOffset,
				Min: 0f, Max: data.EndRect.Left - data.StartRect.Right );

			AddHandle( config );

			ResetHandle( connection, "y" );
			ResetHandle( connection, "a" );
			ResetHandle( connection, "b" );

			data.A = data.B = data.StartRect.Right + config.GetValue( connection );
			data.Y = data.Start.y;
			return;
		}

		var hintMid = GetConnectionIndex( connection );
		var hintEnd = GetPlugIndex( connection, connection.Input );

		var hintEndOffset = (hintEnd.Count - hintEnd.Index - 1) * data.GridSize;
		var hintMidOffset = hintMid.Index * data.GridSize;

		var startEdgeY = Math.Clamp( end.y, data.StartRect.Top - (hintMid.Count - 1) * data.GridSize, data.StartRect.Bottom );

		var aConfig = new ConnectionHandleConfig( "a",
			DragDirection.Horizontal, ConnectionPlug.Output,
			new Vector2( data.StartRect.Right, default ),
			hintStartOffset,
			Min: 0f );

		var bConfig = new ConnectionHandleConfig( "b",
			DragDirection.Horizontal, ConnectionPlug.Input,
			new Vector2( data.EndRect.Left, default ),
			-hintEndOffset,
			Max: 0f );

		var yConfig = new ConnectionHandleConfig( "y",
			DragDirection.Vertical, ConnectionPlug.Output,
			new Vector2( default, start.y ),
			startEdgeY + hintMidOffset - start.y );

		var a = data.StartRect.Right + aConfig.GetValue( connection );
		var b = data.EndRect.Left + bConfig.GetValue( connection );
		var y = start.y + yConfig.GetValue( connection );

		data.A = a;
		data.B = b;
		data.Y = y;

		AddHandle( aConfig with { Origin = new Vector2( data.StartRect.Right, (data.Start.y + y) * 0.5f ) } );
		AddHandle( bConfig with { Origin = new Vector2( data.EndRect.Left, (data.End.y + y) * 0.5f ) } );
		AddHandle( yConfig with { Origin = new Vector2( (a + b) * 0.5f, start.y ) } );

		ResetHandle( connection, "x" );
	}

	static (int Index, int Count) FindIndexAndCount<T>( IEnumerable<T> items, Predicate<T> predicate )
	{
		var count = 0;
		var index = -1;

		foreach ( var item in items )
		{
			if ( index == -1 && predicate( item ) )
			{
				index = count;
			}

			++count;
		}

		return (index, count);
	}

	/// <summary>
	/// How many connected plugs are above this output / below this input on the same node.
	/// </summary>
	static (int Index, int Count) GetPlugIndex( Connection connection, PlugPanel plug )
	{
		if ( plug is null ) return (0, 1);

		var graph = connection.Graph;

		if ( plug is PlugInPanel )
		{
			return FindIndexAndCount( plug.Node.Inputs.Where( x => x.IsConnected ), x => x == plug );
		}

		return FindIndexAndCount( plug.Node.Outputs.Where( x => graph.Connections.Any( y => y.Output == x ) ), x => x == plug );
	}

	/// <summary>
	/// How many connections are above this one between the same two nodes.
	/// </summary>
	static (int Index, int Count) GetConnectionIndex( Connection connection )
	{
		if ( connection.Output?.Node is not { } outNode ) return (0, 1);
		if ( connection.Input?.Node is not { } inNode ) return (0, 1);

		var graph = connection.Graph;

		return FindIndexAndCount( outNode.Outputs.Where( x => graph.Connections.Any( y => y.Output == x && y.Input?.Node == inNode ) ),
			x => x == connection.Output );
	}

	protected override Rect OnGetBounds( Connection connection, Vector2 start, Vector2 end )
	{
		var data = (Data)connection.StyleData;

		var min = Vector2.Min( start, end );
		var max = Vector2.Max( start, end );

		if ( data.PathKind == PathKind.FourCorners )
		{
			var a = new Vector2( data.A, data.Y );
			var b = new Vector2( data.B, data.Y );

			min = Vector2.Min( min, Vector2.Min( a, b ) );
			max = Vector2.Max( max, Vector2.Max( a, b ) );
		}

		var rect = Rect.FromPoints( min, max );

		return rect.Grow( 16f );
	}

	protected override void OnLayout( Connection connection, Vector2 start, Vector2 end )
	{
		var data = (Data)connection.StyleData;

		var gridSize = data.GridSize;

		var a = new Vector2( data.A, data.Y );
		var b = new Vector2( data.B, data.Y );

		var prev = start;
		var curr = start;

		connection.MoveTo( start );

		switch ( data.PathKind )
		{
			case PathKind.TwoCorners:
				CurvedLineTo( connection, gridSize, ref prev, ref curr, start with { x = a.x } );
				CurvedLineTo( connection, gridSize, ref prev, ref curr, end with { x = b.x } );
				CurvedLineTo( connection, gridSize, ref prev, ref curr, end );
				break;

			case PathKind.FourCorners:
				CurvedLineTo( connection, gridSize, ref prev, ref curr, start with { x = a.x } );
				CurvedLineTo( connection, gridSize, ref prev, ref curr, a );
				CurvedLineTo( connection, gridSize, ref prev, ref curr, b );
				CurvedLineTo( connection, gridSize, ref prev, ref curr, end with { x = b.x } );
				CurvedLineTo( connection, gridSize, ref prev, ref curr, end );
				break;
		}

		connection.LineTo( end );
	}

	static void CurvedLineTo( Connection connection, float gridSize, ref Vector2 prev, ref Vector2 curr, Vector2 next )
	{
		if ( next.AlmostEqual( curr ) )
		{
			return;
		}

		var prevTangent = curr.AlmostEqual( prev )
			? new Vector2( 1f, 0f )
			: (curr - prev).Normal;

		var nextTangent = (next - curr).Normal;

		if ( nextTangent.AlmostEqual( -prevTangent ) )
		{
			curr = next;
			return;
		}

		if ( nextTangent.AlmostEqual( prevTangent ) )
		{
			curr = next;
			return;
		}

		var radius = Math.Min( gridSize, (next - curr).Length ) * 0.5f;

		var a = curr - prevTangent * radius;
		var b = curr + nextTangent * radius;

		connection.LineTo( a );
		connection.CubicLineTo( a + prevTangent * radius * 0.5f, b - nextTangent * radius * 0.5f, b );

		prev = curr;
		curr = next;
	}
}
