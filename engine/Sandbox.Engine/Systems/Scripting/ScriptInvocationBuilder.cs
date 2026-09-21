namespace Sandbox;

/// <summary>
/// Builds Breen's invocation only at execution time. Traversal preserves argument order and
/// keeps every value on the stack, including ref structs, without exposing Breen in the public API.
/// </summary>
internal static class ScriptInvocationBuilder
{
	internal interface IContinuation<TResult>
	{
		TResult Apply<TInputs>( Breen.ScriptInvocation<TInputs> invocation ) where TInputs : Breen.IScriptInputs, allows ref struct;
	}

	/// <summary>
	/// Appends this argument after the earlier arguments, then passes the invocation onward.
	/// Generic continuations avoid boxing, delegates and heap storage for borrowed values.
	/// </summary>
	internal readonly ref struct Append<T, TResult, TNext> : IContinuation<TResult>
		where T : allows ref struct
		where TNext : IContinuation<TResult>, allows ref struct
	{
		readonly string name;
		readonly T value;
		readonly TNext next;

		internal Append( string name, T value, TNext next )
		{
			this.name = name;
			this.value = value;
			this.next = next;
		}

		public TResult Apply<TInputs>( Breen.ScriptInvocation<TInputs> invocation ) where TInputs : Breen.IScriptInputs, allows ref struct
			=> next.Apply( invocation.With( name, value ) );
	}

	internal static void CheckArgument<T>( T invocation, string name ) where T : IScriptInvocation, allows ref struct
		=> invocation.Target.CheckArgument( name );

	internal static bool Compile<T>( T invocation ) where T : IScriptInvocation, allows ref struct
		=> invocation.Apply<bool, CompileOperation>( new( invocation.Target ) );

	internal static bool Run<T>( T invocation, int limit ) where T : IScriptInvocation, allows ref struct
		=> invocation.Apply<bool, RunOperation>( new( invocation.Target, limit ) );

	internal static TResult RunAndReturn<TResult, T>( T invocation, int limit ) where T : IScriptInvocation, allows ref struct
		=> invocation.Apply<TResult, ReturnOperation<TResult>>( new( invocation.Target, limit ) );

	readonly struct CompileOperation( Script script ) : IContinuation<bool>
	{
		public bool Apply<TInputs>( Breen.ScriptInvocation<TInputs> invocation ) where TInputs : Breen.IScriptInputs, allows ref struct
			=> script.Compile( invocation );
	}

	readonly struct RunOperation( Script script, int limit ) : IContinuation<bool>
	{
		public bool Apply<TInputs>( Breen.ScriptInvocation<TInputs> invocation ) where TInputs : Breen.IScriptInputs, allows ref struct
			=> script.Invoke<object, TInputs>( false, out _, invocation, limit );
	}

	readonly struct ReturnOperation<TResult>( Script script, int limit ) : IContinuation<TResult>
	{
		public TResult Apply<TInputs>( Breen.ScriptInvocation<TInputs> invocation ) where TInputs : Breen.IScriptInputs, allows ref struct
		{
			script.Invoke<TResult, TInputs>( true, out var value, invocation, limit );
			return value;
		}
	}
}
