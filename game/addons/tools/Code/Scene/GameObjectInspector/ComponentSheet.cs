using Sandbox;

namespace Editor;

public enum ComponentViewMode
{
	Default,
	Events
}

/// <summary>
/// The component sheet that is used to edit a component's properties in the GameObjectInspector
/// </summary>
public partial class ComponentSheet : Widget
{
	public ComponentSheetHeader Header { get; private set; }

	SerializedObject TargetObject;
	Layout Content;
	Guid GameObjectId;
	internal ComponentViewMode ViewMode;

	bool draggingComponentAbove;
	bool draggingComponentBelow;

	string ExpandedCookieString => $"expand.{GameObjectId}.{TargetObject.TypeName}";

	/// <summary>
	/// The user's local preference to having this component expanded or not.
	/// </summary>
	bool ExpandedCookie
	{
		get => ProjectCookie.Get( ExpandedCookieString, true );
		set
		{
			// Don't bother storing the cookie if it's an expanded component
			if ( value )
			{
				ProjectCookie.Remove( ExpandedCookieString );
			}
			else
			{
				ProjectCookie.Set( ExpandedCookieString, value );
			}
		}
	}

	/// <summary>
	/// Is this component currently expanded?
	/// </summary>
	internal bool Expanded { get; set; } = true;

	internal bool ShowAdvanced
	{
		get
		{
			return TargetObject.Targets.OfType<Component>()
				.Any( x => x.IsValid() && x.Flags.Contains( ComponentFlags.ShowAdvancedProperties ) );
		}
		set
		{
			foreach ( var component in TargetObject.Targets.OfType<Component>() )
			{
				if ( component.IsValid() )
				{
					component.Flags = component.Flags.WithFlag( ComponentFlags.ShowAdvancedProperties, value );
				}
			}
		}
	}

	/// <summary>
	/// Expands/shrinks the component in the component list.
	/// </summary>
	/// <param name="expanded"></param>
	internal void SetExpanded( bool expanded )
	{
		Expanded = expanded;
		RebuildContent();
		ExpandedCookie = expanded;
	}

	public ComponentSheet( Guid gameObjectId, SerializedObject target ) : base( null )
	{
		GameObjectId = gameObjectId;
		Name = "ComponentSheet";
		TargetObject = target;
		Layout = Layout.Column();
		Layout.Margin = new( 0, 1 );
		SetSizeMode( SizeMode.Flexible, SizeMode.CanShrink );

		ViewMode = ComponentViewMode.Default;

		// Check to see if we have a cookie to say if the component isn't expanded
		Expanded = ExpandedCookie;
		AcceptDrops = true;

		Header = new ComponentSheetHeader( TargetObject, this );
		Header.BuildUI();

		Layout.Add( Header );

		Content = Layout.AddColumn();
		Content.Margin = new Sandbox.UI.Margin( 0, 0, 0, 0 );
		Frame();
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		if ( !IsBeingDroppedOn ) return;

		var rect = LocalRect;

		if ( draggingComponentAbove )
		{
			Paint.SetPen( Theme.Blue.Lighten( 0.2f ), 2f, PenStyle.Dot );
			Paint.DrawLine( rect.TopLeft, rect.TopRight );
		}

		if ( draggingComponentBelow )
		{
			Paint.SetPen( Theme.Blue.Lighten( 0.2f ), 2f, PenStyle.Dot );
			Paint.DrawLine( rect.BottomLeft, rect.BottomRight );
		}
	}

	public override void OnDragHover( DragEvent ev )
	{
		base.OnDragHover( ev );

		if ( !TryDragComponent( ev, out _, out var moveDelta ) )
		{
			draggingComponentAbove = false;
			draggingComponentBelow = false;
			return;
		}

		draggingComponentAbove = moveDelta < 0;
		draggingComponentBelow = moveDelta > 0;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		base.OnDragDrop( ev );

		if ( !TryDragComponent( ev, out var component, out var moveDelta ) )
			return;

		using var scene = SceneEditorSession.Scope();

		using ( SceneEditorSession.Active.UndoScope( "Change Component Order" ).WithGameObjectChanges( component.GameObject, GameObjectUndoFlags.Components ).Push() )
		{
			component.Components.Move( component, moveDelta );
		}
	}

