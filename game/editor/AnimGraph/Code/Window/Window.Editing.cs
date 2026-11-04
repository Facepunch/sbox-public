using Sandbox.Helpers;

namespace Editor.AnimGraph;

internal enum GraphChangeScope
{
	Runtime,
	EditorOnly
}

public partial class Window
{
	private bool _dirty;
	private string _selectedParameterName;
	private UndoSystem _undoSystem;
	private string _pendingUndoName;
	private GraphChangeScope _pendingChangeScope = GraphChangeScope.Runtime;
	private string _committedState;
	private StateMachineNode _selectedStateMachine;
	private StateMachineNode.State _selectedState;
	private string _selectedStateName;
	private object _primarySelectionContext;
	private object _primarySelectionItem;

	internal void PushGraphUndo( string name, GraphChangeScope changeScope = GraphChangeScope.Runtime )
	{
		_pendingUndoName = name;
		_pendingChangeScope = changeScope;
	}

	internal void PushGraphRedo() => CommitChange( _pendingUndoName, _pendingChangeScope );

	internal void CommitChange( string name, GraphChangeScope changeScope = GraphChangeScope.Runtime )
	{
		_pendingUndoName = null;
		_pendingChangeScope = GraphChangeScope.Runtime;

		var pre = _committedState;
		var post = _graph.Serialize();

		if ( pre is null || pre == post )
		{
			_committedState = post;
			return;
		}

		_committedState = post;
		_undoSystem.Insert( name ?? "Edit Graph", () => RestoreGraphState( pre, changeScope ), () => RestoreGraphState( post, changeScope ) );
		MarkDirty( changeScope == GraphChangeScope.Runtime );

		if ( changeScope == GraphChangeScope.Runtime )
			NotifyGraphEdited();
	}

	/// <summary>
	/// Tell docks the graph changed so they can refresh, instead of them polling every frame.
	/// </summary>
	private void NotifyGraphEdited()
	{
		_parameterReferences = null;
		_parameterList?.OnGraphEdited();
		_clipList?.OnGraphEdited();
		_variationEditor?.OnGraphEdited();
		UpdateVariationOption();
		(_pageCanvas as INodePage)?.OnGraphEdited();
	}

	private void RestoreGraphState( string state, GraphChangeScope changeScope )
	{
		var graph = new Data();
		graph.Deserialize( state );

		// The variation being edited isn't part of the serialized state, so it'd reset to the
		// default one on every undo - it falls back to default if the variation is gone
		graph.SelectedVariationID = _graph?.SelectedVariationID;

		var previous = (_graph?.Parameters ?? []).DistinctBy( p => p.Name ).ToDictionary( p => p.Name );
		for ( var i = 0; i < graph.Parameters.Count; i++ )
		{
			if ( previous.TryGetValue( graph.Parameters[i].Name, out var existing ) )
				graph.Parameters[i] = existing.CopyFrom( graph.Parameters[i] );
		}

		_session.ReplaceGraph( graph );
		_clipList?.SetGraph( graph );
		_committedState = state;
		_clipInfo = null;
		NotifyGraphEdited();

		if ( changeScope == GraphChangeScope.Runtime )
			_preview?.RebuildModel();

		var stale = _nav.FindIndex( page => ResolvePage( page ) is null );
		if ( stale > 0 )
			_nav.RemoveRange( stale, _nav.Count - stale );

		ShowPage();
		MarkDirty( changeScope == GraphChangeScope.Runtime );
	}

	[Shortcut( "editor.undo", "CTRL+Z" )]
	internal bool Undo() => _undoSystem.Undo();

	[Shortcut( "editor.redo", "CTRL+Y" )]
	internal bool Redo() => _undoSystem.Redo();

	[Shortcut( "editor.cut", "CTRL+X" )]
	private void CutSelection()
	{
		if ( FocusedNodePage?.CutSelection() == true )
			return;

		FocusedGraphView?.CutSelection();
	}

	[Shortcut( "editor.copy", "CTRL+C" )]
	private void CopySelection()
	{
		if ( FocusedNodePage?.CopySelection() == true )
			return;

		FocusedGraphView?.CopySelection();
	}

	[Shortcut( "editor.paste", "CTRL+V" )]
	private void PasteSelection()
	{
		if ( FocusedNodePage?.PasteSelection() == true )
			return;

		FocusedGraphView?.PasteSelection();
	}

	[Shortcut( "editor.duplicate", "CTRL+D" )]
	private void DuplicateSelection() => FocusedGraphView?.DuplicateSelection();

	[Shortcut( "editor.select-all", "CTRL+A" )]
	private void SelectAll() => FocusedGraphView?.SelectAll();

	[Shortcut( "editor.clear-selection", "ESC" )]
	private void ClearSelection() => FocusedGraphView?.ClearSelection();

