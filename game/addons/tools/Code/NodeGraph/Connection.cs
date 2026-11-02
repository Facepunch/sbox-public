namespace Editor.NodeEditor;

public class Connection : GraphicsLine
{
	public static Color SelectedColor { get; } = Color.Parse( "#ff99c8" )!.Value;

	public PlugOut Output { get; protected set; }
	public PlugIn Input { get; protected set; }

	public float WidthScale { get; set; } = 1f;
	public Color ColorTint { get; set; }
	internal bool IsInvalidPreview { get; set; }

	private bool _dragging;

	public HandleConfig Config => Output.IsValid() ? Output.HandleConfig : Input.HandleConfig;
	public ConnectionStyle ConnectionStyle => (GraphicsView as GraphView)?.ConnectionStyle ?? ConnectionStyle.Default;

	private readonly Dictionary<string, ConnectionHandleConfig> _handleConfigs = new();
	private readonly Dictionary<string, ConnectionHandle> _handles = new();

	public Vector2 OutputPosition { get; private set; }
	public Vector2 InputPosition { get; private set; }

	private Rect _localBounds;

	public override Rect BoundingRect => _localBounds;

	/// <summary>
	/// Per-<see cref="NodeEditor.ConnectionStyle"/> data.
	/// </summary>
	public object StyleData { get; set; }

	public Connection( PlugOut output, PlugIn input )
	{
		ZIndex = -10;
		HoverEvents = true;
		Selectable = true;
		Output = output;
		Input = input;

		input.SetConnectionInternal( this );
		output.AddConnectionInternal( this );

		Cursor = CursorShape.Finger;
	}

	public Connection( Plug source )
	{
		Input = source as PlugIn;
		Output = source as PlugOut;
		ZIndex = -10;
	}

	internal void UpdateSceneBounds( Rect sceneRect )
	{
		_localBounds = new Rect( FromScene( sceneRect.Position ), sceneRect.Size ).Grow( 64f );
	}

	protected override void OnPaint()
	{
		if ( !Output.IsValid() && !Input.IsValid() ) return;
		if ( _dragging ) return;

		var config = Config;
		var outNode = Output.IsValid() ? Output.Node : null;
		var inNode = Input.IsValid() ? Input.Node : null;
		var color = Color.Lerp( config.Color, ColorTint.WithAlpha( 1f ), ColorTint.a );
		var width = 4.0f;

		if ( !Input.IsValid() || !Output.IsValid() ) color = color.WithAlpha( 0.4f );
		else if ( outNode?.Node.IsReachable is false || inNode?.Node.IsReachable is false ) color = color.Desaturate( 0.5f ).Darken( 0.25f );

		if ( Paint.HasSelected )
		{
			color = SelectedColor.Darken( 0.2f );
			width = 4.0f;
		}

		if ( Paint.HasMouseOver )
		{
			color = SelectedColor;
			width = 6.0f;
		}

		if ( IsInvalidPreview )
			color = Theme.Red;

		Paint.SetPen( color, width * WidthScale );

		PaintLine();
	}

	internal void LayoutForPreview( Plug plug, Vector2 scenePosition, Plug dropTarget )
	{
		Output = plug as PlugOut ?? dropTarget as PlugOut;
		Input = plug as PlugIn ?? dropTarget as PlugIn;

		if ( Output.IsValid() && Input.IsValid() && Output.Node != Input.Node )
		{
			Layout();
			return;
		}

		OutputPosition = Output?.ConnectionPosition ?? scenePosition;
		InputPosition = Input?.ConnectionPosition ?? scenePosition;

		PrepareGeometryChange();

		Position = OutputPosition;
		Size = new Vector2( 0f, 0f );

		ConnectionStyle.Layout( this, OutputPosition, InputPosition );
	}

	public void Layout()
	{
		OutputPosition = Output.ConnectionPosition;
		InputPosition = Input.ConnectionPosition;

		PrepareGeometryChange();

		Position = OutputPosition;
		Size = new Vector2( 0f, 0f );

		ConnectionStyle.Layout( this, OutputPosition, InputPosition );
	}

