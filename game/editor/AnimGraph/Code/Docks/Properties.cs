namespace Editor.AnimGraph;

/// <summary>
/// Properties panel that shows editable properties for the selected node,
/// or the graph settings when nothing is selected.
/// </summary>
public class Properties : Widget
{
	private const string DocumentationExpandedCookie = "AnimGraph.DocumentationExpanded";
	private const string DebugExpandedCookie = "AnimGraph.DebugExpanded";

	private readonly Window _window;
	private readonly Layout _editor;
	private object _target;
	private ScrollArea _scroller;
	private Widget _sheetCanvas;
	private readonly Widget _sheetPanel;
	private readonly PropertiesPreview _miniPreview;
	private Widget _debugBody;

	internal Node LiveDebugNode => _debugBody?.Visible == true ? _target as Node : null;

	private const float PreviewHeight = 250;

	private string _currentAnim;
	private Nodes.AnimationPoseNode _poseNode;

	public Action<SerializedProperty> PropertyUpdated { get; set; }
	public Action TargetChanged { get; set; }

	public object Target
	{
		get => _target;
		set
		{
			if ( value == _target )
				return;

			_target = value;
			Rebuild();
			TargetChanged?.Invoke();
		}
	}

	private PropertiesHeader _header;

	public Properties( Window window ) : base( window )
	{
		_window = window;

		Name = "Properties";
		WindowTitle = "Properties";
		SetWindowIcon( "edit" );
		MinimumWidth = 400;

		Layout = Layout.Column();
		Layout.Margin = 0;

		_header = Layout.Add( new PropertiesHeader() );

		// Fixed-height clip preview between the header and the properties,
		// which scroll in the remaining dock space.
		_miniPreview = new PropertiesPreview( this, window.Graph );
		_miniPreview.FixedHeight = PreviewHeight;

		// Start with preview hidden
		_miniPreview.Visible = false;

		_sheetPanel = new Widget( this ) { Layout = Layout.Column() };
		_sheetPanel.Layout.Margin = 0;
		_sheetPanel.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.WidgetBackground );
			Paint.DrawRect( _sheetPanel.LocalRect, Theme.ControlRadius );
			return true;
		};

		_scroller = _sheetPanel.Layout.Add( CreateScroller( _sheetPanel ), 1 );
		_sheetCanvas = _scroller.Canvas;

		_editor = Layout.AddColumn( 1 );
		_editor.Add( _miniPreview );
		_editor.Add( _sheetPanel, 1 );
	}

	private void Rebuild()
	{
		bool wantsPreview = _target is AnimationClipInfo or Nodes.AnimationClipNode or Nodes.AnimationPoseNode;

		UpdateHeader();

		if ( _miniPreview is not null )
			_miniPreview.Visible = wantsPreview;

		SyncPreview();
		RebuildSheet();
	}

	/// <summary>
	/// Rebuild the sheet in place, for when the values behind it changed - a variation swap, say.
	/// </summary>
	internal void Refresh() => Rebuild();

	internal void NavigationContextChanged()
	{
		if ( _target is null )
			return;

		UpdateHeader();
		RebuildSheet();
	}

	/// <summary>
	/// Play a specific animation in the mini preview.
	/// </summary>
	public void PlayAnimation( string name )
	{
		_currentAnim = name;
		_poseNode = null;

		_miniPreview.SetAnimation( name );
	}

	/// <summary>
	/// Show an animation held at a pose node's fixed time.
	/// </summary>
	public void ShowPose( Nodes.AnimationPoseNode pose )
	{
		PlayAnimation( pose.AnimationName );
		_poseNode = pose;
		_miniPreview.Pose = pose;
	}

	/// <summary>
	/// Keep the mini preview on whatever the inspector is showing - the clip of the variation being
	/// edited, which changes when the target, its data or the selected variation changes.
	/// </summary>
	private void SyncPreview()
	{
		_miniPreview.Graph = _window.Graph;

		switch ( _target )
		{
			case AnimationClipInfo clip when clip.AnimationName != _currentAnim || _poseNode is not null:
				PlayAnimation( clip.AnimationName );
				break;
			case Nodes.AnimationClipNode clip when clip.AnimationName != _currentAnim || _poseNode is not null:
				PlayAnimation( clip.AnimationName );
				break;
			case Nodes.AnimationPoseNode pose when pose.AnimationName != _currentAnim || _poseNode != pose:
				ShowPose( pose );
				break;
		}
	}

	/// <summary>
	/// Update header and sheet without rebuilding the layout (avoids flicker).
	/// </summary>
	public void UpdateClipInfo()
	{
		UpdateHeader();
	}

	private static ScrollArea CreateScroller( Widget parent )
	{
		var scroller = new ScrollArea( parent );
		scroller.VerticalScrollbarMode = ScrollbarMode.Auto;
		scroller.HorizontalScrollbarMode = ScrollbarMode.Off;
		scroller.TranslucentBackground = true;
		scroller.NoSystemBackground = true;
		// Scope to the scroll area itself, otherwise the rules cascade into every
		// descendant - including the QToolTip popups shown for the rows inside it.
		scroller.Name = "AnimGraphPropertiesScroll";
		scroller.SetStyles( "#AnimGraphPropertiesScroll { border: none; background: transparent; }" );
		scroller.Canvas = CreateSheetCanvas( scroller );
		return scroller;
	}

	private static Widget CreateSheetCanvas( Widget parent )
	{
		return new Widget( parent )
		{
			Layout = Layout.Column(),
			HorizontalSizeMode = SizeMode.Flexible,
			VerticalSizeMode = SizeMode.CanGrow,
			TranslucentBackground = true,
			NoSystemBackground = true,
		};
	}

	private void RebuildSheet()
	{
		if ( _scroller is null || _target is null )
			return;

		var previousCanvas = _scroller.Canvas;
		var previousDebugBody = _debugBody;
		_debugBody = null;
		var nextCanvas = CreateSheetCanvas( _scroller );
		nextCanvas.Visible = false;
		var content = CreateSheetCanvas( nextCanvas );
		content.Layout.Margin = 4;
		nextCanvas.Layout.Add( content );
		_sheetCanvas = content;

		try
		{
			var so = _target.GetSerialized();
			so.OnPropertyChanged += property =>
			{
				UpdateHeader();
				SyncPreview();
				PropertyUpdated?.Invoke( property );
			};
			so.OnPropertyFinishEdit += p => _window.CommitChange( p is null ? "Edit Properties" : $"Edit {p.Name}" );

			var hideAnimName = _target is AnimationClipInfo;

			// Variation data is edited in the inspector, above the properties every variation shares
			var variationNode = _target as VariationDataNode;
			var variationID = _window.SelectedVariationID;
			var inherited = variationNode is not null
				&& variationID != Variation.DefaultVariationID
				&& !variationNode.HasVariationOverride( variationID );

			if ( variationNode is not null )
			{
				if ( variationID != Variation.DefaultVariationID )
					AddVariationBanner( variationNode, variationID, inherited );

				AddVariationData( so, inherited );
			}

			var sheet = new ControlSheet();
			sheet.AddObject( so, p => FilterProperty( p, hideAnimName )
				&& !(variationNode is not null && p.Name == VariationDataNode.SelectedDataProperty) );

			if ( _target is Nodes.StateMachineNode.Conduit conduit )
				AddConduitRow( conduit );

			_sheetCanvas.Layout.Add( sheet );

			if ( _target is Nodes.StateMachineNode.State selectedState && _window.SelectedStateMachine is { } machine )
				AddTransitionPriority( machine, selectedState );

			switch ( _target )
			{
				case Nodes.StateMachineNode.State { Type: not Nodes.StateMachineNode.StateType.Off } state:
					AddConvertButton( state );
					break;
			}

			nextCanvas.Layout.AddStretchCell();

			_sheetCanvas = nextCanvas;

			if ( _target is Node debugNode )
				AddDebugSection( debugNode );

			AddDocumentation( _target );
		}
		catch
		{
			_sheetCanvas = previousCanvas;
			_debugBody = previousDebugBody;
			nextCanvas.Destroy();
			throw;
		}

		_scroller.UpdatesEnabled = false;
		try
		{
			nextCanvas.Visible = true;
			_scroller.Canvas = nextCanvas;
		}
		finally
		{
			_scroller.UpdatesEnabled = true;
			_scroller.Update();
		}
	}

	private void AddDocumentation( object target )
	{
		var type = target switch
		{
			Node node => node.GetType(),
			Nodes.StateMachineNode.State => typeof( Nodes.StateMachineNode.State ),
			Nodes.StateMachineNode.Conduit => typeof( Nodes.StateMachineNode.Conduit ),
			StateMachineCanvas.SharedSelection shared => shared.DocumentationType,
			_ => null
		};

		if ( type is null )
			return;

		var description = DisplayInfo.ForType( type ).Description;
		if ( string.IsNullOrWhiteSpace( description ) )
			return;

		AddCollapsibleSection( "Documentation", "help", Theme.Blue, DocumentationExpandedCookie, body =>
		{
			var label = new Label( description, body )
			{
				WordWrap = true,
				MinimumHeight = 1,
				VerticalSizeMode = SizeMode.CanGrow,
				Color = Theme.Text.WithAlpha( 0.75f )
			};

			body.Layout.Add( label );
		} );
	}

	private void AddDebugSection( Node node )
	{
		if ( !node.HasDebugContent )
			return;

		AddCollapsibleSection( "Debug", "bug_report", Theme.Green, DebugExpandedCookie, body =>
		{
			_debugBody = body;
			body.Layout.Spacing = 2;
			node.BuildDebugWidgets( body );
		} );
	}

	/// <summary>
	/// Adds a collapsible section (header + body) to the sheet, persisting its expanded
	/// state under <paramref name="cookie"/>. Body content is supplied by <paramref name="buildBody"/>.
	/// </summary>
	private void AddCollapsibleSection( string title, string icon, Color color, string cookie, Action<Widget> buildBody )
	{
		var group = new Widget( _sheetCanvas ) { Layout = Layout.Column() };
		var expanded = ProjectCookie.Get( cookie, false );
		var header = group.Layout.Add( new SectionHeader
		{
			Title = title,
			Icon = icon,
			Color = color,
			IsExpanded = expanded
		} );
		var body = group.Layout.Add( new Widget( group ) { Layout = Layout.Column(), Visible = expanded } );
		body.Layout.Margin = new Sandbox.UI.Margin( 12, 4, 12, 8 );

		header.ExpandedChanged = value =>
		{
			using var updates = SuspendUpdates.For( _sheetCanvas );

			if ( value )
				body.Show();
			else
				body.Hide();

			group.UpdateGeometry();
			_sheetCanvas.UpdateGeometry();
			ProjectCookie.Set( cookie, value );
		};
		header.BuildUI();

		buildBody( body );
		_sheetCanvas.Layout.Add( group );
	}

	private sealed class SectionHeader : InspectorHeader
	{
		public Action<bool> ExpandedChanged { get; set; }

		protected override void BuildRightIcons( Layout layout ) { }

		protected override void OnExpandChanged()
		{
			ExpandedChanged?.Invoke( IsExpanded );
		}
	}

	private void AddConduitRow( Nodes.StateMachineNode.Conduit conduit )
	{
		var count = conduit.Transitions.Count();
		var card = new Widget( _sheetCanvas ) { Layout = Layout.Column() };
		card.Layout.Margin = 12;
		card.Layout.Spacing = 6;
		card.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( card.LocalRect, Theme.ControlRadius );
			return true;
		};

		var heading = card.Layout.AddRow();
		heading.Spacing = 8;
		heading.Add( new Label.Header( "Transition Rules", card ), 1 );

		var status = heading.Add( new Label( count == 1 ? "1 rule" : $"{count} rules", card ) );
		status.Alignment = TextFlag.RightCenter;
		status.Color = count > 0 ? Theme.TextControl : Theme.Red;

		var description = count > 0
			? $"Controls when {conduit.From} can transition to {conduit.To}."
			: $"No rules are defined, so {conduit.From} cannot transition to {conduit.To}.";
		var details = card.Layout.Add( new Label.Body( description, card ) );
		details.Color = count > 0 ? Theme.TextControl : Theme.Red;

		_sheetCanvas.Layout.Add( card );
	}

	private void AddTransitionPriority( Nodes.StateMachineNode machine, Nodes.StateMachineNode.State state )
	{
		var routes = machine.GetOrderedTransitions( state );
		var card = new Widget( _sheetCanvas ) { Layout = Layout.Column() };
		card.Layout.Margin = 8;
		card.Layout.Spacing = 4;
		card.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( card.LocalRect, Theme.ControlRadius );
			return true;
		};

		var heading = card.Layout.AddRow();
		heading.Margin = new Sandbox.UI.Margin( 2, 0 );
		var title = heading.Add( new Label.Header( "Transition Priority", card ), 1 );
		title.Color = Theme.Text.WithAlpha( 0.88f );

		var count = heading.Add( new Label.Small( routes.Count == 1 ? "1 transition" : $"{routes.Count} transitions", card ) );
		count.Alignment = TextFlag.RightCenter;
		count.Color = Theme.TextControl;

		var description = card.Layout.Add( new Label.Small(
			routes.Count == 0 ? "This state has no outgoing transitions." : "The first transition with a true condition wins.",
			card ) );
		description.Indent = 2;
		description.Color = Theme.TextControl;

		var list = card.Layout.Add( new Widget( card ) { Layout = Layout.Column() } );
		list.Layout.Margin = new Sandbox.UI.Margin( 0, 2, 0, 0 );
		list.Layout.Spacing = 4;

		var rows = new List<TransitionPriorityRow>();

		void RefreshRows()
		{
			var ordered = machine.GetOrderedTransitions( state );
			for ( var i = 0; i < rows.Count; i++ )
				rows[i].Bind( ordered[i], i );
		}

		for ( var i = 0; i < routes.Count; i++ )
		{
			var index = i;
			var row = new TransitionPriorityRow(
				list,
				state,
				routes[index],
				index,
				( transition, newIndex ) =>
				{
					if ( MoveTransition( machine, state, transition, newIndex ) )
						RefreshRows();
				} );

			rows.Add( row );
			list.Layout.Add( row );
		}

		_sheetCanvas.Layout.Add( card );
	}

	private bool MoveTransition( Nodes.StateMachineNode machine, Nodes.StateMachineNode.State state, Nodes.TransitionNode transition, int newIndex )
	{
		if ( !machine.MoveTransition( state, transition, newIndex ) )
			return false;

		_window.PushGraphUndo( "Reorder Transitions" );
		machine.Update();
		_window.PushGraphRedo();
		return true;
	}

	private static string ConditionSummary( Nodes.TransitionNode transition )
		=> DescribeCondition( transition.GetConnectedNode( transition.Condition ), 0 );

	private static string DescribeCondition( Node node, int depth )
	{
		if ( node is null )
			return "No condition";

		if ( depth >= 3 )
			return node.DisplayInfo.Name ?? node.GetType().Name;

		return node switch
		{
			Nodes.BoolParameterNode parameter => string.IsNullOrWhiteSpace( parameter.ParameterName ) ? "Bool Parameter" : parameter.ParameterName,
			Nodes.ConstBoolNode constant => constant.Value ? "Always" : "Never",
			Nodes.NotNode not => $"not {DescribeInput( not, not.Value, depth )}",
			Nodes.AndNode and => and.Conditions.Count == 0
				? "Always"
				: string.Join( " and ", and.Conditions.Select( x => DescribeInput( and, x.Input, depth ) ) ),
			Nodes.OrNode or => $"{DescribeInput( or, or.A, depth )} or {DescribeInput( or, or.B, depth )}",
			Nodes.StateCompletedConditionNode completed => completed.AtClipEnd ? "At clip end" : "State completed",
			Nodes.TimeConditionNode time => TimeConditionSummary( time ),
			Nodes.FloatComparisonNode comparison => FloatComparisonSummary( comparison ),
			_ => node.DisplayInfo.Name ?? node.GetType().Name,
		};
	}

	private static string DescribeInput( Node owner, NodeInput input, int depth )
	{
		var source = owner.GetConnectedNode( input );
		var summary = DescribeCondition( source, depth + 1 );
		return source is Nodes.AndNode or Nodes.OrNode ? $"({summary})" : summary;
	}

	private static string FloatComparisonSummary( Nodes.FloatComparisonNode comparison )
	{
		var value = comparison.GetConnectedNode( comparison.Value )?.DisplayInfo.Name ?? "Value";
		var comparand = comparison.GetConnectedNode( comparison.Comparand )?.DisplayInfo.Name ?? comparison.ComparisonValue.ToString( "0.##" );
		var symbol = comparison.Comparison switch
		{
			GraphBuilder.FloatComparison.GreaterThanEqual => "≥",
			GraphBuilder.FloatComparison.LessThanEqual => "≤",
			GraphBuilder.FloatComparison.NearEqual => "≈",
			GraphBuilder.FloatComparison.GreaterThan => ">",
			GraphBuilder.FloatComparison.LessThan => "<",
			_ => "?",
		};

		return $"{value} {symbol} {comparand}";
	}

	private static string TimeConditionSummary( Nodes.TimeConditionNode time )
	{
		var value = time.GetConnectedNode( time.ValueInput )?.DisplayInfo.Name ?? time.Value.ToString( "0.##" );
		return $"{TimeConditionName( time.Type )} {ComparisonSymbol( time.Operator )} {value}";
	}

	private static string TimeConditionName( GraphBuilder.StateMachineBuilder.TimeComparisonType type ) => type switch
	{
		GraphBuilder.StateMachineBuilder.TimeComparisonType.PercentageThroughState => "State progress",
		GraphBuilder.StateMachineBuilder.TimeComparisonType.PercentageThroughSyncEvent => "Sync event progress",
		_ => "Elapsed time",
	};

	private static string ComparisonSymbol( GraphBuilder.StateMachineBuilder.TimeComparison comparison ) => comparison switch
	{
		GraphBuilder.StateMachineBuilder.TimeComparison.LessThan => "<",
		GraphBuilder.StateMachineBuilder.TimeComparison.LessThanEqual => "≤",
		GraphBuilder.StateMachineBuilder.TimeComparison.GreaterThan => ">",
		_ => "≥",
	};

	private sealed record TransitionPriorityDrag( Nodes.StateMachineNode.State State, Nodes.TransitionNode Transition, int Index );

	private sealed class TransitionPriorityRow : Widget
	{
		private readonly Nodes.StateMachineNode.State _state;
		private Nodes.StateMachineNode.TransitionRoute _route;
		private int _index;
		private string _conditionSummary;
		private readonly Action<Nodes.TransitionNode, int> _moved;
		private bool _dropAbove;
		private bool _dropBelow;

		public TransitionPriorityRow(
			Widget parent,
			Nodes.StateMachineNode.State state,
			Nodes.StateMachineNode.TransitionRoute route,
			int index,
			Action<Nodes.TransitionNode, int> moved ) : base( parent )
		{
			_state = state;
			_moved = moved;

			FixedHeight = 46;
			AcceptDrops = true;
			IsDraggable = true;
			Cursor = CursorShape.OpenHand;
			ToolTip = "Drag to change priority.";

			Bind( route, index );
		}

		public void Bind( Nodes.StateMachineNode.TransitionRoute route, int index )
		{
			_route = route;
			_index = index;
			_conditionSummary = ConditionSummary( route.Transition );
			ToolTip = $"Drag to change priority.\nCondition: {_conditionSummary}";
			Update();
		}

		protected override void OnPaint()
		{
			Paint.SetBrush( Paint.HasMouseOver ? Theme.WidgetBackground.Lighten( 0.15f ) : Theme.WidgetBackground );
			Paint.ClearPen();
			Paint.DrawRect( LocalRect, Theme.ControlRadius );

			var content = LocalRect.Shrink( 8, 0 );
			var handle = new Rect( content.Left, content.Top, 18, content.Height );
			Paint.SetPen( Theme.TextControl );
			Paint.DrawIcon( handle, "drag_indicator", 15, TextFlag.Center );

			var priority = new Rect( handle.Right + 5, content.Center.y - 10, 20, 20 );
			Paint.SetBrush( Theme.Primary.WithAlpha( 0.14f ) );
			Paint.ClearPen();
			Paint.DrawRect( priority, 10 );
			Paint.SetPen( Theme.Text.WithAlpha( 0.88f ) );
			Paint.SetDefaultFont( 7, 600 );
			Paint.DrawText( priority, (_index + 1).ToString(), TextFlag.Center );

			var right = content.Right;
			if ( _route.IsGlobal )
			{
				var badge = new Rect( right - 46, content.Top + 3, 46, 18 );
				Paint.SetBrush( Theme.Blue.WithAlpha( 0.14f ) );
				Paint.ClearPen();
				Paint.DrawRect( badge, 9 );
				Paint.SetPen( Theme.Blue.Lighten( 0.35f ).WithAlpha( 0.9f ) );
				Paint.DrawText( badge, "global", TextFlag.Center );
				right = badge.Left - 6;
			}

			var targetText = $"→ {_route.TargetState}";
			Paint.SetDefaultFont( 7, 500 );
			var targetWidth = Math.Clamp( Paint.MeasureText( targetText ).x + 4, 48, 120 );
			var target = new Rect( right - targetWidth, content.Top, targetWidth, 24 );
			Paint.SetPen( Theme.TextControl );
			Paint.DrawText( target, targetText, TextFlag.RightCenter );

			var name = string.IsNullOrWhiteSpace( _route.Transition.TransitionName ) ? "Transition" : _route.Transition.TransitionName;
			var nameRect = new Rect( priority.Right + 7, content.Top, Math.Max( target.Left - priority.Right - 13, 0 ), 24 );
			Paint.SetPen( Theme.Text.WithAlpha( 0.88f ) );
			Paint.SetDefaultFont( 8, 500 );
			Paint.DrawText( nameRect, name, TextFlag.LeftCenter );

			var summary = new Rect( priority.Right + 7, content.Top + 22, Math.Max( content.Right - priority.Right - 7, 0 ), 20 );
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.8f ) );
			Paint.SetDefaultFont( 7, 400 );
			Paint.DrawText( summary, _conditionSummary, TextFlag.LeftCenter | TextFlag.SingleLine );

			if ( _dropAbove || _dropBelow )
			{
				Paint.SetPen( Theme.TextHighlight, 2 );
				var y = _dropAbove ? LocalRect.Top : LocalRect.Bottom;
				Paint.DrawLine( new Vector2( LocalRect.Left, y ), new Vector2( LocalRect.Right, y ) );
			}
		}

		public override void OnDragHover( DragEvent e )
		{
			if ( e.Data.Object is not TransitionPriorityDrag drag
				|| !ReferenceEquals( drag.State, _state )
				|| ReferenceEquals( drag.Transition, _route.Transition ) )
			{
				e.Action = DropAction.Ignore;
				SetDropIndicator( false, false );
				return;
			}

			e.Action = DropAction.Move;
			SetDropIndicator( drag.Index > _index, drag.Index < _index );
		}

		public override void OnDragLeave()
		{
			SetDropIndicator( false, false );
		}

		public override void OnDragDrop( DragEvent e )
		{
			SetDropIndicator( false, false );

			if ( e.Data.Object is TransitionPriorityDrag drag && ReferenceEquals( drag.State, _state ) )
				_moved( drag.Transition, _index );
		}

		protected override void OnDragStart()
		{
			var drag = new Drag( this );
			drag.Data.Text = _route.Transition.TransitionName;
			drag.Data.Object = new TransitionPriorityDrag( _state, _route.Transition, _index );
			drag.Execute();
		}

		private void SetDropIndicator( bool above, bool below )
		{
			if ( _dropAbove == above && _dropBelow == below )
				return;

			_dropAbove = above;
			_dropBelow = below;
			Update();
		}
	}

	private void AddConvertButton( Nodes.StateMachineNode.State state )
	{
		if ( _window.StateMachineCanvas is not { } canvas )
			return;

		switch ( state.Type )
		{
			case Nodes.StateMachineNode.StateType.BlendTree when StateMachineCanvas.CanConvertToStateMachineState( state ):
				AddButtonRow( "Convert to State Machine State", "hub", () =>
				{
					canvas.ConvertStateType( state, Nodes.StateMachineNode.StateType.StateMachine );
					RebuildSheet();
				} );
				break;
			case Nodes.StateMachineNode.StateType.StateMachine:
				AddButtonRow( "Convert to Blend Tree State", "account_tree", () =>
				{
					canvas.ConvertStateType( state, Nodes.StateMachineNode.StateType.BlendTree );
					RebuildSheet();
				} );
				break;
		}
	}

	/// <summary>
	/// Explains where the data of a variation data node comes from, and lets you override or
	/// revert it without leaving the inspector.
	/// </summary>
	private void AddVariationBanner( VariationDataNode node, string variationID, bool inherited )
	{
		var source = _window.GetVariationDataSource( node, variationID );
		var tint = inherited ? Theme.TextControl.WithAlpha( 0.6f ) : Theme.Green;

		var banner = new Widget( _sheetCanvas ) { Layout = Layout.Row() };
		banner.FixedHeight = Theme.RowHeight;
		banner.Layout.Margin = new Sandbox.UI.Margin( 26, 0, 6, 0 );
		banner.Layout.Spacing = 4;
		banner.OnPaintOverride = () =>
		{
			Paint.Antialiasing = true;
			Paint.ClearPen();
			Paint.SetBrush( tint.WithAlpha( 0.07f ) );
			Paint.DrawRect( banner.LocalRect, Theme.ControlRadius );

			Paint.ClearBrush();
			Paint.SetPen( tint );
			Paint.DrawIcon( new Rect( 8, 0, 14, banner.LocalRect.Height ), inherited ? "link" : "edit_note", 13, TextFlag.Center );

			return true;
		};

		var label = banner.Layout.Add( new Label( inherited
			? $"Inherits from '{source}'"
			: $"Overrides for '{variationID}'" ), 1 );
		label.Color = tint;
		label.Alignment = TextFlag.LeftCenter;

		var button = banner.Layout.Add( new IconButton( inherited ? "edit_note" : "undo" ) );
		button.IconSize = 14;
		button.FixedSize = Theme.RowHeight - 2;
		button.Background = Color.Transparent;
		button.Foreground = inherited ? Theme.Green : Theme.TextControl.WithAlpha( 0.6f );
		button.ToolTip = inherited
			? $"Give '{variationID}' its own copy of this data so it can be edited"
			: $"Remove this override so '{variationID}' inherits its data again";
		button.OnClick = () => _window.SetVariationOverride( node, variationID, inherited );

		var row = _sheetCanvas.Layout.AddRow();
		row.Margin = new Sandbox.UI.Margin( 4, 2, 4, 2 );
		row.Add( banner, 1 );
	}

	/// <summary>
	/// The data of the variation being edited, always in the same place so overriding doesn't move
	/// anything around. Inherited data is read only - only the variation owning it can edit it.
	/// </summary>
	private void AddVariationData( SerializedObject so, bool inherited )
	{
		if ( !so.TryGetProperty( VariationDataNode.SelectedDataProperty, out var property ) )
			return;

		var host = new Widget( _sheetCanvas ) { Layout = Layout.Column(), Enabled = !inherited };

		var sheet = new ControlSheet();
		sheet.AddRow( property );
		host.Layout.Add( sheet );

		_sheetCanvas.Layout.Add( host );
	}

	private void AddButtonRow( string text, string icon, Action clicked )
	{
		var row = _sheetCanvas.Layout.AddRow();
		row.Margin = new Sandbox.UI.Margin( 12, 8, 12, 4 );

		var button = row.Add( new Button( text, icon, _sheetCanvas ), 1 );
		button.FixedHeight = Theme.RowHeight;
		button.Clicked = clicked;
	}

	private static bool FilterProperty( SerializedProperty property, bool hideAnimName )
	{
		if ( property.HasAttribute<HideAttribute>() ) return false;
		if ( !property.HasAttribute<PropertyAttribute>() ) return false;
		if ( hideAnimName && property.Name == "AnimationName" ) return false;
		return true;
	}

	private void UpdateHeader()
	{
		_header.Color = PropertiesHeader.DefaultIconColor;

		switch ( _target )
		{
			case AnimationClipInfo clip:
				SetHeader( "Animation Clip", clip.AnimationName );
				_header.Icon = "movie";
				_header.Color = Theme.Green;
				break;
			case Node node:
				var type = DisplayInfo.ForType( node.GetType() );
				SetHeader( type.Name ?? node.GetType().Name, node.DisplayInfo.Name );
				_header.Icon = node.DisplayInfo.Icon ?? type.Icon ?? "account_tree";
				_header.Color = node.PrimaryColor;
				break;
			case Parameter parameter:
				SetHeader( $"{AnimParameterUI.TypeName( parameter.Type )} Parameter", parameter.Name );
				_header.Icon = AnimParameterUI.Icon( parameter.Type );
				_header.Color = AnimParameterUI.Color( parameter.Type );
				break;
			case VirtualParameter parameter:
				SetHeader( $"Virtual {AnimParameterUI.TypeName( parameter.Type )} Parameter", parameter.Name );
				_header.Icon = AnimParameterUI.Icon( parameter.Type );
				_header.Color = AnimParameterUI.Color( parameter.Type );
				break;
			case Nodes.StateMachineNode.State state:
				SetHeader( "State", state.Name );
				_header.Icon = "animation";
				_header.Color = Theme.Red;
				break;
			case Nodes.StateMachineNode.Conduit conduit:
				SetHeader( "Conduit", $"{conduit.From} → {conduit.To}" );
				_header.Icon = "route";
				_header.Color = Theme.Pink;
				break;
			case StateMachineCanvas.SharedSelection shared:
				SetHeader( shared.Title );
				_header.Icon = shared.Icon;
				break;
			case Nodes.Blend2DNode.BlendPoint point:
				SetHeader( "Blend Point", point.Name );
				_header.Icon = "grain";
				_header.Color = Theme.Blue;
				break;
			case Nodes.Blend1DNode.BlendPoint point:
				SetHeader( "Blend Point", point.Name );
				_header.Icon = "linear_scale";
				_header.Color = Theme.Blue;
				break;
			default:
				SetHeader( "Graph Settings" );
				_header.Icon = "settings";
				break;
		}
		var navigation = _window.GetNavigationAction( _target );
		_header.SetOpenAction( navigation?.Text, "open_in_full", navigation?.Action );
		_header.Update();
	}

	/// <summary>
	/// Set the header to the selection's type name plus optional instance detail.
	/// </summary>
	private void SetHeader( string type, string detail = null )
	{
		_header.Text = type;
		_header.Detail = string.IsNullOrWhiteSpace( detail ) || detail == type ? null : detail;
	}

}

