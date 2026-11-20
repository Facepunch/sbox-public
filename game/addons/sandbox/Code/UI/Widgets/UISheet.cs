using Sandbox.Internal;
using Sandbox.UI;
using Sandbox.UI.Construct;

namespace Sandbox;

public class UISheet : Panel, IControlSheet
{
	/// <summary>
	/// The object whose properties we show, or a list of already serialized properties.
	/// </summary>
	[Parameter] public object Target { get; set; }

	/// <summary>
	/// Decides which properties get a row. All of them when not set, <see cref="PropertiesAndButtons"/> is the usual choice.
	/// </summary>
	[Parameter] public Func<SerializedProperty, bool> PropertyFilter { get; set; }

	/// <summary>
	/// A filter for <see cref="PropertyFilter"/>: members marked [Property] and methods marked [Button], without delegates or [Hide].
	/// </summary>
	public static bool PropertiesAndButtons( SerializedProperty property )
	{
		if ( property.IsMethod ) return property.HasAttribute<ButtonAttribute>();
		if ( property.PropertyType is null || property.PropertyType.IsAssignableTo( typeof( Delegate ) ) ) return false;
		if ( property.HasAttribute<HideAttribute>() ) return false;

		return property.HasAttribute<PropertyAttribute>();
	}

	Panel _body;
	int _hash;

	public UISheet()
	{
		AddClass( "sheet" );
	}

	/// <summary>
	/// Throws the rows away and makes them again from the target.
	/// </summary>
	public void Rebuild()
	{
		IControlSheet sheet = this;

		_body?.Delete( true );
		_body = AddChild<Panel>( "body" );

		if ( Target is null ) return;

		if ( Target is List<SerializedProperty> properties )
		{
			IControlSheet.FilterSortAndAdd( sheet, properties );
			return;
		}

		var serialized = Game.TypeLibrary.GetSerializedObject( Target );
		IControlSheet.FilterSortAndAdd( sheet, serialized.ToList() );
	}

	public override void Tick()
	{
		base.Tick();

		var hash = HashCode.Combine( Target );
		if ( hash == _hash && _body.IsValid() ) return;

		_hash = hash;
		Rebuild();
	}

	void IControlSheet.AddFeature( IControlSheet.Feature feature )
	{
		((IControlSheet)this).AddPropertiesWithGrouping( feature.Properties );
	}

	void IControlSheet.AddGroup( IControlSheet.Group group )
	{
		var panel = _body.AddChild<UISheetGroup>();
		panel.Initialize( group );
	}

	bool IControlSheet.TestFilter( SerializedProperty prop ) => PropertyFilter?.Invoke( prop ) ?? true;
}

/// <summary>
/// A titled run of rows. A toggle group has a checkbox in its title that shows and hides the rows.
/// </summary>
public class UISheetGroup : Panel
{
	Panel _header;
	Panel _rows;
	SerializedProperty _toggle;
	InspectorVisibilityAttribute[] _visibility;

	public UISheetGroup()
	{
		AddClass( "sheet-group" );
	}

	internal void Initialize( IControlSheet.Group group )
	{
		var title = group.Name;

		// the property named like the group switches it on and off
		var toggle = group.Properties.FirstOrDefault( x => x.HasAttribute<ToggleGroupAttribute>() && x.Name == group.Name );
		if ( toggle is not null )
		{
			toggle.TryGetAttribute<ToggleGroupAttribute>( out var attribute );
			group.Properties.Remove( toggle );

			title = attribute?.Label ?? title;
			_toggle = toggle;
			_visibility = toggle.GetAttributes<InspectorVisibilityAttribute>()?.ToArray();
		}

		// buttons for the whole group go in its title instead of a row
		var headerButtons = group.Properties.Where( x => x.IsMethod && x.HasAttribute<HeaderButtonAttribute>() ).ToList();
		foreach ( var button in headerButtons )
			group.Properties.Remove( button );

		if ( !string.IsNullOrWhiteSpace( title ) || _toggle is not null || headerButtons.Count > 0 )
		{
			_header = Add.Panel( "header" );

			if ( _toggle is not null )
				_header.AddChild( new UICheckbox { Property = _toggle } );

			var label = _header.Add.Label( "", "title" );
			label.Text = title ?? "";

			foreach ( var method in headerButtons )
			{
				var button = _header.AddChild<UIButton>( "header-button" );
				button.Method = method;
				button.MakeIconOnly();
			}
		}

		_rows = Add.Panel( "rows" );

		UISheetRow last = null;

		foreach ( var property in group.Properties )
		{
			// shares the row above, like the second button of a pair
			if ( last is not null && property.HasAttribute<SameLineAttribute>() )
			{
				last.AddToRow( property );
				continue;
			}

			if ( property.TryGetAttribute<SpaceAttribute>( out var space ) )
				_rows.Add.Panel( "space" ).Style.Height = space.Height;

			if ( property.HasAttribute<SeparatorAttribute>() )
				_rows.AddChild<UISeparator>();

			last = _rows.AddChild<UISheetRow>();
			last.Initialize( property );
		}
	}

	public override void Tick()
	{
		base.Tick();

		if ( _toggle is not null )
			_rows?.SetClass( "hidden", !_toggle.As.Bool );

		if ( _visibility?.Length > 0 )
			SetClass( "hidden", UISheetRow.IsHidden( _visibility, _toggle?.Parent ) );
	}
}

