namespace Editor;

class GameObjectHeader : Widget
{
	public SerializedObject Target { get; }

	public GameObjectHeader( Widget parent, SerializedObject targetObject ) : base( parent )
	{
		Target = targetObject;

		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.CanShrink;

		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;

		//var networkModeControl = new NetworkModeControlWidget( targetObject );

		// top section
		{
			var topRow = Layout.AddRow();
			topRow.Spacing = 4;
			topRow.Margin = 8;

			// big icon left
			{
				var left = topRow.AddRow();
				left.Add( new GameObjectIconButton( this ) );
			}

			// 2 rows right
			{
				var right = topRow.AddColumn();
				right.Spacing = 2;

				{
					var top = right.AddRow();
					top.Spacing = 4;
					top.Add( new GameObjectEnabledWidget( targetObject.GetProperty( nameof( GameObject.Enabled ) ) ) );
					var s = top.Add( ControlWidget.Create( targetObject.GetProperty( nameof( GameObject.Name ) ) ), 1 );
					s.HorizontalSizeMode = SizeMode.Flexible;
					top.Add( new GameObjectStaticWidget( targetObject.GetProperty( nameof( GameObject.IsStatic ) ) ) );
					top.Add( new GameObjectFlagsWidget( targetObject ) );
				}

				{
					var bottom = right.AddRow();
					bottom.Spacing = 4;
					//bottom.Add( networkModeControl );
					// TODO: Remove these
					//bottom.Add( new BoolControlWidget( targetObject.GetProperty( nameof( GameObject.NetworkInterpolation ) ) ) { Icon = "linear_scale" } );
					//bottom.Add( new AdvancedNetworkControlWidget( targetObject ) );
					bottom.Add( new NetworkModeControlWidget( targetObject ) );
					var s = bottom.Add( ControlWidget.Create( targetObject.GetProperty( nameof( GameObject.Tags ) ) ), 1 );
					s.HorizontalSizeMode = SizeMode.Flexible;
				}
			}
		}
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground );
		Paint.DrawRect( LocalRect );
	}

}

/// <summary>
/// Draggable icon.
/// </summary>
file sealed class GameObjectIconButton : IconButton
{
	private readonly GameObjectHeader _parent;

	private Drag _drag;

	public GameObjectIconButton( GameObjectHeader parent )
		: base( "📦" )
	{
		_parent = parent;

		FixedHeight = Theme.RowHeight * 2;
		FixedWidth = Theme.RowHeight * 2;
		IconSize = 27;
		Background = Color.Transparent;

		IsDraggable = !parent.Target.IsMultipleTargets;
	}

	protected override void OnDragStart()
	{
		base.OnDragStart();

		var target = _parent.Target.Targets.OfType<GameObject>().FirstOrDefault();

		if ( target is null ) return;

		_drag = new Drag( this )
		{
			Data = { Object = target, Text = target.Name }
		};

		_drag.Execute();
	}

	protected override void OnPaint()
	{
		Background = _drag.IsValid() ? Theme.Pink.WithAlpha( 0.6f ) : Color.Transparent;

		base.OnPaint();

		if ( _drag.IsValid() )
		{
			Update();
		}
	}
}

file sealed class GameObjectStaticWidget : BoolControlWidget
{
	public GameObjectStaticWidget( SerializedProperty property )
		: base( property )
	{
		ToolTip = "Static - this object never moves during gameplay";
		MinimumWidth = 64;
		Icon = "";
	}

	protected override Vector2 SizeHint() => new( 64, Theme.RowHeight );

	protected override void OnPaint()
	{
		// base paints the background, checked fill and hover ring; we add icon + label
		base.OnPaint();

		var rect = LocalRect.Shrink( 8, 2 );
		var isOn = !SerializedProperty.IsMultipleDifferentValues && IsChecked;

		Paint.SetPen( isOn ? Tint : Theme.Text.WithAlpha( 0.7f ) );
		Paint.DrawIcon( rect, "anchor", 13, TextFlag.LeftCenter );
		Paint.SetDefaultFont();
		Paint.DrawText( rect, "Static", TextFlag.RightCenter );
	}
}

file sealed class GameObjectEnabledWidget : BoolControlWidget
{
	public GameObjectEnabledWidget( SerializedProperty property )
		: base( property )
	{
		Icon = "power_settings_new";
		Tint = Theme.Green;

		IsDraggable = !property.Parent.IsMultipleTargets;
	}

	protected override void OnDragStart()
	{
		base.OnDragStart();

		var drag = new Drag( this )
		{
			Data = { Object = SerializedProperty, Text = SerializedProperty.As.String }
		};

		drag.Execute();
	}
}
