using System.Threading;

namespace Sandbox.Engine;

internal partial class GlobalContext
{
	/// <summary>
	/// Should rarely have to get called, game scope is implicit. Will need to be called if we're
	/// in the menu scope, and have to call something in the game scope.
	/// </summary>
	public static GlobalContextScope GameScope() => new( Game, clearAsyncContext: true );

	/// <summary>
	/// Should only be called at a really high level, when doing menu stuff
	/// </summary>
	public static GlobalContextScope MenuScope() => new( Menu );

	// Setting an AsyncLocal makes a new ExecutionContext every time, and the menu and game scopes
	// are entered several times a frame. Execution contexts are immutable, so entering the same
	// context from the same execution context always produces an equal one - keep the last one
	// made for each target and switch to it instead of making another.
	[ThreadStatic] static GlobalContext t_contextA, t_contextB;
	[ThreadStatic] static ExecutionContext t_fromA, t_toA, t_fromB, t_toB;
	[ThreadStatic] static bool t_replaceB;

	public struct GlobalContextScope : IDisposable
	{
		GlobalContext previous;
		ExecutionContext _outer;
		ExecutionContext _inner;

		public GlobalContextScope( GlobalContext context, bool clearAsyncContext = false )
		{
			previous = Current;

			// Null when flow is suppressed, in which case we just set the value
			_outer = ExecutionContext.Capture();

			if ( _outer is not null && TryGetCached( context, _outer ) is { } cached )
			{
				ExecutionContext.Restore( cached );
			}
			else
			{
				_current.Value = context;

				if ( _outer is not null )
					SetCached( context, _outer, ExecutionContext.Capture() );
			}

			_inner = _outer is null ? null : ExecutionContext.Capture();
		}

		public void Dispose()
		{
			// Nothing else changed an AsyncLocal inside the scope, so the execution context from
			// before it is exactly what setting the previous context back would make
			if ( _inner is not null && ExecutionContext.Capture() == _inner )
			{
				ExecutionContext.Restore( _outer );
				return;
			}

			Current = previous;
		}

		static ExecutionContext TryGetCached( GlobalContext context, ExecutionContext from )
		{
			if ( t_contextA == context && t_fromA == from ) return t_toA;
			if ( t_contextB == context && t_fromB == from ) return t_toB;
			return null;
		}

		// One entry per context, the menu and game scopes being the two that matter
		static void SetCached( GlobalContext context, ExecutionContext from, ExecutionContext to )
		{
			if ( context is null || to is null ) return;

			bool useB = t_contextA == context ? false : t_contextB == context || t_replaceB;
			if ( useB ) { t_contextB = context; t_fromB = from; t_toB = to; }
			else { t_contextA = context; t_fromA = from; t_toA = to; }

			t_replaceB = !useB;
		}
	}
}
