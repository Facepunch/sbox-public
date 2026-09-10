namespace Sandbox.PanelGallery;

/// <summary>
/// The property pane. Fields come from <see cref="SerializedObject"/> - the same place the real
/// inspector gets them - so what's shown, what it's called and whether it's editable all match.
/// Only the controls are ours.
/// </summary>
public class Inspector : Panel
{
	GameObject target;

	public Inspector()
	{
		AddClass( "scroll inspector" );
	}

	public void Show( GameObject item )
	{
		target = item;
		Rebuild();
	}

	void Rebuild()
	{
		DeleteChildren( true );

		if ( !target.IsValid() )
		{
			Add.Label( "No object selected", "empty" );
			return;
		}

		BuildObjectSheet();

		foreach ( var component in target.Components.GetAll() )
		{
			BuildComponentSheet( component );
		}
	}

	/// <summary>
	/// The object itself - the header row and its transform, laid out the way the real inspector
	/// lays them out rather than as a raw property list.
	/// </summary>
	void BuildObjectSheet()
	{
		var header = Add.Panel( "objectheader" );

		header.Icon( Hierarchy.IconFor( target ), "typeicon" );

		var enabled = header.AddChild( new Checkbox( target.Enabled ) );
		enabled.OnChange = value => { if ( target.IsValid() ) target.Enabled = value; };

		var name = header.AddChild( new TextInput( target.Name, null, true ) );
		name.AddClass( "name" );
		name.OnChange = value => { if ( target.IsValid() ) target.Name = value; };

		var tagRow = Add.Panel( "objecttags" );
		tagRow.Icon( "sell" );

		var tags = target.Tags.TryGetAll().ToArray();
		tagRow.Add.Label( tags.Length > 0 ? string.Join( ", ", tags ) : "No tags", tags.Length > 0 ? "value" : "value none" );

		var body = Section( "Transform", null, "open_with" );

		VectorField( body, "Local Position", target.LocalPosition, v => { if ( target.IsValid() ) target.LocalPosition = v; } );

		var angles = target.LocalRotation.Angles();

		VectorField( body, "Local Rotation", new Vector3( angles.pitch, angles.yaw, angles.roll ),
			v => { if ( target.IsValid() ) target.LocalRotation = Rotation.From( v.x, v.y, v.z ); } );

		VectorField( body, "Local Scale", target.LocalScale, v => { if ( target.IsValid() ) target.LocalScale = v; } );
	}

	/// <summary>
	/// One component's properties, filtered the same way the real component sheet filters them.
	/// </summary>
	void BuildComponentSheet( Component component )
	{
		var serialized = component.GetSerialized();
		var title = string.IsNullOrEmpty( serialized?.TypeTitle ) ? component.GetType().Name : serialized.TypeTitle;

		var body = Section( title, new Checkbox( component.Enabled ) { OnChange = v => component.Enabled = v }, Hierarchy.IconForComponent( component ) );

		if ( serialized is null )
		{
			body.Add.Label( "Nothing to show", "label" );
			return;
		}

		var shown = 0;

		// Group headers, the way the real sheet breaks properties up. Grouped rather than just
		// watching for the name changing, or a group that comes back round gets a second header.
		var groups = serialized
			.Where( ShouldShow )
			.OrderBy( x => x.Order )
			.GroupBy( x => x.GroupName ?? "" );

		foreach ( var group in groups )
		{
			var target = string.IsNullOrEmpty( group.Key ) ? body : Group( body, group.Key );

			foreach ( var property in group )
			{
				if ( BuildProperty( target, property ) ) shown++;
			}
		}

		if ( shown == 0 ) body.Add.Label( "Nothing to show", "label" );
	}

	/// <summary>
	/// Same rules as ComponentSheet.FilterProperties - a component only shows what's marked as a
	/// property, minus the advanced ones and the component event callbacks.
	/// </summary>
	static bool ShouldShow( SerializedProperty property )
	{
		if ( property.PropertyType is null ) return false;

		// These live in the events tab in the real inspector, and we don't have one
		if ( property.PropertyType.IsAssignableTo( typeof( Delegate ) ) ) return false;

		if ( property.HasAttribute<AdvancedAttribute>() ) return false;
		if ( property.IsMethod ) return false;
		if ( !property.HasAttribute<PropertyAttribute>() ) return false;

		return true;
	}

