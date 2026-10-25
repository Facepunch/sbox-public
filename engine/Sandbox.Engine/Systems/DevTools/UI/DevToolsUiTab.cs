using Sandbox.UI;
using System.Reflection;

namespace Sandbox.Diagnostics;

/// <summary>
/// The UI: an inspector - the panel tree, and the picked panel's box, the rules that matched it and where they're
/// from, and what it computed to, outlined over the game - and what each root costs to lay out, build and submit.
/// </summary>
internal sealed class DevToolsUiTab : DevToolsTab
{
	public override string TabId => "ui";
	public override string Title => "UI";
	public override bool FillsPage => true;

	/// <summary>
	/// The panel picked in the tree, outlined over the game while this tab shows.
	/// </summary>
	internal static Panel Selected { get; private set; }

	const int MaxTreeRows = 1500;

	readonly Label _inspectorButton, _performanceButton;
	readonly Panel _inspector, _performance;
	readonly Panel _treePane;
	readonly DevToolsTable _tree;
	readonly Panel _details;
	readonly Label _signature, _source;
	readonly DevToolsFacts _panelFacts;
	readonly Panel _boxModel;
	readonly Panel _rules;
	readonly TextEntry _computedFilter;
	readonly DevToolsTable _computed;
	readonly Label _nothing;
	readonly DevToolsFacts _facts;
	readonly DevToolsTable _roots;
	readonly HashSet<Panel> _expanded = new();
	bool _showPerformance;
	bool _revealSelected;
	Panel _detailsOf;
	int _detailsHash;

	public DevToolsUiTab()
	{
		AddClass( "ui-tab" );

		var toolbar = AddChild( new Panel( null, "net-toolbar" ) );
		var segments = toolbar.AddChild( new Panel( null, "segments" ) );
		_inspectorButton = segments.AddChild( new Label( "Inspector", "segment" ) );
		_inspectorButton.AddEventListener( "onclick", () => _showPerformance = false );
		_performanceButton = segments.AddChild( new Label( "Performance", "segment" ) );
		_performanceButton.AddEventListener( "onclick", () => _showPerformance = true );
		toolbar.AddChild( new Panel( null, "grow" ) );
		var deselect = toolbar.AddChild( new Label( "deselect", "icon-button" ) );
		deselect.Tooltip = "Clear the selection";
		deselect.AddEventListener( "onclick", () => Select( null ) );

		// Inspector: the tree on the left, what's picked on the right
		_inspector = AddChild( new Panel( null, "ui-inspector" ) );
		_treePane = _inspector.AddChild( new Panel( null, "ui-tree-pane" ) );
		_tree = _treePane.AddChild( new DevToolsTable( "minmax(0, 1fr) 40px", "Panels", "#kids" ).Banded() );

		_details = _inspector.AddChild( new Panel( null, "ui-details" ) );
		_nothing = _details.AddChild( new Label( "Pick a panel in the tree", "quiet" ) );
		var head = _details.AddChild( new Panel( null, "ui-head" ) );
		_signature = head.AddChild( new Label( "", "ui-signature" ) );
		_source = head.AddChild( new Label( "", "ui-source" ) );
		_panelFacts = _details.AddChild( new DevToolsFacts() );

		var box = _details.AddChild( new DevToolsSection( "Box" ) );
		_boxModel = box.Body.AddChild( new Panel( null, "box-model" ) );

		var rules = _details.AddChild( new DevToolsSection( "Matched rules" ) );
		rules.Aside.Text = "winning first";
		_rules = rules.Body;

		var computed = _details.AddChild( new DevToolsSection( "Computed" ) );
		_computedFilter = computed.Head.AddChild( new TextEntry() );
		_computedFilter.AddClass( "ui-filter" );
		_computedFilter.Placeholder = "Filter";
		_computed = computed.Body.AddChild( new DevToolsTable( "minmax(0, 0.8fr) minmax(0, 1fr)" ) );

		// Performance: what each root costs
		_performance = AddChild( new Panel( null, "net-view scrolls" ) );
		_facts = _performance.AddChild( new DevToolsFacts() );
		var roots = _performance.AddChild( new Panel( null, "section" ) );
		_roots = roots.AddChild( new DevToolsTable( "minmax(0, 1fr) 56px 48px 48px 48px 48px 40px", "Roots", "#panels", "#layout", "#build", "#submit", "#total", "#draws" ).Banded() );
	}