class PropertiesHeader : Widget
{
	public static readonly Color DefaultIconColor = global::Color.Parse( "#8E9199" ) ?? Theme.TextControl;

	public string Text { get; set; } = "Properties";
	public string Detail { get; set; }
	public string Icon { get; set; } = "settings";
	public Color Color { get; set; } = DefaultIconColor;

	private readonly IconButton _openButton;

	public PropertiesHeader() : base( null )
	{
		FixedHeight = Theme.RowHeight + 2;

		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( 0, 1, 4, 1 );
		Layout.AddStretchCell();

		_openButton = Layout.Add( new IconButton( "open_in_full", null, this )
		{
			Background = global::Color.Transparent,
			Foreground = Theme.TextControl,
			IconSize = 14,
			Visible = false
		} );
	}

	/// <summary>
	/// Show a right-anchored action button (e.g. Open Full Graph), or hide it when action is null.
	/// </summary>
	public void SetOpenAction( string tooltip, string icon, Action action )
	{
		_openButton.Visible = action is not null;
		_openButton.ToolTip = tooltip;
		_openButton.Icon = icon;
		_openButton.OnClick = action;
	}

	protected override void OnPaint()
	{
		var rect = LocalRect;

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground );
		Paint.DrawRect( rect );

		// Icon
		if ( !string.IsNullOrEmpty( Icon ) )
		{
			var iconRect = new Rect( rect.Left + 4, rect.Top, 22, rect.Height );
			Paint.SetPen( Color );
			Paint.DrawIcon( iconRect, Icon, 16, TextFlag.Center );
		}

		// Title: type tinted with the selection color, instance detail in plain text
		var textLeft = string.IsNullOrEmpty( Icon ) ? 4f : 30f;
		var textRight = _openButton.Visible ? _openButton.Position.x - 4 : rect.Width;
		var textRect = new Rect( textLeft, rect.Top, textRight - textLeft, rect.Height );
		Paint.SetHeadingFont( 11, 440, sizeInPixels: true );
		Paint.SetPen( Color == DefaultIconColor ? Theme.Text : Color );
		var drawn = Paint.DrawText( textRect, Text, TextFlag.LeftCenter );

		if ( !string.IsNullOrEmpty( Detail ) )
		{
			textRect.Left = drawn.Right + 6;
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.5f ) );
			drawn = Paint.DrawText( textRect, "·", TextFlag.LeftCenter );

			textRect.Left = drawn.Right + 6;
			Paint.SetPen( Theme.Text );
			Paint.DrawText( textRect, Detail, TextFlag.LeftCenter );
		}
	}
}
