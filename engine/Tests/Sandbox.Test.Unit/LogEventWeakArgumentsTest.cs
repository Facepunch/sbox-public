using System;
using System.Runtime.CompilerServices;

namespace EngineTests;

/// <summary>
/// Log sinks keep thousands of events. A stored event must not keep what it logged alive -
/// a component logged as context would otherwise keep its whole closed scene in memory.
/// </summary>
[TestClass]
public class LogEventWeakArgumentsTest
{
	sealed class Logged { }

	[MethodImpl( MethodImplOptions.NoInlining )]
	static (LogEvent Event, WeakReference Probe) StoreEventLogging()
	{
		var logged = new Logged();
		var e = new LogEvent { Message = "context", Arguments = [logged, "text", 42] }.WithWeakArguments();

		Assert.AreSame( logged, e.GetArgument( 0 ), "A live logged object is still reachable" );
		return (e, new WeakReference( logged ));
	}

	[TestMethod]
	public void StoredEventDoesNotKeepLoggedObjectAlive()
	{
		var (stored, probe) = StoreEventLogging();

		for ( int i = 0; i < 10 && probe.IsAlive; i++ )
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}

		Assert.IsFalse( probe.IsAlive, "The stored event kept the logged object alive" );
		Assert.IsNull( stored.GetArgument( 0 ) );
		Assert.AreEqual( "text", stored.GetArgument( 1 ), "Strings are kept as they are" );
		Assert.AreEqual( 42, stored.GetArgument( 2 ), "Value types are kept as they are" );
		Assert.IsNull( stored.GetArgument( 3 ), "Out of range reads nothing" );
	}
}
