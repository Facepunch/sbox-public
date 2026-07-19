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
	public SortLayerControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Column();
		Layout.Spacing = 2;
		AcceptDrops = false;

		Rebuild();
	}

	void Rebuild()
	{
		Layout.Clear( true );

		var settings = ProjectSettings.Sorting;
		var current = SerializedProperty.GetValue<SortLayerHandle>();

		var comboBox = new ComboBox( this );

		foreach ( var layer in settings.Layers )
		{
			// Captured per iteration so each entry writes back its own id.
			var id = layer.Id;

			comboBox.AddItem( layer.Name,
				onSelected: () => SerializedProperty.SetValue( new SortLayerHandle( id ) ),
				selected: current.Id == id || (current.Id == Guid.Empty && layer == settings.DefaultLayer) );
		}

		comboBox.AddItem( "Edit Layers…", "tune", onSelected: OpenLayerEditor );

		Layout.Add( comboBox );

		AddUnsortedWarning();
		AddGroupWarnings();
	}

	/// <summary>
	/// The two ways a sorting group silently does nothing: it is nested inside another one, or its
	/// sprites never opted into sorting. Both look identical from the viewport - the group is
	/// simply ignored - so neither is discoverable without being told.
	/// </summary>
	void AddGroupWarnings()
	{
		if ( SerializedProperty.Parent?.Targets.FirstOrDefault() is not SortingGroup group ) return;

		if ( group.IsNested )
		{
			Layout.Add( new Label( "Inside another Sorting Group - the inner group wins, and this one will not see these sprites." )
			{
				Color = Theme.Yellow,
				WordWrap = true
			} );
		}

		if ( group.MemberCount == 0 )
		{
			Layout.Add( new Label( "No Sprite Renderers beneath this object, so this group has nothing to order." )
			{
				Color = Theme.Yellow,
				WordWrap = true
			} );
		}
	}

	/// <summary>
	/// Sorting does nothing at all while Is Sorted is off, and it is off by default. Without this,
	/// the most likely first experience of the feature is picking a layer and seeing no change.
	/// </summary>
	void AddUnsortedWarning()
	{
		if ( !TryGetIsSorted( out var isSorted ) || isSorted ) return;

		var fix = new Button.Primary( "Enable sorting on this sprite", "sort" );
		fix.ToolTip = "Sort layers and sort order are ignored until Is Sorted is enabled.";
		fix.Clicked = EnableSorting;

		Layout.Add( new Label( "Is Sorted is off - this layer is being ignored." )
		{
			Color = Theme.Yellow
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
