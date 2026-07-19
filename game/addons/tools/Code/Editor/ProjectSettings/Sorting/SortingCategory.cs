namespace Editor.ProjectSettingPages;

[Title( "Sorting" ), Icon( "layers" )]
internal sealed class SortingCategory : ProjectSettingsWindow.Category
{
	public override void OnInit( Project project )
	{
		var col = BodyLayout.AddColumn();
		col.Spacing = 8;

		{
			col.Add( new Label.Header( "Sort Layers" ) );

			var list = col.Add( new SortLayerListWidget( ProjectSettings.Sorting ) );
			list.ValueChanged = () => StateHasChanged();
		}

		{
			col.Add( new InformationBox(
				"""
				<p>Sort layers control which sprites draw in front of which, regardless of where they are in the world.</p>
				<ul style="-qt-list-indent: 0; margin-left: 10px;" >
					<li>A sprite in a later layer always draws in front of a sprite in an earlier one
					<li>Within a layer, <b>Sort Order</b> on the Sprite Renderer decides - higher draws on top
					<li>Sprites with the same layer and order fall back to sorting by depth
					<li>Sprites only sort at all when <b>Is Sorted</b> is enabled on the renderer
				</ul>
				<p>Layers are referenced by identity, so renaming or reordering them will not re-sort anything unexpectedly.</p>
				""" ) );
		}
	}

	public override void OnSave()
	{
		EditorUtility.SaveProjectSettings( ProjectSettings.Sorting, "Sorting.config" );

		base.OnSave();
	}
}
