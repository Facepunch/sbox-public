namespace Editor.MeshEditor;

partial class ClipTool
{
	public override Widget CreateToolSidebar()
	{
		return new ClipToolWidget( this );
	}

	public class ClipToolWidget : ToolSidebarWidget
	{
		readonly ClipTool _tool;
		readonly Button _applyButton;
		readonly Button _cancelButton;
		readonly IconButton _keepFront;
		readonly IconButton _keepBack;
		readonly IconButton _keepBoth;

		public ClipToolWidget( ClipTool tool ) : base()
		{
			_tool = tool;

			AddTitle( "Clipping Tool", "content_cut" );

			{
				var group = AddGroup( "Keep Mode" );
				var row = group.AddRow();
				row.Spacing = 4;

				_keepFront = CreateButton( "Keep Front", "hammer/clipper_keep_front.png", null, () => Keep( ClipKeepMode.Front ), true, row );
				_keepBack = CreateButton( "Keep Back", "hammer/clipper_keep_back.png", null, () => Keep( ClipKeepMode.Back ), true, row );
				_keepBoth = CreateButton( "Keep Both", "hammer/clipper_keep_both.png", null, () => Keep( ClipKeepMode.Both ), true, row );
			}

			Layout.AddSpacingCell( 8 );

			{
				var so = tool.GetSerialized();
				var c = ControlSheetRow.Create( so.GetProperty( nameof( CapNewSurfaces ) ) );
				Layout.Add( c );
			}

			Layout.AddSpacingCell( 8 );

			{
				var row = Layout.AddRow();
				row.Spacing = 4;

				_applyButton = new Button( "Apply", "done" );
				_applyButton.Clicked = Apply;
				_applyButton.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.clip-apply" ) + "]";
				row.Add( _applyButton );

				_cancelButton = new Button( "Cancel", "close" );
				_cancelButton.Clicked = Cancel;
				_cancelButton.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.clip-cancel" ) + "]";
				row.Add( _cancelButton );
			}

			Layout.AddStretchCell();
		}

		void Keep( ClipKeepMode keepMode ) => _tool.KeepMode = keepMode;

		[Shortcut( "mesh.clip-apply", "enter", typeof( SceneViewWidget ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.clip-apply-stay", "space", typeof( SceneViewWidget ) )]
		void ApplyAndContinue() => _tool.Apply( false );

		[Shortcut( "mesh.clip-cancel", "ESC", typeof( SceneViewWidget ) )]
		void Cancel() => _tool.Cancel();

		[Shortcut( "mesh.clip-cycle-mode", "shift+x", typeof( SceneViewWidget ) )]
		void CycleMode() => _tool.CycleMode();

		[EditorEvent.Frame]
		public void Frame()
		{
			_applyButton?.Enabled = _tool.CanApply;
			_cancelButton?.Enabled = _tool.CanApply;
			_keepFront?.IsActive = _tool.KeepMode == ClipKeepMode.Front;
			_keepBack?.IsActive = _tool.KeepMode == ClipKeepMode.Back;
			_keepBoth?.IsActive = _tool.KeepMode == ClipKeepMode.Both;
		}
	}
}