	[Shortcut( "gameObject.frame", "F" )]
	private void CenterOnSelection() => FocusedGraphView?.CenterOnSelection();

	private INodePage FocusedNodePage => (_workspace?.SecondaryView.IsFocused ?? false) ? null : _pageCanvas as INodePage;

	private View FocusedGraphView => (_workspace?.SecondaryView.IsFocused ?? false) ? _workspace.SecondaryView
		: (_pageCanvas as INodePage)?.FocusedGraphView
		?? (_graphView.IsFocused || _graphView.Visible ? _graphView : null);

	internal void MarkDirty( bool updatePreview = true )
	{
		_dirty = true;
		UpdateTitle();

		if ( !updatePreview )
			return;

		_previewCompileStale = true;

		if ( _autoCompilePreviewEnabled && !_saving )
			CompilePreview();
	}

	private void UpdateTitle()
	{
		var name = _asset?.Path ?? "untitled";
		Title = $"Animgraph Editor - {name}{(_dirty ? "*" : "")}";
	}

	public void OnNodeSelected( View source, Node node )
	{
		if ( node is null && _clipInfo is not null )
			return;

		_clipInfo = null;

		if ( source != _workspace?.SecondaryView )
		{
			_primarySelectionContext = node;
			_primarySelectionItem = node;
			_selectedState = null;
			_selectedStateMachine = null;
			_workspace?.Show( node, node );
		}

		ApplyNodeSelection( node );
	}

	internal void OnCanvasSelection( object context, object item )
	{
		_clipInfo = null;
		_primarySelectionContext = context;
		_primarySelectionItem = item;
		_selectedStateMachine = context as StateMachineNode;
		_selectedState = item as StateMachineNode.State;
		_selectedStateName = _selectedState?.Name;
		_workspace?.Show( context, item );

		if ( _properties is not null )
			_properties.Target = item ?? context ?? (object)_graph;
	}

	internal SecondaryGraphInfo? ResolveSecondaryGraph( object context, object selected )
	{
		if ( selected is ParameterNode parameter && FindVirtualParameter( parameter ) is { } virtualParameter )
			return new( virtualParameter.Graph, virtualParameter.Name, "Virtual Parameter", "vibration", Theme.Yellow, $"parameter:{virtualParameter.Identifier}" );

		if ( selected is Node node && node.SecondaryGraph is { } graph )
		{
			var (title, icon) = SubGraphInfo( node, graph );
			return new( graph, node.DisplayInfo.Name, title, icon, node.PrimaryColor, $"node:{node.Identifier}" );
		}

		if ( context is Nodes.Blend2DNode blend && selected is Nodes.Blend2DNode.BlendPoint point )
		{
			var index = blend.Points.IndexOf( point );
			return new( point.Graph, blend.PointTitle( index ), "Blend Point", "grain", blend.PrimaryColor, $"{blend.Identifier}:point:{index}" );
		}

		if ( context is Nodes.Blend1DNode blend1D && selected is Nodes.Blend1DNode.BlendPoint point1D )
		{
			var index = blend1D.Points.IndexOf( point1D );
			return new( point1D.Graph, blend1D.PointTitle( index ), "Blend Point", "linear_scale", blend1D.PrimaryColor, $"{blend1D.Identifier}:point:{index}" );
		}

		if ( context is not StateMachineNode machine )
			return null;

		return selected switch
		{
			StateMachineNode.State state => new( state.LayerGraph, state.Name, "State Layer Data", "layers", Theme.Red, $"{machine.Identifier}:state:{state.Name}" ),
			StateMachineNode.Conduit conduit => new( conduit.Graph, $"{conduit.From} → {conduit.To}", "Transition", "route", Theme.Pink, $"{machine.Identifier}:transition:{conduit.From}:{conduit.To}" ),
			StateMachineCanvas.SharedSelection shared => new( shared.GetGraph( machine ), shared.Title, "State Machine", shared.Icon, Theme.Pink, $"{machine.Identifier}:{shared.Key}" ),
			_ => null
		};
	}

	internal void RestorePrimarySelection()
	{
		if ( ReferenceEquals( _primarySelectionContext, _primarySelectionItem )
			&& _primarySelectionItem is Node node )
		{
			ApplyNodeSelection( node );
			return;
		}

		_selectedStateMachine = _primarySelectionContext as StateMachineNode;
		_selectedState = _primarySelectionItem as StateMachineNode.State;
		_selectedStateName = _selectedState?.Name;
		_properties.Target = _primarySelectionItem ?? _primarySelectionContext ?? (object)_graph;
	}

	private void ApplyNodeSelection( Node node )
	{
		_selectedState = null;
		_selectedStateMachine = null;

		if ( node is AnimationClipNode clipNode && !string.IsNullOrWhiteSpace( clipNode.AnimationName ) )
			_properties.PlayAnimation( clipNode.AnimationName );
		else if ( node is AnimationPoseNode poseNode && !string.IsNullOrWhiteSpace( poseNode.AnimationName ) )
			_properties.ShowPose( poseNode );

		_properties.Target = node ?? (object)_graph;
	}