	public override void Tick()
	{
		base.Tick();

		_inspectorButton.SetClass( "active", !_showPerformance );
		_performanceButton.SetClass( "active", _showPerformance );
		_inspector.SetClass( "hidden", _showPerformance );
		_performance.SetClass( "hidden", !_showPerformance );

		if ( Selected is { IsValid: false } ) Select( null );
		if ( _revealSelected ) RevealSelected();
	}

	/// <summary>
	/// Scroll the tree to the picked panel's row, once it's laid out.
	/// </summary>
	void RevealSelected()
	{
		if ( _tree.Children.FirstOrDefault( x => x.HasClass( "selected" ) ) is not { } row )
		{
			_revealSelected = Selected is not null && _revealSelected;
			return;
		}

		if ( row.Box.Rect.Height <= 0 ) return;
		_revealSelected = false;

		var top = row.Box.Rect.Top - _treePane.Box.Rect.Top + _treePane.ScrollOffset.y;
		var view = _treePane.Box.Rect.Height;
		if ( top < _treePane.ScrollOffset.y || top + row.Box.Rect.Height > _treePane.ScrollOffset.y + view )
			_treePane.ScrollOffset = new Vector2( 0, MathF.Max( 0, top - view / 3 ) );
	}

	public override void Refresh( DevTools.Summary s )
	{
		if ( _showPerformance ) RefreshPerformance();
		else RefreshInspector();
	}

	void Select( Panel panel )
	{
		Selected = panel;
		_revealSelected = panel is not null;

		// Open the way down to it
		for ( var p = panel?.Parent; p is not null; p = p.Parent ) _expanded.Add( p );

		RefreshInspector();
	}

	//
	// Inspector
	//

	void RefreshInspector()
	{
		RefreshTree();
		RefreshDetails();
	}

	void RefreshTree()
	{
		var t = _tree;
		t.Begin();

		var rows = 0;
		foreach ( var (name, root) in RenderedRoots() )
		{
			// Roots start open
			if ( _seenRoots.Add( root ) ) _expanded.Add( root );

			AddNode( t, root, name, 0, ref rows );
		}

		t.End( "No UI rendered this frame" );
	}

	readonly HashSet<Panel> _seenRoots = new();

	void AddNode( DevToolsTable t, Panel panel, string label, int depth, ref int rows )
	{
		if ( rows++ >= MaxTreeRows ) return;

		var children = panel.ChildrenCount;
		var open = children > 0 && _expanded.Contains( panel );
		var arrow = children == 0 ? "  " : open ? "▾ " : "▸ ";
		var hidden = !panel.IsVisible;

		t.Row( $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( panel )}",
			[new( $"{arrow}{label ?? Signature( panel )}", hidden ? "ui-node dim" : "ui-node", Indent: 10 + depth * 12 ), new( children > 0 ? $"{children}" : "", "number dim" )],
			() =>
			{
				if ( Selected == panel && children > 0 )
				{
					if ( !_expanded.Remove( panel ) ) _expanded.Add( panel );
				}
				else if ( children > 0 ) _expanded.Add( panel );

				Select( panel );
			},
			tooltip: hidden ? "Not visible" : null,
			rowClass: Selected == panel ? "selected" : null );

		if ( !open ) return;

