namespace Editor.MeshEditor;

partial class DisplacementTool
{
	public override Widget CreateToolSidebar()
	{
		return new DisplacementToolWidget( this );
	}

	public class DisplacementToolWidget : ToolSidebarWidget
	{
		readonly Label _selectionCountLabel;
		readonly Widget _flattenSubModeRow;
		readonly DisplacementTool _tool;

		public DisplacementToolWidget( DisplacementTool tool ) : base()
		{
			_tool = tool;

			AddTitle( "Displacement Tool", "landscape" );

			var so = tool.GetSerialized();

			{
				var group = AddGroup( "Displace On" );
				var control = ControlWidget.Create( so.GetProperty( nameof( tool.LimitMode ) ) );
				control.FixedHeight = Theme.ControlHeight;
				group.Add( control );

				_selectionCountLabel = new Label( this );
				_selectionCountLabel.SetStyles( "color: #888; font-size: 11px; margin-left: 12px; margin-top: 2px; margin-bottom: 2px;" );
				group.Add( _selectionCountLabel );
			}
			{
				var group = AddGroup( "Mode" );
				var control = ControlWidget.Create( so.GetProperty( nameof( tool.Mode ) ) );
				control.FixedHeight = Theme.ControlHeight;
				group.Add( control );

				_flattenSubModeRow = ControlSheetRow.Create( so.GetProperty( nameof( tool.FlattenSubMode ) ) );
				group.Add( _flattenSubModeRow );
			}
			{
				var group = AddGroup( "Brush" );
				group.Add( ControlSheetRow.Create( so.GetProperty( nameof( tool.Radius ) ) ) );
				group.Add( ControlSheetRow.Create( so.GetProperty( nameof( tool.Strength ) ) ) );
				group.Add( ControlSheetRow.Create( so.GetProperty( nameof( tool.Hardness ) ) ) );
				group.Add( ControlSheetRow.Create( so.GetProperty( nameof( tool.NormalDir ) ) ) );
				group.Add( ControlSheetRow.Create( so.GetProperty( nameof( tool.PaintBackfacing ) ) ) );
			}
			{
				var group = AddGroup( "Visualization" );

				group.Add( ControlSheetRow.Create( so.GetProperty( nameof( tool.ShowVerts ) ) ) );
			}

			Layout.AddStretchCell();
		}

		[EditorEvent.Frame]
		void UpdateFrame()
		{
			UpdateSelectionCount();
			UpdateFlattenSubModeVisibility();
		}

		void UpdateSelectionCount()
		{
			if ( _tool.LimitMode == DisplaceLimitMode.Everything )
			{
				_selectionCountLabel.Visible = false;
				return;
			}

			var (count, name) = _tool.LimitMode switch
			{
				DisplaceLimitMode.Objects => (
				_tool._selectedMeshes.Count,
				_tool._selectedMeshes.Count == 1 ? "object" : "objects"),

				DisplaceLimitMode.Faces => (
				SelectionTool.GetAllSelected<MeshFace>().Count(),
				SelectionTool.GetAllSelected<MeshFace>().Count() == 1 ? "face" : "faces"),

				DisplaceLimitMode.Vertices => (
				SelectionTool.GetAllSelected<MeshVertex>().Count(),
				SelectionTool.GetAllSelected<MeshVertex>().Count() == 1 ? "vertex" : "vertices"),

				_ => (0, "selected")
			};

			_selectionCountLabel.Visible = true;
			_selectionCountLabel.Text = $"{count} {name} selected";
		}

		void UpdateFlattenSubModeVisibility()
		{
			if ( _flattenSubModeRow is not null )
				_flattenSubModeRow.Visible = _tool.Mode == DisplaceMode.Flatten;
		}
	}
}
