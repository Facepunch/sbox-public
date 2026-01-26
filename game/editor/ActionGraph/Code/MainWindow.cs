using Editor.NodeEditor;
using Facepunch.ActionGraphs;
using Sandbox;
using Sandbox.ActionGraphs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Editor.ActionGraphs;

public partial class MainWindow : DockWindow, EditorEvent.ISceneView, IActionGraphEvents
{
	public event Action<object> SelectionChanged;
	public event Action<Node.Input, int?> FocusedOnInput;

	internal static List<MainWindow> AllWindows { get; } = new List<MainWindow>();

	private static string FormatMessageContext( IMessageContext context )
	{
		return context switch
		{
			ActionGraph graph => graph.Title,
			Node node => node.GetDisplayInfo().Name,
			Link link => FormatMessageContext( link.Target ),
			Node.IParameter parameter => FormatMessageContext( parameter.Node ),
			Variable variable => variable.Name,
			_ => $"{context}"
		};
	}

	private static string GetTargetName( ActionGraph graph )
	{
		switch ( graph.GetEmbeddedTarget() )
		{
			case GameObject go:
				return go.Name;

			default:
				return null;
		}
	}

	private static string GetSourceName( ISourceLocation source )
	{
		return source switch
		{
			GameResourceSourceLocation { Resource: SceneFile { ResourcePath: null } } => "Play Mode",
			GameResourceSourceLocation { Resource: SceneFile { ResourceName: { } name } } => name,
			_ => source.ToString()
		};
	}

	public static string GetFullPath( ActionGraph graph )
	{
		if ( graph.SourceLocation is null )
		{
			return null;
		}

		return GetTargetName( graph ) is { } targetName
			? $"{GetSourceName( graph.SourceLocation )} - {targetName} - {graph.Title}"
			: $"{GetSourceName( graph.SourceLocation )} - {graph.Title}";
	}

	[StackLineHandler( @"^at (.+?) in (.+/Facepunch.ActionGraphs/.+):line (.+)$", Order = 910 )]
	public static StackRow ActionGraphLibraryStackLineHandler( Match match )
	{
		var row = new StackRow( match.Value, null ) { IsFromAction = true };

		return row;
	}

	[StackLineHandler( @"at ActionGraph\.(?<guid>[a-fA-F0-9-]+)(?:\.Node(?<nodeid>[0-9]+))?(?:\.[^(]+)?(?:\(.*\))?", Order = 900 )]
	public static StackRow ActionGraphStackLineHandler( Match match )
	{
		if ( !Guid.TryParse( match.Groups["guid"].Value, out var guid ) )
		{
			return null;
		}

		var graph = EditorNodeLibrary.GetGraphs( guid ).FirstOrDefault();

		if ( graph is null )
		{
			return new StackRow( $"Unknown Action Graph ({guid})", null );
		}

		IMessageContext context = graph;

		if ( match.Groups["nodeid"].Success && int.TryParse( match.Groups["nodeid"].Value, out var nodeId ) && graph.Nodes.TryGetValue( nodeId, out var node ) )
		{
			context = node;
		}

		var row = new StackRow( FormatMessageContext( context ), GetFullPath( graph ) )
		{
			IsFromEngine = false,
			IsFromAction = true
		};

		if ( graph.SourceLocation is not null )
		{
			row.MouseClick += () =>
			{
				var view = ActionGraphView.Open( graph );

				switch ( context )
				{
					case Link l:
						view.SelectLink( l );
						break;

					case Node n:
						view.SelectNode( n );
						break;
				}

				view.CenterOnSelection();
			};
		}

		return row;
	}

	private List<ActionGraphView> Views { get; } = new();

	public IReadOnlyList<ActionGraphView> OpenViews => Views;

	public ActionGraphView FocusedView => Views.LastOrDefault();

	private Option _undoMenuOption;
	private Option _redoMenuOption;

	public bool CanOpenMultipleAssets => true;

	public MainWindow( Window parent )
	{
		DeleteOnClose = true;

		Size = new Vector2( 1280, 720 );

		// Make this window stay on top of the editor, by making it a dialog
		Parent = parent;
		WindowFlags = WindowFlags.Dialog | WindowFlags.Customized | WindowFlags.CloseButton | WindowFlags.WindowSystemMenuHint | WindowFlags.WindowTitle | WindowFlags.MaximizeButton;

		SetWindowIcon( "electrical_services" );

		AllWindows.Add( this );

		RebuildUI();
	}

