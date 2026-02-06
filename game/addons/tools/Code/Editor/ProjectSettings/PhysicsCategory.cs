namespace Editor.ProjectSettingPages;

[Title( "Physics" ), Icon( "sports_cricket" )]
internal sealed class PhysicsCategory : ProjectSettingsWindow.Category
{
	public override void OnInit( Project project )
	{
		var col = BodyLayout.AddColumn();
		col.Spacing = 8;

		{
			var so = ProjectSettings.Physics.GetSerialized();
			ListenForChanges( so );

			var cs = ControlSheet.Create( so );
			col.Add( cs );
		}

		{
			col.Add( new Label.Header( "Collision Rules" ) );
			var matrix = col.Add( new CollisionMatrixWidget( this ) );
			matrix.Rebuild( ProjectSettings.Collision );
			matrix.ValueChanged = () => StateHasChanged();

			col.Add( new Label( "Left Click to toggle. Shift and left to collide, Ctrl and left to ignore, alt and left to trigger, right click to clear." ) { WordWrap = true, Margin = 8 } );
		}

		{
			col.Add( new InformationBox(
				"""
				<p>The collision matrix represents what will happen when two physics bodies are about to touch. Your changes here should affect the game on save.</p>
				<p>Collisions are chosen using the following logic..</p>
				<ul style="-qt-list-indent: 0; margin-left: 10px;" >
					<li>Get tags from both objects
					<li>Matching pair? Use as collision
					<li>Multiple matching pairs? Use the least colliding (ignore, trigger, then collision)
					<li>No matching pairs? Use the tag's default value
					<li>Multiple default values? Use least colliding
					<li>No defaults & no pairs? Collide.
				</ul>
				""" ) );
		}
	}

	public override void OnSave()
	{
		EditorUtility.SaveProjectSettings( ProjectSettings.Collision, "Collision.config" );
		EditorUtility.SaveProjectSettings( ProjectSettings.Physics, "Physics.config" );

		base.OnSave();
	}
}
