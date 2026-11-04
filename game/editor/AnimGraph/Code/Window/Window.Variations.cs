namespace Editor.AnimGraph;

public partial class Window
{
	/// <summary>
	/// The variation currently being edited - decides which data the nodes show and which
	/// variation the preview compiles.
	/// </summary>
	internal string SelectedVariationID => _graph?.SelectedVariationID ?? Variation.DefaultVariationID;

	internal VariationHierarchy VariationHierarchy => _graph?.Variations;

	internal void SetSelectedVariation( string variationID )
	{
		if ( _graph is null || _graph.SelectedVariationID == variationID )
			return;

		_graph.SelectedVariationID = variationID;

		// Each variation compiled into its own definition, so this usually just plays another one
		var swapped = TryApplyPreviewVariation( variationID );

		// Nothing compiled for this variation yet, so play nothing until there is
		if ( !swapped )
		{
			SetPreviewGraph( null );
			_previewCompileStale = true;
		}

		// Nodes, clips and the preview all read the selected variation's data
		_preview?.RebuildModel();
		_clipList?.Rebuild();
		_variationEditor?.Rebuild();
		_properties?.Refresh();
		UpdateVariationOption();
		(_pageCanvas as INodePage)?.OnGraphEdited();
		_graphView?.Update();

		if ( !swapped && _autoCompilePreviewEnabled )
			CompilePreview();
	}

	internal void RebuildPreviewModel() => _preview?.RebuildModel();

	/// <summary>
	/// Set the model a variation animates. Its preview body groups reset with it, since they index
	/// into the old model's parts.
	/// </summary>
	internal void SetVariationModel( string variationID, Model model )
	{
		if ( _graph is null || VariationHierarchy?.GetVariation( variationID ) is not { } variation )
			return;

		if ( variation.TargetModel == model )
			return;

		variation.TargetModel = model;

		CommitChange( $"Set {variationID} Model" );

		if ( SelectedVariationID == variationID )
		{
			RebuildPreviewModel();
			_clipList?.Rebuild();
		}
	}

	/// <summary>
	/// Pick the model a variation animates.
	/// </summary>
	internal void PromptSetVariationModel( string variationID )
	{
		if ( VariationHierarchy?.GetVariation( variationID ) is not { } variation )
			return;

		var picker = AssetPicker.Create( null, AssetType.Model );
		picker.Title = $"Select the model '{variationID}' animates";
		picker.OnAssetPicked = assets =>
		{
			if ( assets.FirstOrDefault() is { } asset )
				SetVariationModel( variationID, asset.LoadResource<Model>() );
		};
		picker.Show();

		if ( variation.TargetModel?.Name is { Length: > 0 } path )
			picker.SetSelection( AssetSystem.FindByPath( path ) );
	}

	internal void CreateVariation( string variationID, string parentVariationID )
	{
		if ( _graph is null || string.IsNullOrWhiteSpace( variationID ) )
			return;

		if ( _graph.Variations.IsValidVariation( variationID ) )
		{
			Log.Warning( $"AnimGraph: variation '{variationID}' already exists" );
			return;
		}

		_graph.Variations.CreateVariation( variationID, parentVariationID );
		CommitChange( $"Create Variation {variationID}" );

		// Switch to the new variation
		SetSelectedVariation( variationID );
	}

	internal void RenameVariation( string oldVariationID, string newVariationID )
	{
		if ( _graph is null || string.IsNullOrWhiteSpace( newVariationID ) )
			return;

		if ( oldVariationID == Variation.DefaultVariationID || oldVariationID == newVariationID )
			return;

		try
		{
			_graph.RenameVariation( oldVariationID, newVariationID );
		}
		catch ( InvalidOperationException exception )
		{
			Log.Warning( $"AnimGraph: {exception.Message}" );
			return;
		}

		CommitChange( $"Rename Variation {newVariationID}" );
	}

	internal void DeleteVariation( string variationID )
	{
		if ( _graph is null || variationID == Variation.DefaultVariationID )
			return;

		if ( !_graph.Variations.IsValidVariation( variationID ) )
			return;

		// Move off the selection before it's destroyed, so the preview and docks follow it back
		if ( SelectedVariationID == variationID
			|| _graph.Variations.GetDescendantVariations( variationID ).Contains( SelectedVariationID ) )
		{
			SetSelectedVariation( Variation.DefaultVariationID );
		}

		// Takes the children and the data nodes hold for them with it
		_graph.RemoveVariation( variationID );
		CommitChange( $"Delete Variation {variationID}" );
	}

	internal void SetVariationOverride( VariationDataNode node, string variationID, bool hasOverride )
	{
		if ( node is null || variationID == Variation.DefaultVariationID )
			return;

		if ( hasOverride )
			node.CreateVariationOverride( variationID );
		else
			node.RemoveVariationOverride( variationID );

		CommitChange( hasOverride ? "Add Variation Override" : "Remove Variation Override" );
		_properties?.Refresh();
		_graphView?.Update();
	}

	/// <summary>
	/// Picking a node in the variation editor targets it in the inspector, where its per variation
	/// data is edited.
	/// </summary>
	internal void SelectVariationNode( VariationDataNode node )
	{
		if ( node is null )
			return;

		_clipInfo = null;
		_properties.Target = node;
	}

