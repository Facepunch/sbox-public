using System;
using System.Linq;

namespace Editor;

/// <summary>
/// Picks one of the project's sprite sort layers. Offers a way through to the layer editor
/// inline, so needing a new layer doesn't mean going hunting through project settings.
/// </summary>
[CustomEditor( typeof( SortLayerHandle ), NamedEditor = "sortlayer" )]
public class SortLayerControlWidget : ControlWidget
{
	/// <summary>
	/// Picking a layer writes that one layer to every selected object, which is what multi-editing
	/// this should mean. Without this the whole row is replaced by "Multi Edit Not Supported" and a
	/// selection cannot be given a layer at all.
	/// </summary>
	public override bool SupportsMultiEdit => true;

	/// <summary>
	/// Set while the combo box is being filled in. Adding an entry with <c>selected</c> assigns
	/// CurrentIndex, and that reaches the item's action whether or not anybody picked it - so
	/// without this a rebuild can write a layer to the whole selection on its own. On a
	/// multi-selection that would quietly copy the first object's layer over all the others, which
	/// is the same way the sprite texture used to get aliased.
	/// </summary>
	bool _building;

	public SortLayerControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Column();
		Layout.Spacing = 2;
		AcceptDrops = false;

		Rebuild();
	}

	void Rebuild()
	{
		_building = true;

		try
		{
			RebuildLayout();
		}
		finally
		{
			_building = false;
		}
	}

	void RebuildLayout()
	{
		Layout.Clear( true );

		var settings = ProjectSettings.Sorting;

		// GetValue only reports the first selected object, so it only describes the selection when
		// they all agree.
		var mixed = SerializedProperty.IsMultipleDifferentValues;
		var current = SerializedProperty.GetValue<SortLayerHandle>();

		var comboBox = new ComboBox( this );

		if ( mixed )
		{
			// Nothing else is pre-selected in this case, and an empty selection makes the combo
			// settle on the first layer - which would read as though everything already shared it.
			comboBox.AddItem( "Multiple Values", onSelected: () => { }, selected: true );
		}

		foreach ( var layer in settings.Layers )
		{
			// Captured per iteration so each entry writes back its own id.
			var id = layer.Id;

			comboBox.AddItem( layer.Name,
				onSelected: () => SetLayer( id ),
				selected: !mixed && (current.Id == id || (current.Id == Guid.Empty && layer == settings.DefaultLayer)) );
		}

		comboBox.AddItem( "Edit Layers…", "tune", onSelected: OpenLayerEditor );

		Layout.Add( comboBox );

		AddUnsortedWarning();
		AddGroupWarnings();
	}

	void SetLayer( Guid id )
	{
		if ( _building ) return;

		SerializedProperty.SetValue( new SortLayerHandle( id ) );
	}

	/// <summary>
	/// The two ways a sorting group silently does nothing: it is nested inside another one, or its
	/// sprites never opted into sorting. Both look identical from the viewport - the group is
	/// simply ignored - so neither is discoverable without being told.
	/// </summary>
	void AddGroupWarnings()
	{
		// Every selected group, not just the first. These warn about a group silently doing nothing,
		// so one bad group in a selection is still worth saying out loud.
		var groups = SerializedProperty.Parent?.Targets.OfType<SortingGroup>().ToList();
		if ( groups is not { Count: > 0 } ) return;

		var nested = groups.Count( x => x.IsNested );
		var empty = groups.Count( x => x.MemberCount == 0 );

		if ( nested > 0 )
		{
			Layout.Add( new Label( groups.Count > 1
				? $"{Describe( nested, groups.Count )} are inside another Sorting Group - the inner group wins, and the outer one will not see those sprites."
				: "Inside another Sorting Group - the inner group wins, and this one will not see these sprites." )
			{
				Color = Theme.Yellow,
				WordWrap = true
			} );
		}

		if ( empty > 0 )
		{
			Layout.Add( new Label( groups.Count > 1
				? $"{Describe( empty, groups.Count )} have no Sprite Renderers beneath them, so they have nothing to order."
				: "No Sprite Renderers beneath this object, so this group has nothing to order." )
			{
				Color = Theme.Yellow,
				WordWrap = true
			} );
		}
	}

	/// <summary>
	/// Names how much of a selection a warning applies to, so it is clear whether it is about all of
	/// them or only some.
	/// </summary>
	static string Describe( int matching, int total )
		=> matching == total ? $"All {total} selected groups" : $"{matching} of {total} selected groups";

	/// <summary>
	/// Sorting does nothing at all while Is Sorted is off, and it is off by default. Without this,
	/// the most likely first experience of the feature is picking a layer and seeing no change.
	/// </summary>
	void AddUnsortedWarning()
	{
		if ( !TryGetIsSorted( out var isSorted ) || isSorted ) return;

		// EnableSorting writes to every selected object, so the button has to say that rather than
		// claim it only touches one of them.
		var total = SerializedProperty.MultipleProperties.Count();
		var many = total > 1;

		var fix = new Button.Primary( many ? $"Enable sorting on all {total} sprites" : "Enable sorting on this sprite", "sort" );
		fix.ToolTip = "Sort layers and sort order are ignored until Is Sorted is enabled.";
		fix.Clicked = EnableSorting;

		Layout.Add( new Label( many
			? "Is Sorted is off on at least one of these - the layer is being ignored there."
			: "Is Sorted is off - this layer is being ignored." )
		{
			Color = Theme.Yellow,
			WordWrap = true
		} );

		Layout.Add( fix );
	}

	bool TryGetIsSorted( out bool value )
	{
		value = false;

		if ( SerializedProperty.Parent is not { } parent ) return false;
		if ( !parent.TryGetProperty( "IsSorted", out var property ) ) return false;

		// With several sprites selected, GetValue only reports the first one. The warning is about
		// sorting silently doing nothing, so it has to appear if *any* of them has it switched off.
		if ( property.IsMultipleValues )
		{
			value = property.MultipleProperties.All( p => p.GetValue<bool>() );
			return true;
		}

		value = property.GetValue<bool>();
		return true;
	}

	void EnableSorting()
	{
		if ( SerializedProperty.Parent is not { } parent ) return;
		if ( !parent.TryGetProperty( "IsSorted", out var property ) ) return;

		property.SetValue( true );

		Rebuild();
	}

	static void OpenLayerEditor()
	{
		var project = Project.Current;
		if ( project is null ) return;

		ProjectSettingsWindow.OpenForProject( project );
	}

	protected override int ValueHash
	{
		get
		{
			var hc = new HashCode();
			hc.Add( base.ValueHash );

			// Rebuild when layers are added, removed, renamed or reordered.
			hc.Add( ProjectSettings.Sorting.GetHashCode() );

			// Rebuild when Is Sorted is toggled, so the warning appears and clears with it.
			hc.Add( TryGetIsSorted( out var isSorted ) && isSorted );

			return hc.ToHashCode();
		}
	}

	protected override void OnValueChanged()
	{
		Rebuild();
	}

	protected override void OnPaint()
	{
		// Combo box paints itself
	}
}
