namespace Editor;

// The component list will be rebuilt on hotload, so we can safely skip
[SkipHotload]
public class ComponentSheetHeader : InspectorHeader
{
	SerializedObject TargetObject { get; init; }
	TypeDescription TargetType { get; init; }
	ComponentSheet Sheet { get; set; }
	ControlWidget EnabledWidget { get; set; }

	public ComponentSheetHeader( SerializedObject target, ComponentSheet parent ) : base()
	{
		TargetObject = target;
		Sheet = parent;

		var t = target.Targets.FirstOrDefault();
		TargetType = TypeLibrary.GetType( t.GetType() );

		Title = TargetObject.TypeTitle;
		Icon = TargetObject.TypeIcon;
		Color = Theme.Blue;

		IsExpanded = Sheet.Expanded;
		ToolTip = GetTooltip();

		IsDraggable = !target.IsMultipleTargets;

		TargetObject.OnPropertyFinishEdit += TargetObject_PropertyChanged;
		ContextMenu += FillContextMenu;

		var tint = TypeLibrary.GetAttribute<TintAttribute>( t.GetType() );
		if ( tint is not null ) Color = Theme.GetTint( tint.Tint );

		UpdateOverrideState();

		var helpUrl = TypeLibrary.GetAttribute<HelpUrlAttribute>( t.GetType() );
		if ( helpUrl is not null ) HelpUrl = helpUrl.Url;
	}

	protected override Widget BuildIcons()
	{
		if ( TargetObject is null )
			return default;

		var propEnabled = TargetObject.GetProperty( "Enabled" );
		if ( propEnabled is not null )
		{
			EnabledWidget = new ComponentEnabledWidget( propEnabled ) { Tint = Color };
			return EnabledWidget;
		}

		return default;
	}

	protected override bool IsTargetDisabled()
	{
		var propEnabled = TargetObject.GetProperty( "Enabled" );
		if ( propEnabled is null ) return false;

		return !propEnabled.As.Bool;
	}

	private void TargetObject_PropertyChanged( SerializedProperty property )
	{
		Update();
		UpdateOverrideState();
	}

	private void UpdateOverrideState()
	{
		var component = TargetObject.Targets.FirstOrDefault() as Component;

		if ( !component.IsValid() ) return;

		if ( EditorUtility.Prefabs.IsComponentAddedToInstance( component ) )
		{
			IconOverlay = "add_circle";
		}
		else
		{
			IconOverlay = string.Empty;
		}
	}

	public override void OnDestroyed()
	{
		TargetObject.OnPropertyFinishEdit -= TargetObject_PropertyChanged;

		base.OnDestroyed();
	}

	public Component GetComponent()
	{
		return TargetObject?.Targets?.FirstOrDefault() as Component;
	}

	protected override void OnDragStart()
	{
		if ( Children.Any( x => x.IsPressed ) ) return;

		base.OnDragStart();

		dragData = new ComponentHeaderDrag( this );
		dragData.Data.Object = TargetObject;
		dragData.Execute();
	}

	protected override void OnExpandChanged()
	{
		base.OnExpandChanged();
		Sheet.SetExpanded( IsExpanded );
	}

	void FillContextMenu( Menu menu )
	{
		{
			var o = menu.AddOption( "View Legacy Actions", "sentiment_very_dissatisfied" );
			o.Checkable = true;
			o.Checked = Sheet.ViewMode == ComponentViewMode.Events;
			o.Toggled = b => Sheet.ViewMode = b ? ComponentViewMode.Events : ComponentViewMode.Default;
		}
		{
			var o = menu.AddOption( "View Advanced Properties", "tune" );
			o.Checkable = true;
			o.Checked = Sheet.ShowAdvanced;
			o.Toggled = b => Sheet.ShowAdvanced = b;
		}
		menu.AddSeparator();
	}

	string GetTooltip()
	{
		var str = "<strong>";
		str += $"<span style=\"color: #9CDCFE;\">{TargetObject.TypeTitle}</span>";

		if ( TargetType?.BaseType is TypeDescription baseType &&
			 baseType.TargetType.IsSubclassOf( typeof( Component ) ) )
		{
			str += $" - ";
			str += $"<span style=\"color: #B8D7A3;\">{baseType.TargetType.ToRichText()}</span>";
		}

		str += "</strong>";

		var description = TargetType?.Description;
		if ( string.IsNullOrWhiteSpace( description ) )
		{
			description = TargetObject.ParentProperty?.Description;
		}

		if ( !string.IsNullOrWhiteSpace( description ) )
		{
			str += $"<br/><br/><font>{description}</font>";
		}

		return str;
	}
}

file sealed class ComponentHeaderDrag : Drag
{
	ComponentSheetHeader Header;

	public ComponentHeaderDrag( ComponentSheetHeader header )
		: base( header )
	{
		Header = header;
	}

	public override void OnDestroyed()
	{
		if ( Header.IsValid() )
		{
			Header.Update();
		}

		base.OnDestroyed();
	}
}

file sealed class ComponentEnabledWidget : BoolControlWidget
{
	public ComponentEnabledWidget( SerializedProperty property )
		: base( property )
	{
		ToolTip = "Enabled";
		FixedWidth = 18;
		FixedHeight = 18;

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
