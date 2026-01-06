namespace Sandbox.UI;

public class BaseControl : Panel
{
	SerializedProperty _property;

	[Parameter]
	public SerializedProperty Property
	{
		get => _property;
		set
		{
			if ( _property == value ) return;

			_property = value;
			Rebuild();
		}
	}

	/// <summary>
	/// Called whenever SerializedProperty changes to another property and we think we probably need to rebuild
	/// </summary>
	public virtual void Rebuild()
	{

	}

	int valueHash = 0;

	public override void Tick()
	{
		base.Tick();

		if ( HasFocus ) return;
		if ( _property is null ) return;

		var val = _property.GetValue<object>( default );
		var hash = HashCode.Combine( val );
		if ( hash == valueHash ) return;

		valueHash = hash;
		StateHasChanged();
	}

	public virtual bool SupportsMultiEdit => false;

	/// <summary>
	/// Create a BaseControl for a given SerializedProperty.
	/// We'll look at BaseControls with [CustomEditor] attributes and see if any of them can handle this property.
	/// </summary>
	public static BaseControl CreateFor( SerializedProperty property )
	{
		ArgumentNullException.ThrowIfNull( property, nameof( property ) );

		var type = property.PropertyType;

		var allAttributes = Game.TypeLibrary.GetTypesWithAttribute<CustomEditorAttribute>( false )
					.Where( x => x.Type.TargetType.IsAssignableTo( typeof( BaseControl ) ) )
					.ToArray();

		var allEditors = allAttributes
							.Select( x => new { score = x.Attribute.GetEditorScore( property ), editor = x } )
							.Where( x => x.score > 0 )
							.OrderByDescending( x => x.score )
							.ToArray();

		// Use the first editor we can successfully create
		foreach ( var entry in allEditors )
		{
			var c = entry.editor.Type.Create<BaseControl>();
			if ( c is not null )
			{
				if ( property.IsMultipleValues && !c.SupportsMultiEdit )
				{
					c.Delete( true );
					return new MultiEditNotSupported( property );
				}

				c.Property = property;
				return c;
			}
		}

		// Nope - sorry, nothing for you
		if ( property.IsMethod )
			return null;

		return new MissingEditor( property );
	}
}

file class MultiEditNotSupported : BaseControl
{
	Label label;

	public MultiEditNotSupported( SerializedProperty property )
	{
		label = AddChild<Label>();
		label.Text = "MultiEditNotSupported";

		Property = property;
	}
}

file class MissingEditor : BaseControl
{
	Label label;

	public MissingEditor( SerializedProperty property )
	{
		label = AddChild<Label>();
		label.Text = $"No Editor Found For {property.PropertyType.Name}";

		Property = property;
	}
}
