using Editor;
using static Sandbox.Internal.GlobalToolsNamespace;

namespace EditorTests;

[TestClass, DoNotParallelize]
public class EditorSettingsTests
{
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void ShutdownSavesSettingsOnlyAfterProjectRestore( bool restored )
	{
		var originalProject = ProjectCookie;
		var originalEditor = EditorCookie;
		var settings = EditorScene.GizmoSettings;
		var originalGrid = settings.GridSpacing;
		var originalSnap = settings.SnapToGrid;
		var originalAngle = settings.AngleSpacing;
		var originalAngleSnap = settings.SnapToAngles;
		var fs = new MemoryFileSystem();
		var project = new CookieContainer( "project", true, fs );
		var editor = new CookieContainer( "tools", true, fs );
		project.StopTimer();
		editor.StopTimer();

		try
		{
			ProjectCookie = project;
			EditorCookie = editor;
			if ( restored ) EditorScene.RestoreState();
			settings.GridSpacing = 0.5f;
			settings.SnapToGrid = true;
			settings.AngleSpacing = 22.5f;
			settings.SnapToAngles = false;

			new ToolsDll().Exiting();

			var saved = new CookieContainer( "project", true, fs );
			saved.StopTimer();
			Assert.AreEqual( restored, saved.TryGet<Gizmo.SceneSettings>( "gizmo.settings", out var value ) );
			if ( restored )
			{
				Assert.AreEqual( 0.5f, value.GridSpacing );
				Assert.IsTrue( value.SnapToGrid );
				Assert.AreEqual( 22.5f, value.AngleSpacing );
				Assert.IsFalse( value.SnapToAngles );
			}
		}
		finally
		{
			settings.GridSpacing = originalGrid;
			settings.SnapToGrid = originalSnap;
			settings.AngleSpacing = originalAngle;
			settings.SnapToAngles = originalAngleSnap;
			ProjectCookie = originalProject;
			EditorCookie = originalEditor;
			EditorScene.RestoreState();
			fs.Dispose();
		}
	}
}
