using Sandbox.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ScriptingTests;

/// <summary>
/// Standard script callbacks preserve type, member and execution boundaries.
/// </summary>
[TestClass, DoNotParallelize]
public class ScriptCallbackTests
{
	GlobalContext previous;
	GlobalContext previousGame;

	/// <summary>
	/// Uses the shared catalog in an isolated scripting context.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		previousGame = GlobalContext.Game;
		GlobalContext.Current = GlobalContext.Game = new GlobalContext { TypeLibrary = SharedTypeLibrary.ScriptingWithSystem };
	}

	/// <summary>
	/// Releases the context and restores the engine globals.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.Shutdown();
		GlobalContext.Current = previous;
		GlobalContext.Game = previousGame;
	}

	/// <summary>
	/// Short and qualified callback names support captures, arguments, results and script methods.
	/// </summary>
	[TestMethod]
	[DataRow( "var result=0; Action callback=()=>{result=42;}; callback(); return result;" )]
	[DataRow( "var result=0; Action<int> callback=value=>{result=value;}; callback(42); return result;" )]
	[DataRow( "var result=0; System.Action<int> callback=value=>{result=value;}; callback.Invoke(42); return result;" )]
	[DataRow( "var value=41; Func<int> callback=()=>++value; return callback();" )]
	[DataRow( "System.Func<int> callback=()=>42; return callback.Invoke();" )]
	[DataRow( "Func<int,int> callback=value=>value+1; return callback(41);" )]
	[DataRow( "System.Func<int,int> callback=value=>value+1; return callback.Invoke(41);" )]
	[DataRow( "class Helper { public void Run(Action<int> callback) { callback(42); } } var result=0; new Helper().Run(value=>{result=value;}); return result;" )]
	[DataRow( "class Helper { public int Run(Func<int> callback) { return callback(); } } return new Helper().Run(()=>42);" )]
	[DataRow( "class Helper { public int Run(Func<int,int> callback) { return callback(41); } } return new Helper().Run(value=>value+1);" )]
	[DataRow( "Func<Func<int>> factory=()=>()=>42; var callback=factory(); return callback();" )]
	[DataRow( "Func<Vector3> callback=()=>new Vector3(42,0,0); return (int)callback().x;" )]
	[DataRow( "Func<object> callback=()=>\"value\"; return callback().Equals(\"value\") ? 42 : 0;" )]
	[DataRow( "Func<string> source=()=>\"value\"; Func<object> callback=source; return callback().Equals(\"value\") ? 42 : 0;" )]
	[DataRow( "var result=0; Action<object> source=value=>{result=42;}; Action<string> callback=source; callback(\"value\"); return result;" )]
	public void ScriptsUseApprovedCallbackContracts( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		Assert.AreEqual( 42, script.RunAndReturn<int>(), script.LastError?.Message );
	}

	/// <summary>
	/// The resolver exposes Invoke without constructors or delegate reflection members.
	/// </summary>
	[TestMethod]
	[DataRow( typeof( Action ) )]
	[DataRow( typeof( Action<int> ) )]
	[DataRow( typeof( Func<int> ) )]
	[DataRow( typeof( Func<int, string> ) )]
	public void CallbackMetadataContainsOnlyApprovedMembers( Type type )
	{
		var members = new List<MemberInfo>();
		new TypeLibraryScriptResolver( Game.TypeLibrary ).GetMembers( type, members );
		Assert.IsTrue( members.OfType<MethodInfo>().Any( x => x.Name == "Invoke" ) );
		Assert.IsFalse( members.OfType<ConstructorInfo>().Any() );
		Assert.IsFalse( members.Any( x => x.Name is "Method" or "Target" or "DynamicInvoke" or "CreateDelegate" or "GetInvocationList" or "GetType" or "BeginInvoke" or "EndInvoke" ) );
		Assert.IsNull( Game.TypeLibrary.GetType( type ) );
	}

	/// <summary>
	/// Open, unapproved and byref-like delegate signatures are not callable.
	/// </summary>
	[TestMethod]
	public void UnsupportedCallbackSignaturesHaveNoInvoke()
	{
		foreach ( var type in new[] { typeof( Action<> ), typeof( Func<> ), typeof( Func<,> ), typeof( Action<int, int> ), typeof( Func<int, int, int> ), typeof( Predicate<int> ), typeof( Action<Span<int>> ), typeof( Func<Span<int>> ), typeof( Func<Span<int>, int> ), typeof( Func<int, Span<int>> ) } )
		{
			var members = new List<MemberInfo>();
			new TypeLibraryScriptResolver( Game.TypeLibrary ).GetMembers( type, members );
			Assert.IsFalse( members.Any( x => x.Name == "Invoke" ), type.ToString() );
		}
	}

	/// <summary>
	/// Callback signatures do not expose unknown types or approve incompatible lambda shapes.
	/// </summary>
	[TestMethod]
	[DataRow( "Func<System.IO.FileInfo> callback = null;" )]
	[DataRow( "Func<MissingType> callback = null;" )]
	[DataRow( "Func<List<System.Reflection.MethodInfo>> callback = null;" )]
	[DataRow( "Action<int,int> callback = (a,b)=>{};" )]
	[DataRow( "Func<int,int,int> callback = (a,b)=>a+b;" )]
	[DataRow( "System.Func<int> callback=()=>\"wrong\"; return callback();" )]
	[DataRow( "System.Action<int> callback=()=>{}; callback(1);" )]
	[DataRow( "System.Func<int> callback=()=>1; return callback.Method;" )]
	[DataRow( "System.Func<int,int> callback=x=>x; return callback.Target;" )]
	[DataRow( "System.Action<int> callback=x=>{}; callback.DynamicInvoke(1);" )]
	[DataRow( "return new System.Func<int>(null,0);" )]
	[DataRow( "Func<object> source=()=>new object(); Func<string> callback=(Func<string>)source; return callback();" )]
	[DataRow( "Func<int> source=()=>42; Func<object> callback=(Func<object>)(object)source; return callback();" )]
	[DataRow( "Action<string> source=value=>{}; Action<object> callback=(Action<object>)source; callback(new object());" )]
	public void UnsupportedCallbackUsesFail( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		Assert.IsFalse( script.Run(), source );
		Assert.IsNotNull( script.LastError );
	}

	/// <summary>
	/// Typed callback bodies and nested callbacks consume the original execution budget.
	/// </summary>
	[TestMethod]
	[DataRow( "Action<int> callback=count=>{for(var i=0;i<count;i++) {}}; callback(10000);" )]
	[DataRow( "Func<int> callback=()=>{var sum=0; for(var i=0;i<10000;i++){sum++;} return sum;}; return callback();" )]
	[DataRow( "Func<int,int> callback=count=>{var sum=0; for(var i=0;i<count;i++){sum++;} return sum;}; return callback(10000);" )]
	[DataRow( "Func<int,int> inner=x=>x+1; Action<int> outer=count=>{for(var i=0;i<count;i++){inner(i);}}; outer(10000);" )]
	public void CallbacksRespectInstructionBudgets( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		Assert.IsFalse( script.Run( instructionLimit: 100 ) );
		Assert.AreEqual( "SCR_BUDGET", script.LastError?.Code );
	}

	/// <summary>
	/// Null callbacks report script errors instead of escaping to the host.
	/// </summary>
	[TestMethod]
	[DataRow( "Action<int> callback=null; callback(1);" )]
	[DataRow( "Func<int> callback=null; return callback();" )]
	[DataRow( "Func<int,int> callback=null; return callback(1);" )]
	public void NullCallbacksReportScriptErrors( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		Assert.IsFalse( script.Run() );
		Assert.AreEqual( "SCR_NULL", script.LastError?.Code );
	}

	/// <summary>
	/// Callback results support implicit and explicit boxing through the engine resolver.
	/// </summary>
	[TestMethod]
	[DataRow( "Func<object> callback=()=>42; return callback();" )]
	[DataRow( "Func<object> callback=()=>(object)42; return callback();" )]
	[DataRow( "Func<object> callback=()=>new Vector3(42,0,0); return (int)((Vector3)callback()).x;" )]
	public void CallbackResultsSupportBoxing( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		Assert.AreEqual( 42, script.RunAndReturn<object>(), script.LastError?.Message );
	}

	private sealed class OpaqueResult
	{
		/// <summary>
		/// An ordinary CLR member that the script catalog does not expose.
		/// </summary>
		public int Secret => 42;
	}

	/// <summary>
	/// Calling a supplied factory does not expose the members of its unregistered result type.
	/// </summary>
	[TestMethod]
	public void HostCallbacksDoNotExposeOpaqueResultMembers()
	{
		Func<OpaqueResult> factory = () => new OpaqueResult();
		Assert.IsNull( Game.TypeLibrary.GetType( typeof( OpaqueResult ) ) );
		Assert.IsNull( Game.TypeLibrary.GetType( typeof( Delegate ) ) );
		Assert.IsNull( Game.TypeLibrary.GetType( typeof( MulticastDelegate ) ) );
		var allowed = Game.Scripting.CreateScript( "return factory() != null;" );
		allowed.SetArgument( "factory", factory );
		Assert.IsTrue( allowed.RunAndReturn<bool>(), allowed.LastError?.Message );

		foreach ( var source in new[] { "return factory().Secret;", "return factory().GetType();" } )
		{
			var denied = Game.Scripting.CreateScript( source );
			denied.SetArgument( "factory", factory );
			Assert.IsFalse( denied.Run(), source );
			Assert.AreEqual( "SCR_BIND", denied.LastError?.Code );
		}
	}

	/// <summary>
	/// Host-retained callbacks keep an instruction budget when invoked after the creating script returns.
	/// </summary>
	[TestMethod]
	[DataRow( "Action<int> callback=value=>{for(var i=0;i<10000;i++){}}; return callback;" )]
	[DataRow( "Func<int> callback=()=>{var sum=0;for(var i=0;i<10000;i++){sum++;}return sum;}; return callback;" )]
	[DataRow( "Func<int,int> callback=value=>{var sum=0;for(var i=0;i<10000;i++){sum++;}return sum;}; return callback;" )]
	public void DeferredCallbacksKeepInstructionBudgets( string source )
	{
		// A deferred call gets a fresh module budget, rather than the small per-run budget used
		// to create it. Keep the loop finite, but above that module ceiling even at one charge per iteration.
		source = source.Replace( "10000", (ScriptSystem.InstructionLimit + 1).ToString( System.Globalization.CultureInfo.InvariantCulture ) );
		var script = Game.Scripting.CreateScript( source );
		var callback = (Delegate)script.RunAndReturn<object>( instructionLimit: 100 );
		var error = CallbackError( callback );
		Assert.IsInstanceOfType<Breen.ScriptException>( error );
		Assert.AreEqual( "SCR_BUDGET", ((Breen.ScriptException)error).Code );
	}

	/// <summary>
	/// Retained callbacks, including returned inner callbacks, retain thread, context and lifetime checks.
	/// </summary>
	[TestMethod]
	[DataRow( "Action<int> callback=value=>{}; return callback;" )]
	[DataRow( "Func<int> callback=()=>42; return callback;" )]
	[DataRow( "Func<int,int> callback=value=>value+1; return callback;" )]
	[DataRow( "Func<Func<int>> factory=()=>()=>42; return factory();" )]
	public void RetainedCallbacksRespectTheirOwner( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		var callback = (Delegate)script.RunAndReturn<object>();
		Assert.IsNull( CallbackError( callback ) );

		var owner = GlobalContext.Current;
		var foreign = new GlobalContext { TypeLibrary = Game.TypeLibrary };
		try
		{
			GlobalContext.Current = foreign;
			var error = CallbackError( callback );
			Assert.IsNotNull( error );
			StringAssert.Contains( error.Message, "owning game context" );
		}
		finally
		{
			GlobalContext.Current = owner;
			foreign.Shutdown();
		}

		var threadError = Task.Run( () => CallbackError( callback ) ).GetAwaiter().GetResult();
		Assert.IsNotNull( threadError );
		StringAssert.Contains( threadError.Message, "creating thread" );

		owner.Shutdown();
		Assert.IsInstanceOfType<ObjectDisposedException>( CallbackError( callback ) );
	}

	private static Exception CallbackError( Delegate callback )
	{
		try
		{
			switch ( callback )
			{
				case Action<int> action:
					action( 42 );
					break;
				case Func<int> function:
					_ = function();
					break;
				case Func<int, int> transform:
					_ = transform( 41 );
					break;
				default:
					throw new NotSupportedException( callback.GetType().ToString() );
			}
			return null;
		}
		catch ( Exception exception )
		{
			return exception;
		}
	}
}
