namespace Editor.NodeEditor;

public abstract class Plug : GraphicsItem
{
	protected const float handleSize = 14;
	public HandleConfig HandleConfig => Node.Graph.GetHandleConfig( PropertyType );
	protected Plug DropTarget => Node.Graph.DropTarget;

	public abstract Vector2 ConnectionPosition { get; }

	public string Title => Inner.DisplayInfo.Name;

	private Type _overridePropertyType;

	public NodeUI Node { get; private set; }
	public IPlug Inner { get; }

	public Type PropertyType
	{
		get => _overridePropertyType ?? Inner.Type;
		set => _overridePropertyType = value;
	}

	public bool Visible { get; set; }
	public bool Dragging { get; protected set; }

	public abstract bool IsConnected { get; }

	public ValueEditor Editor { get; }

	public float DefaultZIndex { get; set; }

	bool IsReconnecting => Reconnection is not null;
	Connection Reconnection;

	public Plug( NodeUI node, IPlug plug ) : base( node )
	{
		Size = new Vector2( 24, 24 );
		Node = node;
		Inner = plug;
		Visible = true;
		HoverEvents = node is not RerouteUI;

		Editor = plug.CreateEditor( node, this );

		Cursor = CursorShape.Finger;
	}

	public virtual void Layout()
	{
	}

	protected override void OnHoverEnter( GraphicsHoverEvent e )
	{
		var display = Inner.DisplayInfo;
		ToolTip = NodeUI.FormatToolTip( display.Name, display.Description, PropertyType, null );
		base.OnHoverEnter( e );
	}

	protected void DrawHandle( Color color, Rect handleRect, HandleShape shape )
	{
		if ( !Visible )
			return;

		if ( !string.IsNullOrEmpty( Inner.ErrorMessage ) )
		{
			color = Theme.Red;
		}

		Paint.ClearPen();
		Paint.SetBrush( color.WithAlpha( 1.0f ) );

		switch ( shape )
		{
			case HandleShape.Square:
				Paint.DrawRect( handleRect, 2.0f );
				break;

			case HandleShape.Circle:
				Paint.DrawCircle( handleRect );
				//Paint.SetBrush( color.Darken( 0.25f ) );
				//Paint.DrawCircle( handleRect.Shrink( 1 ) );
				break;

			case HandleShape.Arrow:
				Paint.DrawPolygon( handleRect.TopLeft, handleRect.Center with { x = handleRect.Right },
					handleRect.BottomLeft );
				break;
		}

	}

	public float PreferredWidth
	{
		get
		{
			if ( !Visible || !Inner.ShowLabel )
			{
				return 0f;
			}

			Paint.SetDefaultFont();

			var titleWidth = Paint.MeasureText( Title ).x;

			if ( Inner.DisplayInfo.Group is not { } groupName )
			{
				return titleWidth + 24f;
			}

			Paint.SetDefaultFont( size: 7, italic: true );
			return Paint.MeasureText( $"{groupName} > " ).x + titleWidth + 24f;
		}
	}

	protected void DrawLabel( Rect rect, bool unreachable, TextFlag flags )
	{
		if ( !Inner.ShowLabel || Inner.InTitleBar || Editor is { Enabled: true, HideLabel: true } )
		{
			return;
		}

		var color = Node.PrimaryColor.Lighten( 0.8f );

		if ( unreachable )
		{
			color = color.WithAlpha( 0.5f );
		}

		Paint.SetPen( color );
		Paint.SetDefaultFont();

		var titleWidth = Paint.MeasureText( Title ).x;
		Paint.DrawText( rect.Shrink( 0f, 0f, 0f, 2f ), Title, flags );

		if ( Inner.DisplayInfo.Group is not { } groupName )
		{
			return;
		}

		Paint.SetPen( color.Darken( 0.2f ) );
		Paint.SetDefaultFont( size: 7, italic: true );

		Paint.DrawText( rect.Shrink( 0f, 0f, titleWidth, 0f ), $"{groupName} > ", flags );
	}

	public override string ToString() => Inner.ToString();

	public void MousePressed( GraphicsMouseEvent ev )
	{
		OnMousePressed( ev );
	}

	public void MouseMove( GraphicsMouseEvent ev )
	{
		OnMouseMove( ev );
	}

	public void MouseReleased( GraphicsMouseEvent ev )
	{
		OnMouseReleased( ev );
	}

	protected override void OnMousePressed( GraphicsMouseEvent e )
	{
		base.OnMousePressed( e );

		if ( e.LeftMouseButton && (!e.HasCtrl && !e.HasShift) )
		{
			if ( Inner is IPlugIn plugIn )
			{
				if ( plugIn.ConnectedOutput is not null )
				{
					var output = plugIn.ConnectedOutput;
					plugIn.ConnectedOutput = null;
					Reconnection = Node.Graph.Items.OfType<Connection>()
						.FirstOrDefault( x => x.Input == this && x.Output.Inner == output );
				}
			}

			e.Accepted = true;
		}
	}

	protected override void OnMouseReleased( GraphicsMouseEvent e )
	{
		if ( Dragging )
		{
			Node.DroppedPlug( this, e.ScenePosition );
		}
		else if ( IsReconnecting )
		{
			Reconnection.Output.Node.DroppedPlug( Reconnection.Output, e.ScenePosition, Reconnection );
			Reconnection = null;
		}

		Dragging = false;
		Cursor = CursorShape.Finger;
		Update();
	}

	protected override void OnMouseMove( GraphicsMouseEvent e )
	{
		// TODO - minimum distance move

		if ( IsReconnecting )
		{
			Reconnection.Output.Node.DraggingPlug( Reconnection.Output, e.ScenePosition, Reconnection );
			return;
		}
		else
		{
			Dragging = true;
			Node.DraggingPlug( this, e.ScenePosition );
		}

		Cursor = CursorShape.DragLink;
		Update();
		e.Accepted = true;
	}
}

public enum HandleShape
{
	Square,
	Circle,
	Arrow,
}

public record struct HandleConfig( string Name, Color Color, HandleShape Shape = HandleShape.Square );
