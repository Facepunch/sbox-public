namespace CompilingTests;

/// <summary>
/// <para>
/// Tests for method body changes that can use <see cref="ILHotload"/> as a fast path.
/// Each test compiles multiple versions of a .cs file in code/fastpath/, with the version
/// number before the file extension (e.g. HelloWorld.1.cs).
/// </para>
/// <para>
/// We couldn't use preprocessor directives because they aren't detected as per-file edits.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public partial class FastPathTest
{
	/// <summary>
	/// Adding a statement within a method body.
	/// </summary>
	[TestMethod]
	public async Task StatementAdded()
	{
		using var compiler = new FastPathTestCompiler( "HelloWorld.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 1, result.ChangedMethods.Length );

		Assert.AreEqual( 0, TestProgram( program, "Hello World!" ) );
	}

	/// <summary>
	/// Removing a statement within a method body.
	/// </summary>
	[TestMethod]
	public async Task StatementRemoved()
	{
		using var compiler = new FastPathTestCompiler( "HelloWorld.cs" );

		var result = await compiler.BuildAsync( version: 2 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, "Hello World!" ) );

		result = await compiler.BuildAsync( version: 1 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 1, result.ChangedMethods.Length );

		Assert.AreEqual( 0, TestProgram( program ) );
	}

	/// <summary>
	/// Adding and then removing a statement within a method body.
	/// </summary>
	[TestMethod]
	public async Task StatementAddedRemoved()
	{
		using var compiler = new FastPathTestCompiler( "HelloWorld.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 1, result.ChangedMethods.Length );

		Assert.AreEqual( 0, TestProgram( program, "Hello World!" ) );

		result = await compiler.BuildAsync( version: 1 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 1, result.ChangedMethods.Length );

		Assert.AreEqual( 0, TestProgram( program ) );
	}

	/// <summary>
	/// Tests multiple changed methods in the same file.
	/// </summary>
	[TestMethod]
	public async Task MultipleChanges()
	{
		using var compiler = new FastPathTestCompiler( "MultipleChanges.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, "Hello World!" ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 2, result.ChangedMethods.Length );

		Assert.AreEqual( 1, TestProgram( program ) );
	}

	/// <summary>
	/// Tests only a string changing in a method body, which doesn't cause any IL to change but
	/// we should still swap it.
	/// </summary>
	[TestMethod]
	public async Task MetadataChange()
	{
		using var compiler = new FastPathTestCompiler( "HelloWorld.cs" );

		var result = await compiler.BuildAsync( version: 2 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, "Hello World!" ) );

		result = await compiler.BuildAsync( version: 3 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 1, result.ChangedMethods.Length );

		Assert.AreEqual( 0, TestProgram( program, "Hello Blorld!" ) );
	}

	/// <summary>
	/// Tests multiple methods having the same name, but only one changes.
	/// </summary>
	[TestMethod]
	public async Task OverloadMethod()
	{
		using var compiler = new FastPathTestCompiler( "Overloads.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 1, result.ChangedMethods.Length );

		Assert.AreEqual( 0, TestProgram( program, "Hello World!" ) );

		result = await compiler.BuildAsync( version: 3 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 2, result.ChangedMethods.Length );

		Assert.AreEqual( 0, TestProgram( program ) );
	}

	/// <summary>
	/// Tests changing a define in project settings.
	/// </summary>
	[TestMethod]
	public async Task PreProcessorSymbolChange()
	{
		using var compiler = new FastPathTestCompiler( "HelloWorld.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		compiler.Config = compiler.Config with { DefineConstants = "TEST_DEFINE" };

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );
	}

	/// <summary>
	/// Tests changing the return type of a method.
	/// </summary>
	[TestMethod]
	public async Task MethodSignatureChange()
	{
		using var compiler = new FastPathTestCompiler( "MethodSignatureChange.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );
	}

	private async Task PropertyGetterSetterTest( string packageFileName )
	{
		using var compiler = new FastPathTestCompiler( packageFileName );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, "Hello World!" ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 1, result.ChangedMethods.Length );

		Assert.AreEqual( 0, TestProgram( program, "Hello Blorld!" ) );
	}

	/// <summary>
	/// Tests the block body of a property getter changing.
	/// </summary>
	[TestMethod]
	public Task PropertyGetterChange1()
	{
		return PropertyGetterSetterTest( "PropertyGetter1.cs" );
	}

	/// <summary>
	/// Tests the expression body of a property getter changing.
	/// </summary>
	[TestMethod]
	public Task PropertyGetterChange2()
	{
		return PropertyGetterSetterTest( "PropertyGetter2.cs" );
	}

	/// <summary>
	/// Tests the block body of a property setter changing.
	/// </summary>
	[TestMethod]
	public Task PropertySetterChange1()
	{
		return PropertyGetterSetterTest( "PropertySetter1.cs" );
	}

	/// <summary>
	/// Tests the expression body of a property setter changing. This case does NOT take the
	/// IL fast path yet (edge case where text before a ; is added in an AccessorDeclaration) -
	/// pinned as unsupported so gaining support shows up as a test failure to upgrade.
	/// </summary>
	[TestMethod]
	public async Task PropertySetterChange2()
	{
		using var compiler = new FastPathTestCompiler( "PropertySetter2.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, "Hello World!" ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );
	}

	/// <summary>
	/// Modifying a statement inside a nested Block.
	/// </summary>
	[TestMethod]
	public async Task NestedBlock()
	{
		using var compiler = new FastPathTestCompiler( "NestedBlocks.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, Enumerable.Range( 0, 10 ).Select( x => $"Hello {x}" ) ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 1, result.ChangedMethods.Length );

		Assert.AreEqual( 0, TestProgram( program, Enumerable.Range( 0, 10 ).Select( x => $"Hello {x + 1}" ) ) );
	}

	/// <summary>
	/// Stuff like <c>typeof(T)</c> should reference the original assembly, not the method body providing assembly.
	/// </summary>
	[TestMethod]
	public async Task Reflection()
	{
		using var compiler = new FastPathTestCompiler( "Reflection.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, result.Assembly.FullName ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 1, result.ChangedMethods.Length );
		Assert.AreNotEqual( result.Assembly.FullName, result.MethodBodyAssembly.FullName );

		Assert.AreEqual( 1, TestProgram( program, result.Assembly.FullName ) );
	}

	/// <summary>
	/// References to static members in changed method bodies should be updated to point to the original assembly.
	/// </summary>
	[TestMethod]
	public async Task StaticReference()
	{
		using var compiler = new FastPathTestCompiler( "StaticReference.cs" );

		var result = await compiler.BuildAsync( version: 2 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, "Hello World" ) );

		result = await compiler.BuildAsync( version: 3 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 1, result.ChangedMethods.Length );

		Assert.AreEqual( 1, TestProgram( program, "Hello Blorld!" ) );
	}

	/// <summary>
	/// Make sure changed methods with parameter / return types defined in the changed module are replaced without issues.
	/// </summary>
	[TestMethod]
	public async Task SignatureTypes()
	{
		using var compiler = new FastPathTestCompiler( "SignatureTypes.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 1, result.ChangedMethods.Length );

		Assert.AreEqual( 1, TestProgram( program ) );
	}

	/// <summary>
	/// Changes that affect generated type definitions aren't supported.
	/// </summary>
	[TestMethod]
	public async Task CompilerGenerated1()
	{
		using var compiler = new FastPathTestCompiler( "CompilerGenerated.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsFalse( result.ILHotloadSupported );
	}

	/// <summary>
	/// Changes inside lambda expression bodies should be supported, if they don't change which variables are captured.
	/// </summary>
	[TestMethod]
	public async Task CompilerGenerated2()
	{
		using var compiler = new FastPathTestCompiler( "CompilerGenerated" );

		var result = await compiler.BuildAsync( version: 2 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, "0, 1, 2, 3, 4, 5, 6, 7, 8, 9" ) );

		result = await compiler.BuildAsync( version: 3 );

		Assert.IsTrue( result.ILHotloadSupported );

		Assert.AreEqual( 0, TestProgram( program, "1, 2, 3, 4, 5, 6, 7, 8, 9, 10" ) );
	}

	/// <summary>
	/// Changes inside async method bodies should be supported, if they don't change the generated state machine type.
	/// </summary>
	[TestMethod]
	public async Task CompilerGenerated3()
	{
		using var compiler = new FastPathTestCompiler( "CompilerGenerated.cs" );

		var result = await compiler.BuildAsync( version: 4 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, "Hello World!" ) );

		result = await compiler.BuildAsync( version: 5 );

		Assert.IsTrue( result.ILHotloadSupported );

		Assert.AreEqual( 0, TestProgram( program, "Hello Blorld!" ) );
	}

	/// <summary>
	/// Make sure that references to generic types involving user types are updated correctly.
	/// </summary>
	[TestMethod]
	public async Task GenericReference1()
	{
		using var compiler = new FastPathTestCompiler( "GenericReference.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );

		var program = result.CreateProgram();

		Assert.AreEqual( 123, TestProgram( program, "Count: 0" ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );

		Assert.AreEqual( 456, TestProgram( program, "Count: 1" ) );
	}

	[TestMethod]
	public async Task GenericReference2()
	{
		using var compiler = new FastPathTestCompiler( "GenericReference.cs" );

		var result = await compiler.BuildAsync( version: 3 );

		Assert.IsFalse( result.ILHotloadSupported );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, "Count: 0" ) );

		result = await compiler.BuildAsync( version: 4 );

		Assert.IsTrue( result.ILHotloadSupported );

		Assert.AreEqual( 1, TestProgram( program, "Count: 1" ) );

		result = await compiler.BuildAsync( version: 3 );

		Assert.IsTrue( result.ILHotloadSupported );

		Assert.AreEqual( 0, TestProgram( program, "Count: 1" ) );
	}

	/// <summary>
	/// Calls to <see cref="Assembly.GetExecutingAssembly"/> should return the original assembly.
	/// </summary>
	[TestMethod]
	public async Task ExecutingAssembly()
	{
		using var compiler = new FastPathTestCompiler( "ExecutingAssembly.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, result.Assembly.FullName ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );

		Assert.AreEqual( 1, TestProgram( program, result.Assembly.FullName ) );
		Assert.AreNotEqual( result.Assembly.FullName, result.MethodBodyAssembly.FullName );
	}

	/// <summary>
	/// Method body changes in a generic readonly struct do NOT take the IL hotload fast path -
	/// they fall back to a full hotload. Pinned as unsupported so gaining fast-path support
	/// shows up as a test failure to upgrade.
	/// </summary>
	[TestMethod]
	public async Task GenericReadonlyStruct()
	{
		using var compiler = new FastPathTestCompiler( "GenericReadonlyStruct.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, "hello", "world" ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsFalse( result.ILHotloadSupported );
	}

	[TestMethod]
	public async Task MixedGenericMethod()
	{
		using var compiler = new FastPathTestCompiler( "MixedGenericMethod.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, "Hello 53 World" ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );

		Assert.AreEqual( 0, TestProgram( program, "Hello 35 Blorld" ) );
	}

	/// <summary>
	/// Reference an array of a generic type.
	/// </summary>
	[TestMethod]
	public async Task ResolveGeneric()
	{
		using var compiler = new FastPathTestCompiler( "ResolveGeneric.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.AreEqual( 1, TestProgram( program ) );
	}

	/// <summary>
	/// Some compiler generated methods wouldn't resolve.
	/// </summary>
	[TestMethod]
	public async Task ResolveLambdaMethod()
	{
		using var compiler = new FastPathTestCompiler( "ResolveLambdaMethod.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsFalse( result.ILHotloadSupported );
	}

	/// <summary>
	/// Some generic methods wouldn't resolve.
	/// </summary>
	[TestMethod]
	public async Task ResolveGenericTupleMethod()
	{
		using var compiler = new FastPathTestCompiler( "ResolveGenericTupleMethod.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );

		Assert.AreEqual( 1, TestProgram( program ) );
	}

	/// <summary>
	/// SourceLocation attribute parameters should be ignored.
	/// </summary>
	[TestMethod]
	public async Task SourceLocation()
	{
		using var compiler = new FastPathTestCompiler( "SourceLocation.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 1, result.ChangedMethods.Length );

		Assert.AreEqual( 1, TestProgram( program ) );
	}

	/// <summary>
	/// Commenting out a line shouldn't count as a trivial change.
	/// </summary>
	[TestMethod]
	public async Task CommentOutLine()
	{
		using var compiler = new FastPathTestCompiler( "CommentLine.cs" );

		var result = await compiler.BuildAsync( version: 1 );

		Assert.IsFalse( result.ILHotloadSupported );
		Assert.AreEqual( 0, result.ChangedMethods.Length );

		var program = result.CreateProgram();

		Assert.AreEqual( 0, TestProgram( program, "Hello, World!" ) );

		result = await compiler.BuildAsync( version: 2 );

		Assert.IsTrue( result.ILHotloadSupported );
		Assert.AreEqual( 1, result.ChangedMethods.Length );

		Assert.AreEqual( 0, TestProgram( program ) );
	}
}
