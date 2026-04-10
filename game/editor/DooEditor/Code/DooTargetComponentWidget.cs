namespace Editor.DooEditor;

/// <summary>
/// Control widget for editing Doo.TargetComponent properties in the inspector.
/// </summary>
[CustomEditor( typeof( Doo.TargetComponent ) )]
public class DooTargetComponentWidget : ControlWidget
{
	Layout ContentLayout;

	public override bool IsWideMode => true;
	public override bool IncludeLabel => true;

	public DooTargetComponentWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Layout.Spacing = 4;
		Layout.Margin = 0;

		if ( SerializedProperty.GetValue<Doo.TargetComponent>() == null )
		{
			SerializedProperty.SetValue( new Doo.TargetComponent() { Type = Doo.TargetComponent.TargetType.Direct } );
		}

		SerializedProperty.OnChanged += OnPropertyChanged;

		ContentLayout = Layout.AddColumn( 1 );
		Layout.AddStretchCell();

		BuildContent();
	}

	public override void OnDestroyed()
	{
		SerializedProperty?.OnChanged -= OnPropertyChanged;

		base.OnDestroyed();
	}

	void OnPropertyChanged( SerializedProperty property )
	{
		if ( property != SerializedProperty )
			return;

		BuildContent();
	}

	protected override void OnPaint() { }

	public override void OnLabelContextMenu( ContextMenu menu )
	{
		var expr = SerializedProperty.GetValue<Doo.TargetComponent>();


		var literal = menu.AddOption( $"Component Literal", "type_specimen", () =>
		{
			SerializedProperty.SetValue( new Doo.TargetComponent() { Type = Doo.TargetComponent.TargetType.Direct } );
			BuildContent();
		} );

		literal.Checkable = true;
		literal.Checked = expr.Type == Doo.TargetComponent.TargetType.Direct;

		var fromGo = menu.AddOption( $"From GameObject", "entity", () =>
		{
			SerializedProperty.SetValue( new Doo.TargetComponent() { Type = Doo.TargetComponent.TargetType.GameObject } );
			BuildContent();
		} );

		fromGo.Checkable = true;
		fromGo.Checked = expr.Type == Doo.TargetComponent.TargetType.GameObject;

		var fromVar = menu.AddOption( $"From Variable", "variable", () =>
		{
			SerializedProperty.SetValue( new Doo.TargetComponent() { Type = Doo.TargetComponent.TargetType.Variable } );
			BuildContent();
		} );

		fromVar.Checkable = true;
		fromVar.Checked = expr.Type == Doo.TargetComponent.TargetType.Variable;

		menu.AddSeparator();
	}


	Doo.TargetComponent _old;

	void BuildContent()
	{
		if ( !SerializedProperty.TryGetAsObject( out var so ) )
			return;

		var expr = SerializedProperty.GetValue<Doo.TargetComponent>();

		if ( _old == expr ) return;
		_old = expr;

		ContentLayout.Clear( true );
		ContentLayout.Spacing = 4;

		var cs = new ControlSheet();
		ContentLayout.Add( cs );

		if ( expr.Type == Doo.TargetComponent.TargetType.Direct )
		{
			var prop = so.GetProperty( nameof( Doo.TargetComponent.ComponentValue ) );
			cs.AddRow( prop );
		}

		if ( expr.Type == Doo.TargetComponent.TargetType.GameObject )
		{
			cs.AddRow( so.GetProperty( nameof( Doo.TargetComponent.GameObjectValue ) ) );
			cs.AddControl<ComponentTypeControlWidget>( so.GetProperty( nameof( Doo.TargetComponent.ComponentType ) ) );
			cs.AddRow( so.GetProperty( nameof( Doo.TargetComponent.FindMode ) ) );
		}

		if ( expr.Type == Doo.TargetComponent.TargetType.Variable )
		{
			cs.AddControl<DooVariableControlWidget>( so.GetProperty( nameof( Doo.TargetComponent.VariableName ) ) );
			cs.AddControl<ComponentTypeControlWidget>( so.GetProperty( nameof( Doo.TargetComponent.ComponentType ) ) );
			cs.AddRow( so.GetProperty( nameof( Doo.TargetComponent.FindMode ) ) );
		}

		ContentLayout.AddSpacingCell( 8 );
	}
}

public class ComponentTypeControlWidget : ControlWidget
{
	public ComponentTypeControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Layout.Spacing = 4;
		Layout.Margin = 0;

		Cursor = CursorShape.Finger;
	}

	protected override void PaintOver()
	{
		base.PaintOver();

		var o = SerializedProperty.GetValue<object>();

		string icon = "broken_image";
		string title = "Component Type";

		if ( o is string s )
		{
			var t = TypeLibrary.GetType( s );
			title = t?.Title ?? title;
			icon = t?.Icon ?? icon;
		}

		Paint.Pen = Theme.Green;
		Paint.DrawIcon( LocalRect.Shrink( 4 ), icon, 17, TextFlag.LeftCenter );

		Paint.SetDefaultFont();
		Paint.Pen = Theme.Green;
		Paint.DrawText( LocalRect.Shrink( 28, 4, 4, 4 ), title, TextFlag.LeftCenter );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( e.Button != MouseButtons.Left )
			return;

		var popup = new ComponentTypeSelector( this );
		popup.OnSelect += ( t ) => SerializedProperty.SetValue( t.FullName );
		popup.OpenAt( ScreenRect.BottomLeft, animateOffset: new Vector2( 0, -4 ) );
		popup.MinimumWidth = Width;
	}
}