	internal bool IsAttachedTo( NodeUI node )
	{
		if ( node == Output?.Node ) return true;
		if ( node == Input?.Node ) return true;

		return false;
	}

	private void SetHandlesVisible( bool visible )
	{
		if ( !Input.IsValid() || !Output.IsValid() ) return;

		if ( visible )
		{
			foreach ( var config in _handleConfigs.Values )
			{
				if ( !_handles.TryGetValue( config.Name, out var handle ) )
				{
					_handles[config.Name] = handle = new ConnectionHandle( this );
				}

				handle.Config = config;
			}
		}
		else
		{
			foreach ( var handle in _handles.Values )
			{
				handle.Destroy();
			}

			_handles.Clear();
		}
	}

	private void UpdateZIndex()
	{
		ZIndex = Hovered ? -8 : Selected ? -9 : -10;
	}

	protected override void OnHoverEnter( GraphicsHoverEvent e )
	{
		if ( Input.IsValid() && Output.IsValid() )
		{
			ToolTip = $"<span style=\"white-space: nowrap;\">{Output.Inner.Type.ToRichText()}<br/>" +
				$"<b>From</b>: {Output.Node.Node.DisplayInfo.Name} \u2192 {Output.Inner.DisplayInfo.Name.WithColor( "#9CDCFE" )}<br/>" +
				$"<b>To</b>: {Input.Node.Node.DisplayInfo.Name} \u2192 {Input.Inner.DisplayInfo.Name.WithColor( "#9CDCFE" )}</span>";
		}

		UpdateZIndex();
		SetHandlesVisible( true );
	}

	protected override void OnHoverLeave( GraphicsHoverEvent e )
	{
		UpdateZIndex();
		SetHandlesVisible( Selected );
	}

	protected override void OnSelectionChanged()
	{
		base.OnSelectionChanged();

		UpdateZIndex();
		SetHandlesVisible( Selected || Hovered );
	}

	protected override void OnMousePressed( GraphicsMouseEvent e )
	{
		if ( e.HasShift )
		{
			Output.Node.Graph.RerouteConnection( this, e.ScenePosition );

			e.Accepted = true;
		}
	}

	protected override void OnMouseReleased( GraphicsMouseEvent e )
	{
		if ( _dragging )
		{
			Output.Node.DroppedPlug( Output, e.ScenePosition, this );
		}

		if ( !IsValid ) return; // connection might get deleted here

		_dragging = false;
		Cursor = CursorShape.Finger;
		Update();
	}

	protected override void OnMouseMove( GraphicsMouseEvent e )
	{
		_dragging = true;
		Cursor = CursorShape.DragLink;
		Output.Node.DraggingPlug( Output, e.ScenePosition, this );
		Update();

		foreach ( var handle in _handles.Values )
		{
			handle.Destroy();
		}

		_handles.Clear();
	}

	public void Disconnect()
	{
		if ( Input.IsValid() )
			Input.SetConnectionInternal( null );

		if ( Output.IsValid() )
			Output.RemoveConnectionInternal( this );

		Output.Node.Graph.RemoveConnection( this );
	}