/// <summary>
/// One property, its name on the left and its control on the right, wide controls like the resource grid go under the name instead.
/// A button has no name and takes the row, and the buttons that follow with [SameLine] share it.
/// </summary>
public class UISheetRow : Panel
{
	SerializedProperty _property;
	InspectorVisibilityAttribute[] _visibility;
	EnabledIfAttribute _enabledIf;
	Panel _holder;

	// the controls of the properties added with [SameLine], they show, hide and grey out by their own conditions
	readonly List<(Panel control, SerializedProperty property, InspectorVisibilityAttribute[] visibility, EnabledIfAttribute enabledIf)> _sharing = new();

	public UISheetRow()
	{
		AddClass( "sheet-row" );
	}

	internal void Initialize( SerializedProperty property )
	{
		_property = property;
		_visibility = property.GetAttributes<InspectorVisibilityAttribute>()?.ToArray();

		// a button checks its own condition
		if ( !property.IsMethod )
			property.TryGetAttribute( out _enabledIf );

		var control = CreateControl( property, out var wide );
		var isButton = control is UIButton;
		var isToggle = control is UICheckbox;
		var hasName = !isButton && !property.HasAttribute<HideLabelAttribute>();

		SetClass( "wide", wide );
		SetClass( "toggle", isToggle );
		SetClass( "buttons", isButton );
		SetClass( "bare", !hasName );

		// a checkbox goes in front of its name, every other control after it
		if ( isToggle )
			_holder = Add.Panel( "control" );

		if ( hasName )
		{
			// only the name shows the description, not the control next to it
			var label = AddChild( new UIHintLabel { Hint = property.Description } );
			label.AddClass( "label" );
			label.Text = property.DisplayName;
		}

		_holder ??= Add.Panel( "control" );
		if ( control is not null )
			_holder.AddChild( control );
	}

	/// <summary>
	/// Another property on the same row, after the ones already there.
	/// </summary>
	internal void AddToRow( SerializedProperty property )
	{
		var control = CreateControl( property, out _ );
		if ( control is null ) return;

		_holder.AddChild( control );

		var visibility = property.GetAttributes<InspectorVisibilityAttribute>()?.ToArray();

		// a button checks its own condition
		EnabledIfAttribute enabledIf = null;
		if ( !property.IsMethod )
			property.TryGetAttribute( out enabledIf );

		if ( visibility?.Length > 0 || enabledIf is not null )
			_sharing.Add( (control, property, visibility, enabledIf) );
	}

	public override void Tick()
	{
		base.Tick();

		if ( _visibility?.Length > 0 && _property?.Parent is { } parent )
			SetClass( "hidden", IsHidden( _visibility, parent ) );

		if ( _enabledIf is not null )
			SetClass( "disabled", !_enabledIf.Test( _property?.Parent ) );

		foreach ( var (control, property, visibility, enabledIf) in _sharing )
		{
			if ( property.Parent is null ) continue;

			if ( visibility?.Length > 0 )
				control.SetClass( "hidden", IsHidden( visibility, property.Parent ) );

			if ( enabledIf is not null )
				control.SetClass( "disabled", !enabledIf.Test( property.Parent ) );
		}
	}

	/// <summary>
	/// Whether every one of the conditions says to hide.
	/// </summary>
	internal static bool IsHidden( InspectorVisibilityAttribute[] visibility, SerializedObject parent ) => visibility.All( x => x.TestCondition( parent ) );

	/// <summary>
	/// Pick the control for a property. Anything we don't have a Sandbox one for gets the engine's.
	/// </summary>
	static Panel CreateControl( SerializedProperty property, out bool wide )
	{
		wide = false;

		if ( property.IsMethod )
			return property.HasAttribute<ButtonAttribute>() ? new UIButton { Method = property } : null;

		var type = property.PropertyType;
		if ( type is null || Nullable.GetUnderlyingType( type ) is not null )
			return BaseControl.CreateFor( property );

		if ( type == typeof( bool ) )
			return new UICheckbox { Property = property };

		if ( type.IsEnum )
			return new UIEnumControl { Property = property };

		if ( type == typeof( Color ) )
			return new UIColor { Property = property };

		if ( UIVector.Supports( type ) )
			return new UIVector { Property = property };

		if ( type == typeof( string ) && property.TryGetAttribute<ResourceSelectAttribute>( out var resourceSelect ) )
		{
			if ( resourceSelect.Popup )
				return new ResourceSelectControl { Property = property };

			wide = true;
			return new UIResourceGrid { Property = property };
		}

		if ( type == typeof( string ) && property.HasAttribute<RopeMaterialsAttribute>() )
		{
			wide = true;
			return new RopeMaterialControl { Property = property };
		}

		if ( type == typeof( string ) && property.HasAttribute<FontNameAttribute>() )
			return new UIFontControl { Property = property };

		var isNumber = type == typeof( float ) || type == typeof( double ) || type == typeof( int );
		if ( isNumber && property.HasAttribute<RangeAttribute>() )
			return new UISlider { Property = property };

		var control = BaseControl.CreateFor( property );

		// the engine's plain text box, in our look and with the placeholder of the property.
		// The special strings (input actions, fonts, icons) have controls of their own and keep them
		if ( type == typeof( string ) && control is TextEntry entry && entry.GetType() == typeof( TextEntry ) )
		{
			entry.AddClass( "text-field" );

			if ( property.TryGetAttribute<PlaceholderAttribute>( out var placeholder ) )
				entry.Placeholder = placeholder.Value;

			// [TextArea] is a tall box that takes new lines, under the name like the other wide controls
			if ( property.HasAttribute<TextAreaAttribute>() )
			{
				wide = true;
				entry.Multiline = true;
				entry.AddClass( "text-area" );
			}
		}

		return control;
	}
}