	/// <summary>
	/// We collect a list of conditional properties (properties that hide or show depending on the value of a property).
	/// If the value of any of these properties changes, we need to rebuild the content.
	/// </summary>
	List<Func<bool>> hideShowConditions = new();

	int lastHash;

	[EditorEvent.Frame]
	public void Frame()
	{
		var hash = BuildHashCode();

		if ( lastHash != hash )
		{
			lastHash = hash;
			RebuildContent();
		}
	}

	public int BuildHashCode()
	{
		HashCode hc = new HashCode();

		hc.Add( TargetObject );
		hc.Add( ViewMode );
		hc.Add( ShowAdvanced );

		foreach ( var condition in hideShowConditions )
		{
			hc.Add( condition() );
		}

		return hc.ToHashCode();
	}

	[EditorEvent.Hotload]
	public void RebuildContent()
	{
		using var _ = SuspendUpdates.For( this );

		Content.Clear( true );
		hideShowConditions.Clear();
		BuildInstanceContent();

		lastHash = BuildHashCode();
	}

	static int GetOrderValue( SerializedProperty prop )
	{
		if ( prop.TryGetAttribute<OrderAttribute>( out var order ) )
			return order.Value;

		return int.MaxValue;
	}

	void BuildInstanceContent()
	{
		if ( !Expanded ) return;

		using var sup = SuspendUpdates.For( this );

		if ( ViewMode == ComponentViewMode.Default )
		{
			var componentEditor = ComponentEditorWidget.Create( TargetObject );
			if ( componentEditor.IsValid() )
			{
				Content.Add( componentEditor );
				Header.ContextMenu += componentEditor.OnHeaderContextMenu;
				return;
			}
		}

		hideShowConditions.Clear();

		HashSet<string> handledGroups = new( StringComparer.OrdinalIgnoreCase );

		var cs = new ControlSheet();
		cs.IncludePropertyNames = true;

		Content.Add( cs );

		var showAdvanced = ShowAdvanced;
		cs.AddObject( TargetObject, ( o ) => FilterProperties( o, showAdvanced ) );
	}

	bool FilterProperties( SerializedProperty o, bool showAdvanced )
	{
		if ( o.PropertyType is null ) return false;

		//
		// We're only going to hide the base OnComponent stuff in the event tab now.
		// All other events can show inline in the property sheet, like they should.
		// We want to remove the OnComponent 
		//
		var hideInEventTab = o.PropertyType.IsAssignableTo( typeof( Delegate ) ) && o.Name.StartsWith( "OnComponent" );

		if ( ViewMode == ComponentViewMode.Events && !hideInEventTab ) return false;
		if ( ViewMode != ComponentViewMode.Events && hideInEventTab ) return false;

		if ( o.IsMethod ) return true;
		if ( o.HasAttribute<PropertyAttribute>() == false ) return false;
		if ( o.HasAttribute<AdvancedAttribute>() && showAdvanced == false ) return false;

		return true;
	}

	bool TryDragComponent( DragEvent ev, out Component component, out int delta )
	{
		delta = 0;
		component = ev.Data.OfType<Component>().FirstOrDefault();

		var myComponent = TargetObject.Targets.OfType<Component>().FirstOrDefault();

		if ( !component.IsValid() || !myComponent.IsValid() || myComponent == component )
		{
			return false;
		}

		var componentList = myComponent.Components;
		var components = componentList.GetAll().ToList();
		var myComponentIndex = components.IndexOf( myComponent );
		var draggedComponentIndex = components.IndexOf( component );

		if ( myComponentIndex == -1 || draggedComponentIndex == -1 )
		{
			return false;
		}

		delta = myComponentIndex - draggedComponentIndex;
		return true;
	}

}