	/// <summary>
	/// The variation tree, as a flat menu - clicking a variation selects it straight away, the
	/// indentation shows what inherits from what.
	/// </summary>
	internal void BuildVariationMenu( Menu menu )
	{
		if ( VariationHierarchy is null )
			return;

		AddVariationOptions( menu, Variation.DefaultVariationID, 0 );

		menu.AddSeparator();
		menu.AddOption( "New Variation...", "add", () => PromptCreateVariation( SelectedVariationID ) );
	}

	private void AddVariationOptions( Menu menu, string variationID, int depth )
	{
		var option = menu.AddOption( new string( ' ', depth * 4 ) + variationID, null, () => SetSelectedVariation( variationID ) );
		option.Checkable = true;
		option.Checked = SelectedVariationID == variationID;

		foreach ( var child in VariationHierarchy.GetChildVariations( variationID ) )
			AddVariationOptions( menu, child, depth + 1 );
	}

	/// <summary>
	/// The right-click menu of a variation in the variation editor.
	/// </summary>
	internal void BuildVariationContextMenu( Menu menu, string variationID )
	{
		if ( SelectedVariationID != variationID )
			menu.AddOption( "Edit This Variation", "edit_note", () => SetSelectedVariation( variationID ) );

		menu.AddOption( "Create Child Variation", "add", () => PromptCreateVariation( variationID ) );

		menu.AddSeparator();
		menu.AddOption( "Set Model...", "view_in_ar", () => PromptSetVariationModel( variationID ) );

		var clearModel = menu.AddOption( "Clear Model", "backspace", () => SetVariationModel( variationID, null ) );
		clearModel.Enabled = VariationHierarchy?.GetVariation( variationID )?.TargetModel is not null;

		if ( variationID == Variation.DefaultVariationID )
			return;

		menu.AddSeparator();
		menu.AddOption( "Rename", "edit", () => PromptRenameVariation( variationID ) );
		menu.AddOption( "Delete", "delete", () => PromptDeleteVariation( variationID ) );
	}

	/// <summary>
	/// How many nodes give this variation its own data.
	/// </summary>
	internal int CountVariationOverrides( string variationID )
	{
		if ( _graph is null || variationID == Variation.DefaultVariationID )
			return 0;

		return _graph.AllNodes.OfType<VariationDataNode>().Count( node => node.HasVariationOverride( variationID ) );
	}

	/// <summary>
	/// The variation a node actually gets its data from - itself when it overrides, otherwise the
	/// closest parent that does, falling back to the default variation.
	/// </summary>
	internal string GetVariationDataSource( VariationDataNode node, string variationID, bool ignoreSelf = false )
	{
		if ( node is null || VariationHierarchy is not { } hierarchy )
			return Variation.DefaultVariationID;

		var start = ignoreSelf ? hierarchy.GetParentVariationID( variationID ) : variationID;

		return hierarchy.ResolveDataSource( start, node.HasVariationOverride );
	}

	internal void PromptCreateVariation( string parentVariationID )
	{
		Dialog.AskString(
			name => CreateVariation( Variation.SanitizeID( name ), parentVariationID ),
			$"Name of the new variation, inheriting from '{parentVariationID}':",
			okay: "Create",
			initialName: GetUniqueVariationName( parentVariationID ),
			title: "Create Variation",
			minLength: 1
		);
	}

	internal void PromptRenameVariation( string variationID )
	{
		if ( variationID == Variation.DefaultVariationID )
			return;

		Dialog.AskString(
			name => RenameVariation( variationID, Variation.SanitizeID( name ) ),
			"Variation name:",
			okay: "Rename",
			initialName: variationID,
			title: "Rename Variation",
			minLength: 1
		);
	}

	internal void PromptDeleteVariation( string variationID )
	{
		if ( _graph is null || variationID == Variation.DefaultVariationID )
			return;

		var children = _graph.Variations.GetChildVariations( variationID ).Count;
		var overrides = CountVariationOverrides( variationID );

		var detail = new List<string>();
		if ( children > 0 )
			detail.Add( $"{children} child variation{(children == 1 ? "" : "s")}" );
		if ( overrides > 0 )
			detail.Add( $"{overrides} node override{(overrides == 1 ? "" : "s")}" );

		var message = detail.Count > 0
			? $"Delete '{variationID}'?\n\nThis also deletes {string.Join( " and ", detail )}."
			: $"Delete '{variationID}'?";

		var confirm = new PopupWindow( "Delete Variation", message, "Cancel",
			new Dictionary<string, Action>
			{
				{ "Delete", () => DeleteVariation( variationID ) }
			} );
		confirm.Show();
		confirm.Window.Position = ScreenRect.Center - confirm.Window.Size * 0.5f;
	}

	/// <summary>
	/// Suggests a name derived from the parent, numbered so it doesn't clash with an existing variation.
	/// </summary>
	private string GetUniqueVariationName( string parentVariationID )
	{
		var baseName = parentVariationID == Variation.DefaultVariationID || string.IsNullOrWhiteSpace( parentVariationID )
			? "Variation"
			: parentVariationID;

		if ( _graph is null )
			return baseName;

		for ( var i = 1; i <= _graph.Variations.All.Count + 1; i++ )
		{
			var name = $"{baseName}_{i}";
			if ( !_graph.Variations.IsVariationNameTaken( name ) )
				return name;
		}

		return baseName;
	}
}
