namespace Editor.AnimGraph;

/// <summary>
/// Compile log dock - shows the warnings from the last compile. Clicking a
/// warning jumps to the offending node, wherever it is in the graph.
/// </summary>
public class CompileLog : Widget
{
	private readonly Window _window;
	private readonly LogListView _listView;

	public CompileLog( Window window ) : base( window )
	{
		_window = window;

		Name = "CompileLog";
		WindowTitle = "Compile Log";
		SetWindowIcon( "rule" );

		Layout = Layout.Column();

		var panel = Layout.Add( new Widget( this ) { Layout = Layout.Column() }, 1 );
		panel.Layout.Margin = 4;
		panel.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.WindowBackground );
			Paint.DrawRect( panel.LocalRect, Theme.ControlRadius );
			return true;
		};

		_listView = new LogListView( this, panel );
		panel.Layout.Add( _listView, 1 );

		Refresh();
	}

	internal void Refresh()
	{
		var warnings = _window.CompileWarnings;
		var errorCount = warnings.Count( warning => warning.IsError );
		var warningCount = warnings.Count - errorCount;

		var summary = _window.IsPreviewCompiling
			? "Compiling preview..."
			: _window.CompileTime is { } ms
			? $"Compiled in {ms:0.0} ms" + (errorCount, warningCount) switch
			{
				(0, 0) => ", no warnings",
				(0, 1) => ", 1 warning",
				(0, var w) => $", {w} warnings",
				(1, 0) => ", 1 error",
				(var e, 0) => $", {e} errors",
				(var e, var w) => $", {e} error{(e == 1 ? "" : "s")}, {w} warning{(w == 1 ? "" : "s")}",
			}
			: "Not compiled yet";

		_listView.SetEntries( _window.IsPreviewCompiling
			? [summary]
			: [summary, .. warnings.OrderByDescending( warning => warning.IsError )] );
		Update();
	}

	private class LogListView : ListView
	{
		private readonly CompileLog _log;

		public LogListView( CompileLog log, Widget parent ) : base( parent )
		{
			_log = log;

			ItemSize = new Vector2( 0, Theme.RowHeight );
			ItemSpacing = 0;
			Margin = 2;
			FocusMode = FocusMode.Click;
			AcceptDrops = false;
			MultiSelect = true;

			TranslucentBackground = true;
			NoSystemBackground = true;
			ItemContextMenu = _ =>
			{
				var count = SelectedItems.Count();
				var menu = new Menu( this );
				menu.AddOption( count > 1 ? $"Copy {count} messages" : "Copy message", "content_copy", CopySelection, "Ctrl+C" );
				menu.OpenAtCursor();
			};
		}

		public void SetEntries( IEnumerable<object> entries )
		{
			UnselectAll();
			SetItems( entries );
		}

		private void CopySelection()
		{
			var messages = Items.Where( IsSelected ).Select( FormatClipboardEntry ).ToArray();
			if ( messages.Length > 0 )
				EditorUtility.Clipboard.Copy( string.Join( Environment.NewLine, messages ) );
		}

		private string FormatClipboardEntry( object item )
		{
			var emoji = item switch
			{
				Compiler.Warning { IsError: true } => "\u274C",
				Compiler.Warning => "\u26A0\uFE0F",
				_ when _log._window is { IsPreviewCompiling: false, CompileTime: not null, CompileWarnings.Count: 0 } => "\u2705",
				_ => "\U0001F552"
			};
			return $"{emoji} {item}";
		}

		protected override string GetTooltip( object item ) => item.ToString();

		private static bool IsSelectionShortcut( KeyEvent e ) => e.HasCtrl && !e.HasAlt && !e.HasShift && e.Key is (KeyCode.C or KeyCode.A);

		protected override void OnShortcutPressed( KeyEvent e )
		{
			if ( IsSelectionShortcut( e ) )
			{
				e.Accepted = true;
				return;
			}

			base.OnShortcutPressed( e );
		}

		protected override void OnKeyPress( KeyEvent e )
		{
			if ( IsSelectionShortcut( e ) )
			{
				e.Accepted = true;
				if ( e.Key == KeyCode.A )
				{
					SelectItems( Items );
					Update();
				}
				else
				{
					CopySelection();
				}
				return;
			}

			base.OnKeyPress( e );
		}

		protected override void PaintItem( VirtualWidget item )
		{
			if ( item.Selected || item.Hovered )
			{
				Paint.ClearPen();
				Paint.SetBrush( item.Selected ? Theme.SelectedBackground : Theme.SelectedBackground.WithAlpha( 0.25f ) );
				Paint.DrawRect( item.Rect );
			}

			var rect = item.Rect.Shrink( 4, 0, 8, 0 );
			var iconRect = new Rect( rect.Position, new Vector2( 18, rect.Height ) );
			var textRect = rect with { Left = rect.Left + 22 };

			if ( item.Object is not Compiler.Warning warning )
			{
				var compiling = _log._window.IsPreviewCompiling;
				var clean = !compiling && _log._window is { CompileTime: not null, CompileWarnings.Count: 0 };
				Paint.SetPen( compiling ? Theme.Blue : clean ? Theme.Green.WithAlpha( 0.8f ) : Theme.Text.WithAlpha( 0.4f ) );
				Paint.DrawIcon( iconRect, clean ? "check_circle" : "schedule", 14 );
				Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
				Paint.SetDefaultFont( italic: true );
				Paint.DrawText( textRect, (string)item.Object, TextFlag.LeftCenter | TextFlag.SingleLine );
				return;
			}

			var clickable = warning.Node is not null;

			Paint.SetPen( warning.IsError ? Theme.Red.WithAlpha( 0.9f ) : Theme.Yellow.WithAlpha( 0.9f ) );
			Paint.DrawIcon( iconRect, warning.IsError ? "error" : "warning", 14 );

			Paint.SetDefaultFont();
			if ( clickable )
			{
				var name = Paint.GetElidedText( warning.Node.DisplayInfo.Name, textRect.Width * 0.3f );
				var nodeRect = textRect;
				nodeRect.Left = nodeRect.Right - MathF.Ceiling( Paint.MeasureText( name ).x );
				Paint.SetPen( Theme.Text.WithAlpha( 0.45f ) );
				Paint.DrawText( nodeRect, name, TextFlag.RightCenter | TextFlag.SingleLine );
				textRect.Right = nodeRect.Left - 12;
			}

			Paint.SetPen( Theme.Text.WithAlpha( item.Selected || item.Hovered ? 0.95f : 0.8f ) );
			Paint.DrawText( textRect, Paint.GetElidedText( warning.Message, textRect.Width ), TextFlag.LeftCenter | TextFlag.SingleLine );
		}

		protected override void OnMousePress( MouseEvent e )
		{
			base.OnMousePress( e );

			if ( e.LeftMouseButton && !e.HasCtrl && !e.HasShift && GetItemAt( e.LocalPosition ) is null )
			{
				UnselectAll();
				Update();
				e.Accepted = true;
			}
		}

		protected override bool OnItemPressed( VirtualWidget pressedItem, MouseEvent e )
		{
			if ( !e.LeftMouseButton )
				return false;

			if ( !e.HasCtrl && !e.HasShift && pressedItem.Object is Compiler.Warning { Node: { } node } )
				_log._window.NavigateToNode( node.Identifier );

			Focus();
			return true;
		}
	}
}
