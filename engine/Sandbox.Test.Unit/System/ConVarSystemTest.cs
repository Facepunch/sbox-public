namespace SystemTest;

[TestClass]
[DoNotParallelize]
public class ConVarSystemTest
{
	private static bool RanProtectedCommand { get; set; }
	private static bool RanNormalCommand { get; set; }
	private static string LastArguments { get; set; }

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();

		ConVarSystem.Members["test_normal"] = new TestCommand( "test_normal", isProtected: false );
		ConVarSystem.Members["test_protected"] = new TestCommand( "test_protected", isProtected: true );

		RanProtectedCommand = false;
		RanNormalCommand = false;
		LastArguments = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		ConVarSystem.Members.Remove( "test_normal" );
		ConVarSystem.Members.Remove( "test_protected" );
	}

	[TestMethod]
	public void SplitCommands_BasicSemicolon()
	{
		var parts = ConVarSystem.SplitCommands( "one;two;three" ).ToArray();

		Assert.AreEqual( 3, parts.Length );
		Assert.AreEqual( "one", parts[0] );
		Assert.AreEqual( "two", parts[1] );
		Assert.AreEqual( "three", parts[2] );
	}

	[TestMethod]
	public void SplitCommands_BasicNewline()
	{
		var parts = ConVarSystem.SplitCommands( "one\ntwo\nthree" ).ToArray();

		Assert.AreEqual( 3, parts.Length );
		Assert.AreEqual( "one", parts[0] );
		Assert.AreEqual( "two", parts[1] );
		Assert.AreEqual( "three", parts[2] );
	}

	[TestMethod]
	public void SplitCommands_RespectsQuotedSemicolon()
	{
		var parts = ConVarSystem.SplitCommands( "cmd \"arg;with;semicolons\"" ).ToArray();

		Assert.AreEqual( 1, parts.Length );
		Assert.AreEqual( "cmd \"arg;with;semicolons\"", parts[0] );
	}

	[TestMethod]
	public void SplitCommands_RespectsQuotedNewline()
	{
		var parts = ConVarSystem.SplitCommands( "cmd \"arg\nwith\nnewlines\"" ).ToArray();
		Assert.AreEqual( 1, parts.Length );
		Assert.AreEqual( "cmd \"arg\nwith\nnewlines\"", parts[0] );
	}

	[TestMethod]
	public void SplitCommands_MixedQuotedAndUnquoted()
	{
		var parts = ConVarSystem.SplitCommands( "cmd1 \"arg;1\";cmd2;cmd3 \"arg;3\"" ).ToArray();
		Assert.AreEqual( 3, parts.Length );
		Assert.AreEqual( "cmd1 \"arg;1\"", parts[0] );
		Assert.AreEqual( "cmd2", parts[1] );
		Assert.AreEqual( "cmd3 \"arg;3\"", parts[2] );
	}

	[TestMethod]
	public void SplitCommands_EscapedQuotes()
	{
		var parts = ConVarSystem.SplitCommands( "cmd \\\"not;quoted\\\"" ).ToArray();
		Assert.AreEqual( 2, parts.Length );
	}

	[TestMethod]
	public void SplitCommands_EmptyInput()
	{
		var parts = ConVarSystem.SplitCommands( "" ).ToArray();
		Assert.AreEqual( 0, parts.Length );
	}

	[TestMethod]
	public void SplitCommands_SingleCommand()
	{
		var parts = ConVarSystem.SplitCommands( "just_one_command" ).ToArray();
		Assert.AreEqual( 1, parts.Length );
		Assert.AreEqual( "just_one_command", parts[0] );
	}

	[TestMethod]
	public void ConsoleSystemRun_NormalCommandRuns()
	{
		ConsoleSystem.Run( "test_normal" );
		Assert.IsTrue( RanNormalCommand );
	}

	[TestMethod]
	public void ConsoleSystemRun_ProtectedCommandThrows()
	{
		Assert.ThrowsException<System.Exception>( () => ConsoleSystem.Run( "test_protected" ) );
		Assert.IsFalse( RanProtectedCommand );
	}

	[TestMethod]
	public void ConsoleSystemRun_SemicolonInjectionBlocked()
	{
		ConsoleSystem.Run( "test_normal ;test_protected" );

		Assert.IsTrue( RanNormalCommand, "Normal command should have run" );
		Assert.IsFalse( RanProtectedCommand, "Protected command injected via semicolon should be blocked" );
	}

	[TestMethod]
	public void ConsoleSystemRun_NewlineInjectionBlocked()
	{
		Assert.ThrowsException<System.Exception>( () => ConsoleSystem.Run( "test_normal\ntest_protected" ) );

		Assert.IsFalse( RanNormalCommand, "Neither command should have run" );
		Assert.IsFalse( RanProtectedCommand, "Protected command injected via newline should be blocked" );
	}

	[TestMethod]
	public void ConsoleSystemRun_WrappedSemicolonInjectionBlocked()
	{
		ConsoleSystem.Run( "test_normal ;test_protected;" );

		Assert.IsTrue( RanNormalCommand, "Normal command should have run" );
		Assert.IsFalse( RanProtectedCommand, "Protected command injected via semicolons should be blocked" );
	}

	[TestMethod]
	public void ConsoleSystemRun_WithArgs_SemicolonInjectionBlocked()
	{
		ConsoleSystem.Run( "test_normal", ";test_protected" );

		Assert.IsTrue( RanNormalCommand, "Normal command should have run" );
		Assert.IsFalse( RanProtectedCommand, "Protected command injected via argument should be blocked" );
	}

	[TestMethod]
	public void ConsoleSystemRun_WithArgs_NormalCommandRuns()
	{
		ConsoleSystem.Run( "test_normal", "arg1", "arg2" );

		Assert.IsTrue( RanNormalCommand );
		Assert.AreEqual( "\"arg1\" \"arg2\"", LastArguments );
	}

	[TestMethod]
	public void ConsoleSystemRun_WithArgs_ProtectedCommandThrows()
	{
		Assert.ThrowsException<System.Exception>( () => ConsoleSystem.Run( "test_protected", "arg1" ) );
		Assert.IsFalse( RanProtectedCommand );
	}

	[TestMethod]
	public void RunSingle_AllowsNormalCommand()
	{
		ConVarSystem.RunSingle( "test_normal", allowProtected: false );

		Assert.IsTrue( RanNormalCommand );
	}

	[TestMethod]
	public void RunSingle_BlocksProtectedCommand()
	{
		ConVarSystem.RunSingle( "test_protected", allowProtected: false );

		Assert.IsFalse( RanProtectedCommand );
	}

	[TestMethod]
	public void RunSingle_AllowsProtectedWhenPermitted()
	{
		ConVarSystem.RunSingle( "test_protected", allowProtected: true );

		Assert.IsTrue( RanProtectedCommand );
	}

	class TestCommand : Command
	{
		public TestCommand( string name, bool isProtected )
		{
			Name = name;
			IsConCommand = true;
			IsProtected = isProtected;
		}

		public override void Run( string args )
		{
			if ( Name == "test_normal" )
			{
				RanNormalCommand = true;
				LastArguments = args;
			}
			else if ( Name == "test_protected" )
			{
				RanProtectedCommand = true;
				LastArguments = args;
			}
		}
	}
}