	/// <summary>
	/// A control for the property, picked from its type. Anything we don't have an editor for is
	/// shown read only rather than skipped, so nothing silently goes missing.
	/// </summary>
	bool BuildProperty( Panel section, SerializedProperty property )
	{
		var label = string.IsNullOrEmpty( property.DisplayName ) ? property.Name : property.DisplayName;
		var type = property.PropertyType;
		var editable = property.IsEditable;

		// Optional values - an unset one isn't zero, so say so rather than showing a control
		// sitting at the bottom of its range
		var nullable = Nullable.GetUnderlyingType( type );

		if ( nullable is not null )
		{
			if ( property.GetValue<object>() is null )
			{
				Row( section, label, "not set" );
				return true;
			}

			type = nullable;
		}

		if ( type == typeof( bool ) )
		{
			var toggle = Field( section, label ).AddChild( new Checkbox( property.GetValue<bool>() ) );
			if ( editable ) toggle.OnChange = v => property.SetValue( v );
			return true;
		}

		if ( type == typeof( float ) || type == typeof( double ) )
		{
			var number = property.GetValue<float>();

			if ( property.TryGetAttribute<RangeAttribute>( out var range ) )
			{
				var slider = Field( section, label ).AddChild( new Slider( range.Min, range.Max, number ) );
				if ( editable ) slider.OnChange = v => property.SetValue( v );
				return true;
			}

			var box = Field( section, label ).AddChild( new NumberBox( null, number, 0.05f ) );
			if ( editable ) box.OnChange = v => property.SetValue( v );
			return true;
		}

		if ( type == typeof( int ) )
		{
			var box = Field( section, label ).AddChild( new NumberBox( null, property.GetValue<int>(), 0.25f ) );
			if ( editable ) box.OnChange = v => property.SetValue( (int)v );
			return true;
		}

		if ( type == typeof( string ) )
		{
			var input = Field( section, label ).AddChild( new TextInput( property.GetValue<string>() ?? "", null, true ) );
			if ( editable ) input.OnChange = v => property.SetValue( v );
			return true;
		}

		if ( type == typeof( Vector3 ) )
		{
			VectorField( section, label, property.GetValue<Vector3>(), v => { if ( editable ) property.SetValue( v ); } );
			return true;
		}

		if ( type == typeof( Vector2 ) )
		{
			var vector = property.GetValue<Vector2>();
			var control = Field( section, label );

			var x = control.AddChild( new NumberBox( "X", vector.x, 0.5f ) );
			var y = control.AddChild( new NumberBox( "Y", vector.y, 0.5f ) );

			x.OnChange = v => { if ( editable ) property.SetValue( new Vector2( v, y.Value ) ); };
			y.OnChange = v => { if ( editable ) property.SetValue( new Vector2( x.Value, v ) ); };
			return true;
		}

		if ( type == typeof( Rotation ) || type == typeof( Angles ) )
		{
			var angles = type == typeof( Angles ) ? property.GetValue<Angles>() : property.GetValue<Rotation>().Angles();

			VectorField( section, label, new Vector3( angles.pitch, angles.yaw, angles.roll ), v =>
			{
				if ( !editable ) return;

				if ( type == typeof( Angles ) ) property.SetValue( new Angles( v.x, v.y, v.z ) );
				else property.SetValue( Rotation.From( v.x, v.y, v.z ) );
			} );

			return true;
		}

		if ( type == typeof( Color ) )
		{
			var colour = property.GetValue<Color>();
			var control = Field( section, label );

			var swatch = control.Add.Panel( "swatch" );
			swatch.Style.BackgroundColor = colour;
			control.Add.Label( colour.Hex );
			return true;
		}

		if ( type.IsEnum )
		{
			var value = property.GetValue<object>();

			// A flags enum reads as the flags that are on, not as a number
			var text = value is null ? "None" : type.IsDefined( typeof( FlagsAttribute ), false )
				? FlagText( type, value )
				: value.ToString();

			ComboField( section, label, text );
			return true;
		}

		// Everything else reads as text - resources, components, curves, whatever it is
		Row( section, label, Describe( property.GetValue<object>() ) );
		return true;
	}