	public void OnClipSelected( string animName )
	{
		_properties.PlayAnimation( animName );

		if ( _clipInfo is null || _clipInfo.AnimationName != animName )
		{
			// The info reads from the clip resource, so a different clip needs a new one
			_clipInfo = new AnimationClipInfo( animName );
		}
		else
		{
			_properties.UpdateClipInfo();
		}

		_properties.Target = _clipInfo;
	}

	public void OnParameterSelected( IParameter parameter )
	{
		_clipInfo = null;
		_selectedParameterName = parameter.Name;
		_properties.Target = parameter;
	}

	internal bool IsSelected( object target ) => _properties?.Target == target;

	internal void ApplyPreviewParameters()
	{
		foreach ( var parameter in _graph?.Parameters ?? [] )
			SetPreviewParameter( parameter );
	}

	internal void SetPreviewParameter( Parameter parameter )
	{
		var renderer = _preview?.ModelRenderer;
		if ( renderer is null || string.IsNullOrWhiteSpace( parameter.Name ) )
			return;

		var parameters = renderer.Animation.Parameters;
		switch ( parameter.Type )
		{
			case AnimParameterType.Float: parameters.Set( parameter.Name, parameter.PreviewFloat, persist: true ); break;
			case AnimParameterType.Bool: parameters.Set( parameter.Name, parameter.PreviewBool, persist: true ); break;
			case AnimParameterType.ID: parameters.Set( parameter.Name, parameter.PreviewID ?? "", persist: true ); break;
			case AnimParameterType.Vector: parameters.Set( parameter.Name, parameter.PreviewVector, persist: true ); break;
			case AnimParameterType.Target:
				if ( !parameter.PreviewTargetIsSet
					|| (parameter.PreviewTargetIsBone && string.IsNullOrWhiteSpace( parameter.PreviewBone )) )
					parameters.UnsetTarget( parameter.Name, persist: true );
				else if ( parameter.PreviewTargetIsBone )
					parameters.SetTargetBone( parameter.Name, parameter.PreviewBone, persist: true );
				else
					parameters.Set( parameter.Name, new Transform( parameter.PreviewPosition, parameter.PreviewRotation ), persist: true );
				break;
		}
	}

	private void OnPropertyUpdated( SerializedProperty property )
	{
		_parameterReferences = null;
		var target = _properties.Target;

		if ( target is IParameter parameter )
		{
			var oldName = _selectedParameterName ?? parameter.Name;
			if ( oldName != parameter.Name || parameter is Parameter && ParameterList.AffectsIDSelectors( property ) )
			{
				_graph.UpdateParameterReferences( parameter, oldName,
					ParameterList.ResetsIDSelectorOptions( property ) );
			}

			if ( _selectedParameterName is not null && _selectedParameterName != parameter.Name )
			{
				_selectedParameterName = parameter.Name;

				if ( parameter is VirtualParameter virtualParameter )
					OnVirtualParameterRenamed( virtualParameter );
			}

			if ( parameter is Parameter control )
			{
				SetPreviewParameter( control );
			}

			_parameterList?.OnGraphEdited();
		}

		if ( target is StateMachineNode.State state && state == _selectedState
			&& _selectedStateMachine is { } machine && _selectedStateName != state.Name )
		{
			machine.RenameState( _selectedStateName, state.Name );
			_selectedStateName = state.Name;
		}

		var editedNode = target as Node ?? (target == _primarySelectionItem ? _primarySelectionContext as Node : null);
		editedNode?.Update();

		if ( editedNode?.SecondaryGraph is { } secondaryGraph )
		{
			foreach ( var node in secondaryGraph.Nodes.OfType<Node>() )
				node.Update();
		}

		if ( editedNode is IParameterReference )
			_parameterList?.OnGraphEdited();

		var clipsChanged = target switch
		{
			AnimationClipNode or AnimationPoseNode => property?.Name is null or nameof( AnimationClipNode.AnimationName ) or VariationDataNode.SelectedDataProperty,
			Data => property?.Name is null or nameof( Data.TargetModel ) or nameof( Data.TargetSkeleton ),
			_ => false
		};

		if ( clipsChanged )
			_clipList?.OnGraphEdited();

		_variationEditor?.OnNodeEdited( editedNode );
		_pageCanvas?.Update();
		(_pageCanvas as INodePage)?.OnGraphEdited();

		// Dragging a value fires this every frame - the preview compiles once the edit finishes,
		// on the same signal that pushes the undo entry
		MarkDirty( updatePreview: false );
		_previewCompileStale = true;
	}
}
