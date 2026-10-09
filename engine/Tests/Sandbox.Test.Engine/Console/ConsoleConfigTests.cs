using Sandbox.Engine;

namespace EngineTests;

[TestClass, DoNotParallelize]
public class ConsoleConfigTests
{
	const string Name = "console_config_test_value";
	MemoryFileSystem fs;
	Command command;

	[TestInitialize]
	public void Initialize()
	{
		fs = new MemoryFileSystem();
		command = new Command { Name = Name, IsSaved = true, Value = "original" };
		ConVarSystem.Members.Add( Name, command );
	}

	[TestCleanup]
	public void Cleanup()
	{
		ConVarSystem.Members.Remove( Name );
		fs.Dispose();
	}

	[TestMethod]
	public void LegacyMigrationPreservesValuesAndRemovesOriginalAfterSaving()
	{
		fs.WriteAllText( "cfg/machine_convars.vcfg", "\"config\" { \"convars\" { \"" + Name + "\" \"hello world; quit\" } }" );
		Assert.IsTrue( ConsoleConfig.LoadVariables( fs, "cfg/machine_convars.vcfg", legacy: true ) );
		Assert.AreEqual( "hello world; quit", command.Value );
		Assert.IsTrue( fs.FileExists( "cfg/machine_convars.vcfg" ) );
		Assert.IsTrue( ConsoleConfig.SaveVariables( fs, [command] ) );
		Assert.IsFalse( fs.FileExists( "cfg/machine_convars.vcfg" ) );
		command.Value = "changed";
		Assert.IsTrue( ConsoleConfig.LoadVariables( fs, "cfg/machine_convars.json" ) );
		Assert.AreEqual( "hello world; quit", command.Value );
	}

	[TestMethod]
	[DataRow( "0", "0" )]
	[DataRow( "123", "123" )]
	[DataRow( "0.75", "0.75" )]
	public void LegacyNumericValuesAreConvertedToStrings( string input, string expected )
	{
		fs.WriteAllText( "cfg/legacy.vcfg", "\"config\" { \"convars\" { \"" + Name + "\" \"" + input + "\" } }" );
		Assert.IsTrue( ConsoleConfig.LoadVariables( fs, "cfg/legacy.vcfg", legacy: true ) );
		Assert.AreEqual( expected, command.Value );
	}

	[TestMethod]
	public void JsonRoundTripsQuotesNewlinesAndEmptyValues()
	{
		foreach ( var value in new[] { "", "spaces and \"quotes\"; quit\nnext", "\\path\\" } )
		{
			command.Value = value;
			Assert.IsTrue( ConsoleConfig.SaveVariables( fs, [command] ) );
			command.Value = "changed";
			Assert.IsTrue( ConsoleConfig.LoadVariables( fs, "cfg/machine_convars.json" ) );
			Assert.AreEqual( value, command.Value );
		}
	}

	[TestMethod]
	public void InvalidDocumentDoesNotApplyPartialValuesOrDeleteLegacy()
	{
		fs.WriteAllText( "cfg/machine_convars.vcfg", "legacy" );
		fs.WriteAllText( "cfg/machine_convars.json", "{\"convars\":{\"" + Name + "\":\"changed\",\"invalid\":null}}" );
		Assert.IsFalse( ConsoleConfig.LoadVariables( fs, "cfg/machine_convars.json" ) );
		Assert.AreEqual( "original", command.Value );
		Assert.IsTrue( fs.FileExists( "cfg/machine_convars.vcfg" ) );
	}

	[TestMethod]
	public void UnarchivedVariablesAndCommandsAreIgnored()
	{
		fs.WriteAllText( "cfg/test.json", "{\"convars\":{\"" + Name + "\":\"changed\"}}" );
		command.IsSaved = false;
		Assert.IsTrue( ConsoleConfig.LoadVariables( fs, "cfg/test.json" ) );
		Assert.AreEqual( "original", command.Value );
		command.IsSaved = true;
		command.IsConCommand = true;
		Assert.IsTrue( ConsoleConfig.LoadVariables( fs, "cfg/test.json" ) );
		Assert.AreEqual( "original", command.Value );
	}

	[TestMethod]
	public void MalformedUserConfigIsPreservedAndSavingCanResume()
	{
		const string invalid = "{broken";
		fs.WriteAllText( "cfg/machine_convars.json", invalid );
		using var storage = new ConsoleConfig.VariableStorage( fs );
		Assert.IsTrue( storage.Initialize( [command] ) );
		Assert.AreEqual( invalid, fs.ReadAllText( "cfg/machine_convars.json.bad" ) );
		Assert.IsFalse( fs.FileExists( "cfg/machine_convars.json" ) );
		Assert.AreEqual( "original", command.Value );
		Assert.IsTrue( storage.Save( [command] ) );
		Assert.IsTrue( storage.Initialize( [command] ) );
		Assert.AreEqual( invalid, fs.ReadAllText( "cfg/machine_convars.json.bad" ) );
	}
}
