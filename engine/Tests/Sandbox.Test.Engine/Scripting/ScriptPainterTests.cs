using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Rendering;

namespace ScriptingTests;

[TestClass, DoNotParallelize]
public partial class ScriptPainterTests
{
	GlobalContext previous;

	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		var types = new TypeLibrary();
		types.AddIntrinsicTypes();
		types.AddAssembly( typeof( Painter ).Assembly, false );
		types.AddAssembly( typeof( Vector2 ).Assembly, false );
		GlobalContext.Current = new GlobalContext { TypeLibrary = types };
	}

	[TestCleanup]
	public void Cleanup() => GlobalContext.Current = previous;

	[TestMethod]
	public void TypeNamesTakePriorityOverEditorAliases()
	{
		var resolver = new TypeLibraryScriptResolver( GlobalContext.Current.TypeLibrary );
		Assert.AreEqual( typeof( Color ), resolver.ResolveType( "Color" ) );
		Assert.AreEqual( typeof( Painter ), resolver.ResolveType( "Sandbox.Painter" ) );
		Assert.AreEqual( typeof( Sandbox.Resources.ColorTextureGenerator ), resolver.ResolveType( "ColorTextureGenerator" ) );
	}

	[TestMethod]
	public void UnusedHostInputsAreNotScriptArguments()
	{
		const string source = "return painter.Bounds.Width;";
		var script = Game.Scripting.CreateScript( source );
		var analysis = Game.Scripting.Analyze( source,
			[new Script.Input( "painter", typeof( Painter ) ), new Script.Input( "size", typeof( Vector2 ) ),
				new Script.Input( "time", typeof( float ) )] );
		Assert.IsTrue( analysis.Inputs.Any( x => x.Name == "size" ) );
		Assert.IsTrue( script.HasArgument( "painter" ) );
		Assert.IsFalse( script.HasArgument( "size" ) );
		Assert.IsFalse( script.HasArgument( "time" ) );
		Assert.IsFalse( script.HasArgument( "Painter" ) );
		Assert.IsFalse( script.HasArgument( null ) );

		var context = new Painter.Context( new CommandList() );
		using var painter = context.Begin( new Rect( 0, 0, 400, 300 ) );
		Assert.AreEqual( 400.0f, script.With( "painter", painter ).RunAndReturn<float>(), script.LastError?.Message );
		Assert.IsTrue( script.HasArgument( "painter" ) );
	}

	[TestMethod]
	public void ScriptUsesTheSuppliedPainterAndRejectsExpiredCopies()
	{
		var context = new Painter.Context( new CommandList() );
		var painter = context.Begin( new Rect( 0, 0, 400, 300 ) );
		var script = Game.Scripting.CreateScript( "painter.Fill = Fill.Solid( Color.Red ); painter.Translate(10.0f, 20.0f); return painter.Bounds.Width;" );
		try
		{
			Assert.AreEqual( 400.0f, script.With( "painter", painter ).RunAndReturn<float>(), script.LastError?.Message );
			Assert.IsNull( script.LastError );
			Assert.AreEqual( Fill.Solid( Color.Red ), painter.Fill );
			Assert.AreEqual( 10.0f, painter.Transform.M41 );
			Assert.AreEqual( 20.0f, painter.Transform.M42 );
		}
		finally { painter.Dispose(); }

		using var next = context.Begin( default );
		Assert.IsFalse( script.With( "painter", painter ).Run() );
		Assert.IsNotNull( script.LastError );
		Assert.AreEqual( Fill.None, next.Fill );
	}

	[TestMethod]
	public void ScriptRecordsShapesInTheSuppliedCommandList()
	{
		var context = new Painter.Context( new CommandList() );
		var painter = context.Begin( new Rect( 0, 0, 400, 300 ) );
		try
		{
			const string source = """
				painter.Fill = Color.Cyan;
				painter.Rect(new Rect(30, 30, 160, 80));
				painter.Fill = Fill.Solid(Color.Orange);
				painter.Circle(new Vector2(200, 150), 45.0f);
				painter.Arc(20, 100.0f, -45.0f, 80.0f);
				painter.Stroke = new Stroke(Fill.Solid(Color.White), 3.0f);
				painter.Line(new Vector2(30, 260), new Vector2(370, 260));
				""";
			var analysis = Game.Scripting.Analyze( source, [new Script.Input( "painter", typeof( Painter ) )] );
			Assert.IsFalse( analysis.Diagnostics.Any( x => x.Severity == Script.DiagnosticSeverity.Error ),
				string.Join( "\n", analysis.Diagnostics.Select( x => x.Message ) ) );
			var script = Game.Scripting.CreateScript( source );
			Assert.IsTrue( script.With( "painter", painter ).Run(), script.LastError?.Message );
			Assert.IsTrue( context.Batcher.Instances.Count >= 3 );
			Assert.AreEqual( new Vector4( 30, 30, 160, 80 ), context.Batcher.Instances[0].Rect );
		}
		finally
		{
			context.CommandList.Reset();
			context.Batcher.Dispose();
		}
	}

	[TestMethod]
	[DataRow( "return painter;" )]
	[DataRow( "painter.Fill = Color.Red; return painter;" )]
	[DataRow( "object saved = painter;" )]
	[DataRow( "static var saved = painter;" )]
	public void ScriptsCannotRetainPainter( string source )
	{
		Assert.IsTrue( typeof( Painter ).IsByRefLike );
		var context = new Painter.Context( new CommandList() );
		using var painter = context.Begin( default );
		var script = Game.Scripting.CreateScript( source );
		Assert.IsFalse( script.With( "painter", painter ).Run() );
		Assert.IsNotNull( script.LastError );
		Assert.AreEqual( Fill.None, painter.Fill );
	}

	[TestMethod]
	[DataRow( "painter.Arc(new Vector2(20, 20), 100.0f, -45.0f, 80.0f);" )]
	[DataRow( "painter.Arc(20, 100.0f, -45.0f, 80.0f);" )]
	public void ArcAcceptsVectorAndScalarCenters( string source )
	{
		var context = new Painter.Context( new CommandList() );
		var painter = context.Begin( new Rect( 0, 0, 400, 300 ) );
		try
		{
			var analysis = Game.Scripting.Analyze( source, [new Script.Input( "painter", typeof( Painter ) )] );
			Assert.IsFalse( analysis.Diagnostics.Any( x => x.Severity == Script.DiagnosticSeverity.Error ),
				string.Join( "\n", analysis.Diagnostics.Select( x => x.Message ) ) );
			var script = Game.Scripting.CreateScript( source );
			painter.Stroke = new Stroke( Color.White, 3 );
			Assert.IsTrue( script.With( "painter", painter ).Run(), script.LastError?.Message );
			Assert.IsTrue( context.Batcher.Instances.Count > 0 );
		}
		finally
		{
			context.CommandList.Reset();
			context.Batcher.Dispose();
		}
	}

	[TestMethod]
	public void CompileChecksInputsWithoutDrawing()
	{
		const string source = "var unused = missing; painter.Opacity = 0.5f;";
		var analysis = Game.Scripting.Analyze( source, [new Script.Input( "painter", typeof( Painter ) )] );
		Assert.IsFalse( analysis.Diagnostics.Any( x => x.Severity == Script.DiagnosticSeverity.Error ) );
		var script = Game.Scripting.CreateScript( source );
		Assert.IsFalse( script.With( "painter", default( Painter ) ).Compile() );
		Assert.IsNotNull( script.LastError );
		script.SetArgument( "missing", 0.5f );
		Assert.IsTrue( script.With( "painter", default( Painter ) ).Compile(), script.LastError?.Message );
		var context = new Painter.Context( new CommandList() );
		using var painter = context.Begin( default );
		Assert.IsTrue( script.With( "painter", painter ).Run(), script.LastError?.Message );
		Assert.AreEqual( 0.5f, painter.Opacity );
	}

	[TestMethod]
	public void CompletionDistinguishesTimeTypeFromTimeInput()
	{
		var analysis = Game.Scripting.Analyze( "Time", [new Script.Input( "time", typeof( float ) )] );
		var items = analysis.GetCompletions( 4 ).Items;
		Assert.AreEqual( "Time", items[0].Label );
		Assert.IsTrue( items.Any( item => item.Label == "time" ) );
	}

	[TestMethod]
	public void PainterMembersAreAvailableToEditorAnalysis()
	{
		var analysis = Game.Scripting.Analyze( "painter.", [new Script.Input( "painter", typeof( Painter ) )] );
		var members = analysis.GetCompletions( analysis.Source.Length ).Items;
		Assert.IsTrue( members.Any( x => x.Label == "Circle" ), string.Join( ", ", members.Select( x => x.Label ) ) );
		Assert.IsTrue( members.Any( x => x.Label == "Rect" ) );
		Assert.IsTrue( members.Any( x => x.Label == "Fill" ) );
	}
}