	protected override void OnClosed()
	{
		base.OnClosed();

		AllWindows.Remove( this );
	}

	protected override void OnFocus( FocusChangeReason reason )
	{
		base.OnFocus( reason );

		// Move this window to the end of the list, so it has priority
		// when opening a new graph

		AllWindows.Remove( this );
		AllWindows.Add( this );
	}

	public ActionGraphView Open( ActionGraph actionGraph )
	{
		var view = new ActionGraphView( actionGraph ) { Window = this };
		var sibling = Views.LastOrDefault();

		Views.Add( view );

		if ( !sibling.IsValid() )
		{
			DockManager.AddDock( null, view, DockArea.RightOuter, split: 1f );
		}
		else
		{
			DockManager.AddDock( sibling, view, DockArea.Inside );
		}

		DockManager.Update();

		return view;
	}

	internal void UpdateTitle( ActionGraphView view )
	{
		Title = $"{view.ActionGraph.Title} - Action Graph";
	}

	public void UpdateMenuOptions( UndoStack undoStack )
	{
		_undoMenuOption.Enabled = undoStack?.CanUndo ?? false;
		_redoMenuOption.Enabled = undoStack?.CanRedo ?? false;
		_undoMenuOption.Text = undoStack?.UndoName ?? "Undo";
		_redoMenuOption.Text = undoStack?.RedoName ?? "Redo";
		_undoMenuOption.StatusTip = undoStack?.UndoName;
		_redoMenuOption.StatusTip = undoStack?.RedoName;
	}

	public void RebuildUI()
	{
		RestoreDefaultDockLayout();

		MenuBar.Clear();

		{
			var file = MenuBar.AddMenu( "File" );
			file.AddOption( new Option( "Save" ) { ShortcutName = "editor.save", Triggered = Save } );
			file.AddSeparator();
			file.AddOption( new Option( "Quit" ) { ShortcutName = "editor.quit", Triggered = Quit } );
		}

		{
			var edit = MenuBar.AddMenu( "Edit" );

			_undoMenuOption = edit.AddOption( "Undo", "undo", Undo, "editor.undo" );
			_redoMenuOption = edit.AddOption( "Redo", "redo", Redo, "editor.redo" );

			edit.AddSeparator();
			edit.AddOption( "Cut", "common/cut.png", CutSelection, "editor.cut" );
			edit.AddOption( "Copy", "common/copy.png", CopySelection, "editor.copy" );
			edit.AddOption( "Paste", "common/paste.png", PasteSelection, "editor.paste" );
			edit.AddOption( "Select All", "select_all", SelectAll, "editor.select-all" );

			edit.AddSeparator();

			edit.AddOption( "Clean Up", "cleaning_services", CleanUp );
		}

		{
			var view = MenuBar.AddMenu( "View" );

			view.AboutToShow += () => OnViewMenu( view );
		}

		{
			var debug = MenuBar.AddMenu( "Debug" );

			debug.AddOption( "Log Expression", "code", LogExpression );
			debug.AddOption( "Log Instances", "list_alt", LogInstances );
		}

		UpdateMenuOptions( FocusedView?.UndoStack );
	}

	private void OnViewMenu( Menu view )
	{
		view.Clear();
		view.AddOption( "Restore To Default", "settings_backup_restore", RestoreDefaultDockLayout );
		view.AddSeparator();

		foreach ( var dock in DockManager.DockTypes )
		{
			var o = view.AddOption( dock.Title, dock.Icon );
			o.Checkable = true;
			o.Checked = DockManager.IsDockOpen( dock.Title );
			o.Toggled += ( b ) => DockManager.SetDockState( dock.Title, b );
		}

		view.AddSeparator();

		var style = view.AddOption( "Grid-Aligned Wires", "turn_sharp_right" );
		style.Checkable = true;
		style.Checked = ActionGraphView.EnableGridAlignedWires;
		style.Toggled += b => ActionGraphView.EnableGridAlignedWires = b;
	}

