using Sandbox.Engine;
using Sandbox.UI;

namespace ScriptingTests;

/// <summary>
/// Script-authored panels use the native stylesheet loading, cascade and reload paths.
/// </summary>
[TestClass, DoNotParallelize]
public class ScriptStyleSheetTests
{
	const string RedStyles = """
		.script-styles
		{
			background-color: red;
		}
		""";

	const string BlueStyles = """
		.script-styles
		{
			background-color: blue;
		}
		""";

	GlobalContext previous;
	GlobalContext previousGame;
	MemoryFileSystem files;
	AggregateFileSystem mounted;
	RootPanel root;
	Panel panel;

	/// <summary>
	/// Gives scripts an isolated UI and a mounted filesystem containing only test styles.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		previousGame = GlobalContext.Game;
		files = new MemoryFileSystem();
		files.CreateDirectory( "ui" );
		files.CreateDirectory( "styles/base" );
		files.WriteAllText( "styles/base/rootpanel.scss", "" );
		mounted = new AggregateFileSystem();
		mounted.Mount( files );
		GlobalContext.Current = GlobalContext.Game = new GlobalContext
		{
			TypeLibrary = SharedTypeLibrary.ScriptingWithSystem,
			FileMount = mounted,
			UISystem = new UISystem()
		};

		root = new RootPanel();
		panel = new Panel { Parent = root };
		panel.AddClass( "script-styles" );

		// Only assert against sheets added by the script, excluding the panel's automatic source lookup.
		panel.StyleSheet.Remove( "*" );
	}

	/// <summary>
	/// Releases stylesheet watchers and restores the original game context.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		StyleSheet.ResetStyleSheets();
		GlobalContext.Current.Shutdown();
		mounted.Dispose();
		files.Dispose();
		GlobalContext.Current = previous;
		GlobalContext.Game = previousGame;
	}

	/// <summary>
	/// The reported Panel.StyleSheet.Load call binds and keeps live mounted-file updates.
	/// </summary>
	[TestMethod]
	public async Task ScriptsCanLoadAndReloadMountedStyleSheets()
	{
		files.WriteAllText( "ui/script-styles.scss", RedStyles );

		Run( "panel.StyleSheet.Load(\"/ui/script-styles.scss\");" );
		var sheet = panel.StyleSheet.List.Single();
		AssertApplied( sheet, Color.Red );

		files.WriteAllText( "ui/script-styles.scss", BlueStyles );

		Assert.IsTrue( await FileWatch.TickUntilFileChanged( "*script-styles.scss" ) );
		Assert.AreSame( sheet, panel.StyleSheet.List.Single() );
		AssertApplied( sheet, Color.Blue );
	}

	/// <summary>
	/// Stylesheet text parsed by a script participates in the panel's native cascade.
	/// </summary>
	[TestMethod]
	public void ScriptsCanParseStyleSheets()
	{
		Run( "panel.StyleSheet.Parse(styles);", RedStyles );
		AssertApplied( panel.StyleSheet.List.Single(), Color.Red );
	}

	/// <summary>
	/// A script can create, attach and remove a native StyleSheet through its public contract.
	/// </summary>
	[TestMethod]
	public void ScriptsCanAddAndRemoveStyleSheets()
	{
		Run( """
			var sheet = StyleSheet.FromString(styles);
			panel.StyleSheet.Add(sheet);
			""", RedStyles );

		var sheet = panel.StyleSheet.List.Single();
		AssertApplied( sheet, Color.Red );

		var script = Game.Scripting.CreateScript( "panel.StyleSheet.Remove(sheet);" );
		Assert.IsTrue( script.With( "panel", panel ).With( "sheet", sheet ).Run(), script.LastError?.Message );
		Assert.AreEqual( 0, panel.StyleSheet.List.Count );
		root.BuildStyleRules();
		var cascade = new LayoutCascade { Scale = 1 };
		panel.Style.BuildCached( ref cascade );
		Assert.IsFalse( panel.Style.ContainsStyle( sheet.Nodes.Single().Styles ) );
	}

	/// <summary>
	/// File-based and inline stylesheet attributes are usable through the script catalog.
	/// </summary>
	[TestMethod]
	public void ScriptsCanConstructStyleSheetAttributes()
	{
		var script = Game.Scripting.CreateScript( """
			var file = new StyleSheetAttribute("/ui/script-styles.scss");
			var inline = new Sandbox.UI.StyleSheet.InlineAttribute("script-styles", "");
			return file.Name == "/ui/script-styles.scss" && inline.Name == "script-styles";
			""" );

		Assert.IsTrue( script.RunAndReturn<bool>(), script.LastError?.Message );
	}

	void Run( string source, string styles = null )
	{
		var script = Game.Scripting.CreateScript( source );
		script.SetArgument( "panel", panel );

		if ( styles is not null )
		{
			script.SetArgument( "styles", styles );
		}

		Assert.IsTrue( script.Run(), script.LastError?.Message );
	}

	void AssertApplied( StyleSheet sheet, Color color )
	{
		root.BuildStyleRules();
		var cascade = new LayoutCascade { Scale = 1 };
		panel.Style.BuildCached( ref cascade );
		var styles = sheet.Nodes.Single().Styles;
		Assert.AreEqual( color, styles.BackgroundColor );
		Assert.IsTrue( panel.Style.ContainsStyle( styles ) );
		Assert.AreEqual( color, panel.Style.Cached.BackgroundColor );
	}
}
