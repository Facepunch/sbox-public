using Editor;
using Editor.Mcp;
using SceneTests;
using System;

namespace McpTests;

/// <summary>
/// <para>
/// editor_status reports the scene the user is looking at, which is the active editor
/// session's scene - not the game scene. Reading Game.ActiveScene instead made the tool
/// report the wrong scene (or none at all) whenever the two differed.
/// </para>
/// <para>
/// Reproduces <see href="https://github.com/Facepunch/sbox-public/issues/11640">Facepunch/sbox-public#11640</see>.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public class EditorStatusTest : SceneTest
{
	/// <summary>
	/// <see cref="TopLevelTools.GetEditorStatus"/> reports FileSystem.Root, which this tier
	/// never initializes - Bootstrap is what normally does it, and Bootstrap never runs here.
	/// Point it at the game folder (already the working directory, see the assembly fixture)
	/// with the base folder mounts skipped, since nothing here reads content.
	/// </summary>
	[ClassInitialize]
	public static void InitializeFilesystem( TestContext context )
	{
		if ( EngineFileSystem.Root is null )
		{
			EngineFileSystem.Initialize( Environment.CurrentDirectory, skipBaseFolderInit: true );
		}
	}

	/// <summary>
	/// Editor sessions live in a static list, so a session left behind would change what the
	/// next test's editor_status reports. Destroy is the supported teardown and is headless
	/// safe: it has no game session to stop, and the editor window and scene dock it would
	/// otherwise touch are both null outside the editor.
	/// </summary>
	[TestCleanup]
	public void DestroyRemainingSessions()
	{
		foreach ( var session in SceneEditorSession.All.ToArray() )
		{
			session.Destroy();
		}

		Assert.IsNull( SceneEditorSession.Active, "A session survived teardown" );
	}

	/// <summary>
	/// A GameEditorSession is the session type that can be built headlessly - the plain
	/// SceneEditorSession constructor is protected and creates a scene dock, which needs an
	/// editor window. The parent is only used by StopPlaying and FrameTo, neither of which
	/// this test calls.
	/// </summary>
	static SceneEditorSession Session( string sceneName )
	{
		var scene = new Scene { Name = sceneName };

		return new GameEditorSession( null, scene );
	}

	/// <summary>
	/// The reported active scene is the active session's scene.
	/// </summary>
	[TestMethod]
	public void ReportsTheActiveSessionsScene()
	{
		Session( "My Editor Scene" ).MakeActive();

		Assert.AreEqual( "My Editor Scene", TopLevelTools.GetEditorStatus().ActiveScene );
	}

	/// <summary>
	/// With several sessions open it follows whichever one is active, which is the tab the
	/// user is actually looking at.
	/// </summary>
	[TestMethod]
	public void FollowsWhicheverSessionIsActive()
	{
		var first = Session( "First Scene" );
		var second = Session( "Second Scene" );

		first.MakeActive();

		Assert.AreEqual( "First Scene", TopLevelTools.GetEditorStatus().ActiveScene );

		second.MakeActive();

		Assert.AreEqual( "Second Scene", TopLevelTools.GetEditorStatus().ActiveScene );
	}

	/// <summary>
	/// A scene the user hasn't touched reports no unsaved changes, and an unsaved scene has
	/// no source path - both read off the same active session.
	/// </summary>
	[TestMethod]
	public void ReportsTheActiveSessionsSaveState()
	{
		var session = Session( "Unsaved Scene" );
		session.MakeActive();

		var status = TopLevelTools.GetEditorStatus();

		Assert.IsFalse( status.SceneHasUnsavedChanges );
		Assert.IsNull( status.ActiveScenePath );

		session.HasUnsavedChanges = true;

		Assert.IsTrue( TopLevelTools.GetEditorStatus().SceneHasUnsavedChanges );
	}
}