	protected override void RestoreDefaultDockLayout()
	{
		var openViews = Views
			.Select( x => x.ActionGraph )
			.ToArray();

		var properties = new Properties( this );
		var errorList = new ErrorList( null, this );

		DockManager.Clear();
		DockManager.RegisterDockType( "Inspector", "edit", () => new Properties( this ) );
		DockManager.RegisterDockType( "ErrorList", "error", () => new ErrorList( null, this ) );

		DockManager.AddDock( null, properties, DockArea.Left, DockManager.DockProperty.HideOnClose );
		DockManager.AddDock( properties, errorList, DockArea.Bottom, DockManager.DockProperty.HideOnClose, split: 0.75f );

		foreach ( var graph in openViews )
		{
			Open( graph );
		}

		DockManager.Update();
	}

	[Shortcut( "editor.quit", "CTRL+Q", ShortcutType.Window )]
	private void Quit()
	{
		Close();
	}

	[Shortcut( "editor.save", "Ctrl+S", ShortcutType.Window )]
	private void Save()
	{
		FocusedView?.Save();
	}

	[Shortcut( "editor.undo", "Ctrl+Z", ShortcutType.Window )]
	private void Undo()
	{
		FocusedView?.Undo();
	}

	[Shortcut( "editor.redo", "Ctrl+Y", ShortcutType.Window )]
	private void Redo()
	{
		FocusedView?.Redo();
	}

	[Shortcut( "editor.cut", "Ctrl+X", ShortcutType.Window )]
	private void CutSelection()
	{
		FocusedView?.CutSelection();
	}

	[Shortcut( "editor.copy", "Ctrl+C", ShortcutType.Window )]
	private void CopySelection()
	{
		FocusedView?.CopySelection();
	}

	[Shortcut( "editor.paste", "Ctrl+V", ShortcutType.Window )]
	private void PasteSelection()
	{
		FocusedView?.PasteSelection();
	}

	[Shortcut( "editor.duplicate", "CTRL+D", ShortcutType.Window )]
	private void DuplicateSelection()
	{
		FocusedView?.DuplicateSelection();
	}

	[Shortcut( "editor.select-all", "Ctrl+A", ShortcutType.Window )]
	private void SelectAll()
	{
		FocusedView?.SelectAll();
	}

	[Shortcut( "editor.clear-selection", "ESC", ShortcutType.Window )]
	private void ClearSelection()
	{
		FocusedView?.ClearSelection();
	}

	[Shortcut( "gameObject.frame", "F", ShortcutType.Window )]
	private void CenterOnSelection()
	{
		FocusedView?.CenterOnSelection();
	}

	private void CleanUp()
	{
		FocusedView?.CleanUp();
	}

	private void LogExpression()
	{
		FocusedView?.LogExpression();
	}

	private void LogInstances()
	{
		FocusedView?.LogInstances();
	}

	internal void OnFocusView( ActionGraphView view )
	{
		Views.Remove( view );
		Views.Add( view );

		DispatchSelectionChanged( view );

		UpdateTitle( view );
	}

	internal void OnRemoveView( ActionGraphView view )
	{
		Views.Remove( view );
	}

	internal void DispatchFocusedOnInput( Node.Input input, int? index )
	{
		FocusedOnInput?.Invoke( input, index );
	}

	internal void DispatchSelectionChanged( ActionGraphView view )
	{
		var node = view.SelectedItems
			.OfType<NodeUI>()
			.MaxBy( n => n is CommentUI );

		SelectionChanged?.Invoke( (object)node ?? view.EditorGraph );
	}

	public void SelectNode( Node node )
	{
		if ( Views.FirstOrDefault( x => x.ActionGraph == node.ActionGraph ) is not { } view )
		{
			return;
		}

		DockManager.RaiseDock( view.Name );

		view.SelectNode( node );
	}

	public void SelectLinks( IEnumerable<Link> links )
	{
		var linkArray = links.ToArray();

		if ( linkArray.FirstOrDefault()?.Target.Node.ActionGraph is not { } graph )
		{
			return;
		}

		if ( Views.FirstOrDefault( x => x.ActionGraph == graph ) is not { } view )
		{
			return;
		}

		DockManager.RaiseDock( view.Name );

		view.SelectLinks( linkArray );
	}

	public void SelectLink( Link link )
	{
		SelectLinks( new[] { link } );
	}

	void EditorEvent.ISceneView.DrawGizmos( Scene scene )
	{
		SceneRefGizmo.Draw( scene );
	}

	void IActionGraphEvents.SceneReferenceTriggered( SceneReferenceTriggeredEvent ev )
	{
		SceneRefGizmo.Trigger( ev );
	}
}
