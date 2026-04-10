using Sandbox.Diagnostics;

namespace Editor.DooEditor;

/// <summary>
/// Control widget for editing Doo properties in the inspector.
/// Shows an "Edit" button that opens the Doo editor.
/// </summary>
[CustomEditor( typeof( Doo.Expression ) )]
public class DooExpressionControlWidget : ControlWidget
{
	Layout ContentLayout;

	readonly System.Type targetType;

	public DooExpressionControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Layout.Spacing = 4;
		Layout.Margin = 0;

		if ( property.TryGetAttribute<TypeHintAttribute>( out var typeHint ) )
		{
			targetType = typeHint.HintedType;
		}

		if ( property.GetValue<Doo.Expression>() == null )
		{
			property.SetValue( new Doo.LiteralExpression() { LiteralValue = new Variant( default, targetType ) } );
		}

		if ( targetType != null && property.GetValue<Doo.LiteralExpression>() is { } expression && expression.LiteralValue.Type == null )
		{
			expression.LiteralValue = new Variant( default, targetType );
		}

		property.OnChanged += _ => BuildContent();

		ContentLayout = Layout.AddRow( 1 );

		Layout.AddStretchCell();

		BuildContent();
	}

	protected override void OnPaint() { }

	public override void OnLabelContextMenu( ContextMenu menu )
	{
		var expr = SerializedProperty.GetValue<Doo.Expression>();

		if ( targetType != null )
		{
			bool active = expr is Doo.LiteralExpression ex && ex.LiteralValue.Type == targetType;

			var o = menu.AddOption( $"{targetType.Name}", "type_specimen", () =>
			{
				if ( active ) return;
				SerializedProperty.SetValue( new Doo.LiteralExpression() { LiteralValue = new Variant( null, targetType ) } );
				BuildContent();
			} );

			o.Checkable = true;
			o.Checked = active;
		}

		// Variable option
		{
			var o = menu.AddOption( "Variable", "subscript", () =>
			{
				SerializedProperty.SetValue( new Doo.VariableExpression() { VariableName = "x" } );
				BuildContent();
			} );

			o.Checkable = true;
			o.Checked = expr is Doo.VariableExpression;
		}

		// Literal submenu with value type options
		{
			var o = menu.AddOption( "Select Type...", "post_add", () =>
			{
				VariantControlWidget.OpenTypeSelector( this, targetType, t =>
				{
					SerializedProperty.SetValue( new Doo.LiteralExpression() { LiteralValue = new Variant( null, t ) } );
					BuildContent();
				} );
			} );

			o.Checkable = true;
			o.Checked = false;
		}

		menu.AddSeparator();

	}

	Doo.Expression _old;

	void BuildContent()
	{
		if ( !SerializedProperty.TryGetAsObject( out var so ) )
			return;

		var expr = SerializedProperty.GetValue<Doo.Expression>();

		if ( _old == expr ) return;
		_old = expr;

		ContentLayout.Clear( true );
		ContentLayout.Spacing = 4;

		if ( expr is Doo.LiteralExpression )
		{
			var literalProp = so.GetProperty( nameof( Doo.LiteralExpression.LiteralValue ) );
			Assert.NotNull( literalProp );
			ContentLayout.Add( ControlWidget.Create( literalProp ) );
		}
		else if ( expr is Doo.VariableExpression )
		{
			var nameProp = so.GetProperty( nameof( Doo.VariableExpression.VariableName ) );
			Assert.NotNull( nameProp );
			ContentLayout.Add( new DooVariableControlWidget( nameProp ) );
		}
	}
}