	internal void SetHandles( IReadOnlyList<ConnectionHandleConfig> configs )
	{
		_handleConfigs.Clear();

		foreach ( var config in configs )
		{
			_handleConfigs[config.Name] = config;
		}

		var anyRemoved = false;

		foreach ( var (name, handle) in _handles )
		{
			if ( _handleConfigs.TryGetValue( name, out var config ) )
			{
				handle.Config = config;
			}
			else
			{
				anyRemoved = true;
			}
		}

		if ( !anyRemoved ) return;

		foreach ( var key in _handles.Keys.Where( x => !_handleConfigs.ContainsKey( x ) ).ToArray() )
		{
			_handles.Remove( key, out var handle );
			handle!.Destroy();
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

public record struct ConnectionHandleConfig(
	string Name,
	DragDirection Direction,
	ConnectionPlug RelativePlug,
	Vector2 SceneOrigin,
	float Default,
	float? Min = null,
	float? Max = null )
{
	public Vector2 GetScenePosition( Connection connection )
	{
		var value = GetValue( connection );
		var axis = Direction == DragDirection.Horizontal ? new Vector2( 1f, 0f ) : new Vector2( 0f, 1f );
		return SceneOrigin + axis * value;
	}

	public float GetValue( Connection connection )
	{
		var value = connection.Input?.Inner.GetHandleOffset( Name ) ?? Default;
		return Math.Clamp( value, Min ?? float.NegativeInfinity, Max ?? float.PositiveInfinity );
	}
}

internal sealed class ConnectionHandle : GraphicsItem
{
	public Connection Connection { get; }

	private ConnectionHandleConfig _config;

	public ConnectionHandleConfig Config
	{
		get => _config;
		set
		{
			_config = value;

			UpdateCursor();
			UpdatePosition();
		}
	}

	public ConnectionHandle( Connection connection )
		: base( connection )
	{
		Connection = connection;

		ZIndex = 1;

		Size = new Vector2( 12f, 12f );
		HandlePosition = new Vector2( 0.5f, 0.5f );

		HoverEvents = true;
		Selectable = true;
		Movable = true;
	}

	private void UpdateCursor()
	{
		Cursor = Config.Direction == DragDirection.Horizontal ? CursorShape.SplitH : CursorShape.SplitV;
	}

	private void UpdatePosition()
	{
		PrepareGeometryChange();

		Position = Connection.FromScene( Config.GetScenePosition( Connection ) );
	}

	protected override void OnPaint()
	{
		var isDefault = Config.GetValue( Connection ).AlmostEqual( Config.Default );
		var baseColor = isDefault ? Connection.SelectedColor : Color.White;

		Paint.SetPen( Connection.SelectedColor, 2f );
		Paint.SetBrush( Hovered || Selected ? baseColor : baseColor.Darken( 0.2f ) );
		Paint.DrawRect( Config.Direction == DragDirection.Horizontal ? LocalRect.Shrink( 2f, 0f ) : LocalRect.Shrink( 0f, 2f ), 2f );
	}

	private bool AreNodesSelected => Config.RelativePlug == ConnectionPlug.Input
		? Connection.Input is { Node.Selected: true }
		: Connection.Output is { Node.Selected: true };

	protected override void OnMousePressed( GraphicsMouseEvent e )
	{
		var view = GraphicsView as GraphView;

		view?.MoveablePressed();
	}

	protected override void OnMouseReleased( GraphicsMouseEvent e )
	{
		var view = GraphicsView as GraphView;

		view?.MoveableReleased();
	}

	protected override void OnMoved()
	{
		if ( AreNodesSelected )
		{
			UpdatePosition();
			return;
		}

		var newScenePos = Connection.ToScene( Position );
		var oldScenePos = Config.GetScenePosition( Connection );
		var view = GraphicsView as GraphView;

		var gridSize = view?.GridSize ?? 12f;
		var diff = (newScenePos - oldScenePos).SnapToGrid( gridSize );
		var offset = 0f;

		switch ( Config.Direction )
		{
			case DragDirection.Horizontal:
				offset = diff.x;
				diff.y = 0f;
				break;

			case DragDirection.Vertical:
				offset = diff.y;
				diff.x = 0f;
				break;
		}

		if ( offset.AlmostEqual( 0f ) )
		{
			UpdatePosition();
			return;
		}

		view?.MoveableMoved();

		var newValue = Config.GetValue( Connection ) + offset;

		Connection.Input?.Inner.SetHandleOffset( Config.Name, newValue.AlmostEqual( Config.Default ) ? null : newValue );
		Connection.Layout();
	}
}