		foreach ( var child in panel.Children )
			AddNode( t, child, null, depth + 1, ref rows );
	}

	/// <summary>
	/// How a panel reads as a selector: <c>div#ok.button.primary</c>.
	/// </summary>
	internal static string Signature( Panel panel )
	{
		var element = string.IsNullOrEmpty( panel.ElementName ) ? panel.GetType().Name : panel.ElementName;
		var id = string.IsNullOrEmpty( panel.Id ) ? "" : $"#{panel.Id}";
		return $"{element}{id}{string.Concat( panel.Class.Select( x => $".{x}" ) )}";
	}

	void RefreshDetails()
	{
		var panel = Selected;
		_nothing.SetClass( "hidden", panel is not null );
		foreach ( var child in _details.Children )
			if ( child != _nothing ) child.SetClass( "hidden", panel is null );

		if ( panel is null ) return;

		_signature.Text = Signature( panel );
		_source.Text = panel.SourceFile is { Length: > 0 } file ? $"{System.IO.Path.GetFileName( file )}:{panel.SourceLine}" : panel.GetType().FullName;
		_source.Tooltip = panel.SourceFile;

		var scale = panel.ScaleToScreen > 0 ? panel.ScaleToScreen : 1f;
		var rect = panel.Box.Rect;
		_panelFacts.Set( "Size", $"{rect.Width / scale:0.#} x {rect.Height / scale:0.#}", tooltip: "Border box, in CSS pixels" );
		_panelFacts.Set( "Position", $"{rect.Left / scale:0.#}, {rect.Top / scale:0.#}" );
		_panelFacts.Set( "State", PseudoText( panel.PseudoClass ) );
		_panelFacts.Set( "Children", $"{panel.ChildrenCount}" );
		_panelFacts.Set( "Visible", panel.IsVisible ? "yes" : "no", panel.IsVisible ? null : DevTools.Severity.Warning );
		_panelFacts.Set( "Scale", $"{scale:0.##}" );

		// Rebuild the rules and box when something about them changed; the computed list refreshes in place
		var hash = HashCode.Combine( panel, panel.Style?.LastActiveRules?.Count ?? 0, panel.PseudoClass, string.Join( ' ', panel.Class ), rect );
		if ( panel != _detailsOf || hash != _detailsHash )
		{
			_detailsOf = panel;
			_detailsHash = hash;
			BuildBoxModel( panel, scale );
			BuildRules( panel );
		}

		RefreshComputed( panel );
	}

	static string PseudoText( PseudoClass pseudo )
	{
		if ( pseudo == PseudoClass.None ) return "-";
		return string.Join( " ", Enum.GetValues<PseudoClass>().Where( x => x != PseudoClass.None && pseudo.HasFlag( x ) ).Select( x => $":{x.ToString().ToLowerInvariant()}" ) );
	}

	/// <summary>
	/// Margin, border and padding around the content, each edge in CSS pixels, as a browser draws it.
	/// </summary>
	void BuildBoxModel( Panel panel, float scale )
	{
		_boxModel.DeleteChildren( true );

		var box = panel.Box;
		var content = box.RectInner;
		var inner = Layer( _boxModel, "margin", box.Margin, scale );
		inner = Layer( inner, "border", box.Border, scale );
		inner = Layer( inner, "padding", box.Padding, scale );
		inner.AddChild( new Label( $"{content.Width / scale:0.#} x {content.Height / scale:0.#}", "bm-content" ) );
	}

	static Panel Layer( Panel parent, string name, Margin edges, float scale )
	{
		var layer = parent.AddChild( new Panel( null, $"bm-layer bm-{name}" ) );
		var top = layer.AddChild( new Panel( null, "bm-row" ) );
		top.AddChild( new Label( name, "bm-name" ) );
		top.AddChild( new Label( Edge( edges.Top, scale ), "bm-edge" ) );
		top.AddChild( new Panel( null, "bm-name-spacer" ) );

		var middle = layer.AddChild( new Panel( null, "bm-middle" ) );
		middle.AddChild( new Label( Edge( edges.Left, scale ), "bm-edge side" ) );
		var inner = middle.AddChild( new Panel( null, "bm-inner" ) );
		middle.AddChild( new Label( Edge( edges.Right, scale ), "bm-edge side" ) );

		layer.AddChild( new Label( Edge( edges.Bottom, scale ), "bm-edge" ) );
		return inner;
	}

	static string Edge( float value, float scale ) => MathF.Abs( value ) < 0.01f ? "-" : $"{value / scale:0.#}";

	/// <summary>
	/// The rules that matched, the one that wins first, with what they set. A declaration a stronger rule - or the
	/// panel's own style - also sets is struck through.
	/// </summary>
	void BuildRules( Panel panel )
	{
		_rules.DeleteChildren( true );

		var set = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		var inline = StyleValues( panel.Style ).ToList();
		if ( inline.Count > 0 )
			Rule( "element.style", "set in code", inline, set );

		var rules = panel.Style?.LastActiveRules;
		if ( rules is not null )
		{
			for ( int i = rules.Count - 1; i >= 0; i-- )
			{
				var selector = rules[i];
				var block = selector.Block;
				if ( block is null ) continue;

				var source = block.FileName is { Length: > 0 } ? $"{System.IO.Path.GetFileName( block.FileName )}:{block.FileLine}" : "";
				Rule( selector.AsString ?? string.Join( ", ", block.SelectorStrings ), source, block.GetRawValues().Select( x => (x.Name, x.Value) ).ToList(), set, block.FileName );
			}
		}

		if ( _rules.ChildrenCount == 0 )
			_rules.AddChild( new Label( "No rules matched", "quiet" ) );
	}

	void Rule( string selector, string source, List<(string Name, string Value)> declarations, HashSet<string> set, string path = null )
	{
		var rule = _rules.AddChild( new Panel( null, "ui-rule" ) );
		var head = rule.AddChild( new Panel( null, "ui-rule-head" ) );
		head.AddChild( new Label( selector, "ui-selector" ) );
		var file = head.AddChild( new Label( source, "ui-rule-source" ) );
		file.Tooltip = path;

		foreach ( var (name, value) in declarations )
		{
			var overridden = !set.Add( name );
			var line = rule.AddChild( new Panel( null, overridden ? "ui-decl overridden" : "ui-decl" ) );
			line.AddChild( new Label( $"{name}:", "ui-prop" ) );
			line.AddChild( new Label( value, "ui-value" ) );
		}
	}

	void RefreshComputed( Panel panel )
	{
		var t = _computed;
		t.Begin();

		var filter = _computedFilter.Text;
		foreach ( var (name, value) in StyleValues( panel.ComputedStyle ) )
		{
			if ( !string.IsNullOrWhiteSpace( filter ) && !name.Contains( filter, StringComparison.OrdinalIgnoreCase ) && !value.Contains( filter, StringComparison.OrdinalIgnoreCase ) ) continue;
			t.Row( name, [new( name, "ui-prop" ), new( value, "ui-value" )], tooltip: $"{name}: {value}" );
		}

		t.End( "Nothing computed" );
	}

	/// <summary>
	/// Shorthands only repeat their longhands; Has* and Is* are questions about the style, not properties of it.
	/// </summary>
	static readonly string[] Shorthands = ["Padding", "Margin", "BorderWidth", "BorderColor", "Transitions"];

	static PropertyInfo[] _styleProperties;

	static PropertyInfo[] StyleProperties => _styleProperties ??= typeof( Styles ).GetProperties()
		.Where( p => p.GetMethod is { IsStatic: false } && p.GetIndexParameters().Length == 0 )
		.Where( p => !Shorthands.Contains( p.Name ) && !(p.PropertyType == typeof( bool ) && (p.Name.StartsWith( "Has" ) || p.Name.StartsWith( "Is" ))) )
		.OrderBy( p => CssName( p.Name ) )
		.ToArray();

	/// <summary>
	/// What a style sets, as css names and values.
	/// </summary>
	static IEnumerable<(string Name, string Value)> StyleValues( Styles style )
	{
		if ( style is null ) yield break;

		foreach ( var property in StyleProperties )
		{
			object value;
			try { value = property.GetValue( style ); }
			catch { continue; }

			if ( Format( value ) is { } text ) yield return (CssName( property.Name ), text);
		}
	}

	static string Format( object value )
	{
		if ( value is null ) return null;

		if ( value is PanelTransform transform )
			return transform.IsEmpty() ? null : string.Join( ", ", transform.List.Select( x => x.Type == PanelTransform.EntryType.Translate ? $"translate( {x.X}, {x.Y}, {x.Z} )" : $"{x.Type.ToString().ToLowerInvariant()}( {x.Data} )" ) );

		if ( value is System.Collections.IEnumerable list and not string )
		{
			var items = list.Cast<object>().Select( x => x?.ToString() ).ToArray();
			return items.Length == 0 ? null : string.Join( ", ", items );
		}

		return value.ToString();
	}

	/// <summary>
	/// MinWidth becomes min-width.
	/// </summary>
	static string CssName( string property )
	{
		var name = new System.Text.StringBuilder();
		foreach ( var c in property )
		{
			if ( char.IsUpper( c ) && name.Length > 0 ) name.Append( '-' );
			name.Append( char.ToLowerInvariant( c ) );
		}

		return name.ToString();
	}

	/// <summary>
	/// Outline the selected panel over the game, as a browser does: margin, border, padding and content. Only for panels
	/// drawn on the screen; a world panel's box isn't in screen space.
	/// </summary>
	internal static void DrawHighlight( Painter painter )
	{
		if ( Selected is not { IsValid: true } panel || !panel.IsVisible ) return;
		if ( panel.FindRootPanel() is { } root && root.IsWorldPanel ) return;

		var box = panel.Box;
		var outer = box.RectOuter;
		var border = box.Rect;
		var padding = Shrink( border, box.Border );
		var content = box.RectInner;

		painter.Stroke = Stroke.None;
		painter.Fill = new Color( 0.9f, 0.6f, 0.3f, 0.25f );
		painter.Rect( outer );
		painter.Fill = new Color( 0.95f, 0.85f, 0.45f, 0.30f );
		painter.Rect( border );
		painter.Fill = new Color( 0.55f, 0.75f, 0.45f, 0.30f );
		painter.Rect( padding );
		painter.Fill = new Color( 0.55f, 0.65f, 0.75f, 0.35f );
		painter.Rect( content );

		painter.Fill = Color.Transparent;
		painter.Stroke = Stroke.Solid( new Color( 1f, 1f, 1f, 0.7f ) );
		painter.Rect( border );
	}

	static Rect Shrink( Rect rect, Margin edges ) => new( rect.Left + edges.Left, rect.Top + edges.Top,
		MathF.Max( 0, rect.Width - edges.Left - edges.Right ), MathF.Max( 0, rect.Height - edges.Top - edges.Bottom ) );

	//
	// Performance
	//

	void RefreshPerformance()
	{
		var total = new RootPanel.RenderStatistics();
		var count = 0;
		var gpuBuffers = 0;

		var t = _roots;
		t.Begin();

		foreach ( var (name, root) in RenderedRoots() )
		{
			var r = root.Stats;
			count++;
			gpuBuffers += GpuBuffers( root );
			total = Sum( total, r );

			t.Row( $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( root )}", [name,
				DevToolsTable.Number( $"{r.PanelsRendered:N0}" ),
				Ms( r.LayoutTime ), Ms( r.CommandBuildTime ), Ms( r.SubmissionTime ),
				DevToolsTable.Number( DevToolsFormat.Ms( r.CpuTime.TotalMilliseconds ) ),
				new( $"{r.DrawCalls:N0}", "number dim" )], tooltip: $"{name}: {r.PanelsCulled:N0} subtrees culled, {r.Layers:N0} layers, {r.FrameGrabs:N0} frame grabs" );
		}

		t.End( "No UI rendered this frame" );

		_facts.Set( "Roots", $"{count}" );
		_facts.Set( "Panels", $"{total.PanelsRendered:N0}", tooltip: $"{total.PanelsCulled:N0} subtrees culled" );
		_facts.Set( "CPU", $"{total.CpuTime.TotalMilliseconds:0.00} ms" );
		_facts.Set( "Draw calls", $"{total.DrawCalls:N0}" );
		_facts.Set( "Instances", $"{total.Instances:N0}" );
		_facts.Set( "Layers", $"{total.Layers:N0}" );
		_facts.Set( "Frame grabs", $"{total.FrameGrabs:N0}", total.FrameGrabs > 0 ? DevTools.Severity.Info : null, "Backdrop filters and the like copy the screen" );
		_facts.Set( "GPU buffers", $"{gpuBuffers:N0}" );
	}

	/// <summary>
	/// The root panels that rendered last frame, most expensive first, game before menu. The game's UI and the menu's are
	/// separate systems; the dev tools' own isn't counted.
	/// </summary>
	internal static IEnumerable<(string Name, RootPanel Root)> RenderedRoots()
	{
		foreach ( var context in Contexts() )
		{
			var system = context.UISystem;
			if ( system is null ) continue;

			foreach ( var root in system.RootPanels.OrderByDescending( x => x.Stats.CpuTime ) )
			{
				if ( !root.IsValid ) continue;

				// Roots that have stopped rendering keep their last numbers
				var r = root.Stats;
				if ( r.RenderCount == 0 || r.FrameNumber + 1 < Application.FrameCount ) continue;

				yield return (context == Engine.GlobalContext.Menu ? $"{root.GetType().Name} (menu)" : root.GetType().Name, root);
			}
		}
	}

	internal static int GpuBuffers( RootPanel root ) => root.PanelCommandList.FindResource<Painter.Context>()?.Batcher.GpuBufferCount ?? 0;

	internal static RootPanel.RenderStatistics Sum( RootPanel.RenderStatistics total, RootPanel.RenderStatistics r ) => total with
	{
		LayoutTime = total.LayoutTime + r.LayoutTime,
		CommandBuildTime = total.CommandBuildTime + r.CommandBuildTime,
		SubmissionTime = total.SubmissionTime + r.SubmissionTime,
		PanelsRendered = total.PanelsRendered + r.PanelsRendered,
		PanelsCulled = total.PanelsCulled + r.PanelsCulled,
		DrawCalls = total.DrawCalls + r.DrawCalls,
		Instances = total.Instances + r.Instances,
		Layers = total.Layers + r.Layers,
		FrameGrabs = total.FrameGrabs + r.FrameGrabs,
	};

	static IEnumerable<Engine.GlobalContext> Contexts()
	{
		if ( Engine.GlobalContext.Game is { } game ) yield return game;
		if ( Engine.GlobalContext.Menu is { } menu && menu != Engine.GlobalContext.Game ) yield return menu;
	}

	static DevToolsTable.Cell Ms( TimeSpan time ) => new( DevToolsFormat.Ms( time.TotalMilliseconds ), "number dim" );
}