	/// <summary>
	/// Which flags are set, as names. Nothing set reads as None.
	/// </summary>
	static string FlagText( Type type, object value )
	{
		var text = value.ToString();

		if ( text == "0" ) return "None";

		return text;
	}

	static string Describe( object value )
	{
		return value switch
		{
			null => "none",
			GameObject gameObject => gameObject.Name,
			Component component => component.GetType().Name,
			Resource resource => resource.ResourceName,
			string text => text,
			BBox box => $"{box.Size.x:0.#} x {box.Size.y:0.#} x {box.Size.z:0.#}",
			TagSet tags => tags.TryGetAll().Any() ? string.Join( ", ", tags.TryGetAll() ) : "none",

			// ToString on most types is the type name, which reads as a namespace path
			_ => value.GetType().IsPrimitive ? value.ToString() : value.GetType().Name,
		};
	}

	/// <summary>
	/// A collapsible group of properties inside a component, like the real sheet's.
	/// </summary>
	static Panel Group( Panel section, string title )
	{
		var group = section.Add.Panel( "group" );

		// Not "head" and "body" - those are the section's own classes, and a group sits inside one
		var head = group.Add.Panel( "grouphead" );
		var arrow = head.Icon( "remove", "arrow" );
		head.Add.Label( title );

		var body = group.Add.Panel( "groupbody" );

		head.AddEventListener( "onclick", () =>
		{
			var open = body.Style.Display != DisplayMode.None;
			body.Style.Display = open ? DisplayMode.None : DisplayMode.Flex;
			arrow.Text = open ? "add" : "remove";
		} );

		return body;
	}

	Panel Section( string title, Panel control = null, string icon = null )
	{
		var section = Add.Panel( "section" );

		var head = section.Add.Panel( "head" );
		var arrow = head.Icon( "expand_more", "arrow" );

		if ( icon is not null ) head.Icon( icon, "typeicon" );
		if ( control is not null ) head.AddChild( control );

		head.Add.Label( title );
		head.Add.Panel( "grow" );
		head.Icon( "more_horiz", "menu" );

		var body = section.Add.Panel( "body" );

		head.AddEventListener( "onclick", () =>
		{
			var open = body.Style.Display != DisplayMode.None;
			body.Style.Display = open ? DisplayMode.None : DisplayMode.Flex;
			arrow.Text = open ? "chevron_right" : "expand_more";
		} );

		return body;
	}

	static Panel Field( Panel section, string label )
	{
		var field = section.Add.Panel( "field" );
		field.Add.Label( label, "label" );
		return field.Add.Panel( "control" );
	}

	static void Row( Panel section, string label, string value )
	{
		var control = Field( section, label );
		control.Add.Label( value, "readonly" );
	}

	static void ComboField( Panel section, string label, string value )
	{
		var control = Field( section, label );
		var combo = control.Add.Panel( "combo" );
		combo.Add.Label( value, "value" );
		combo.Icon( "expand_more" );
	}

	static void VectorField( Panel section, string label, Vector3 value, Action<Vector3> onChange )
	{
		var control = Field( section, label );

		var current = value;

		var x = control.AddChild( new NumberBox( "X", value.x, 0.5f ) );
		var y = control.AddChild( new NumberBox( "Y", value.y, 0.5f ) );
		var z = control.AddChild( new NumberBox( "Z", value.z, 0.5f ) );

		x.OnChange = v => { current.x = v; onChange( current ); };
		y.OnChange = v => { current.y = v; onChange( current ); };
		z.OnChange = v => { current.z = v; onChange( current ); };
	}
}
