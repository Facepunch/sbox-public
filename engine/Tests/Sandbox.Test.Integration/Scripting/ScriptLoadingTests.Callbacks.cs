using System;

namespace ScriptingTests;

/// <summary>
/// Published script callbacks preserve lifetime boundaries across reloads.
/// </summary>
public partial class ScriptLoadingTests
{
	/// <summary>
	/// Published generic callback signatures invoke correctly and retire captured state on reload.
	/// </summary>
	[TestMethod]
	[DataRow( "Action<int>", "value => { Count += value; }" )]
	[DataRow( "Func<int>", "() => ++Count" )]
	[DataRow( "Func<int,int>", "value => Count += value" )]
	public void GenericCallbacksRespectScriptReload( string callbackType, string expression )
	{
		var source = $$"""
			/// <summary>
			/// Creates a stateful callback for a script reload check.
			/// </summary>
			public class GenericCallbackProbe
			{
				/// <summary>
				/// State captured by the callback.
				/// </summary>
				public int Count { get; set; } = 1;
				/// <summary>
				/// Captures this instance in a typed callback.
				/// </summary>
				public System.{{callbackType}} Capture() { return {{expression}}; }
			}
			""";
		var loaded = Game.Scripting.LoadFile( "generic-callback.csx", source );
		Assert.IsTrue( loaded.Success, Game.Scripting.LastError?.Message );
		var callback = (Delegate)Game.Scripting.CreateScript( "return new GenericCallbackProbe().Capture();" ).RunAndReturn<object>();
		InvokeCallback( callback );

		var reloaded = Game.Scripting.LoadFile( "generic-callback.csx", source.Replace( "= 1;", "= 2;" ) );
		Assert.IsTrue( reloaded.Success, Game.Scripting.LastError?.Message );
		var exception = Assert.ThrowsException<Breen.ScriptException>( () => InvokeCallback( callback ) );
		Assert.AreEqual( "SCR_LAMBDA", exception.Code );

		var replacement = (Delegate)Game.Scripting.CreateScript( "return new GenericCallbackProbe().Capture();" ).RunAndReturn<object>();
		InvokeCallback( replacement );
	}

	private static void InvokeCallback( Delegate callback )
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
	}
}
