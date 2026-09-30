namespace Sandbox;

/// <summary>
/// A stack-only script invocation. Its implementation is reserved to the engine.
/// </summary>
[Expose]
public interface IScriptInvocation
{
	internal Script Target { get; }
	internal TResult Apply<TResult, TNext>( TNext next ) where TNext : ScriptInvocationBuilder.IContinuation<TResult>, allows ref struct;
}

/// <summary>
/// A temporary script argument, kept on the stack. Chain With to add arguments, then Run or Compile.
/// Each run uses fresh copies; host objects and borrowed buffers remain shared.
/// </summary>
/// <typeparam name="T">The argument type, inferred by With. May be a ref struct.</typeparam>
[Expose]
public readonly ref struct ScriptInvocation<T> : IScriptInvocation where T : allows ref struct
{
	private readonly Script _script;
	private readonly string _name;
	private readonly T _value;

	internal ScriptInvocation( Script script, string name, T value )
	{
		_script = script;
		_name = name;
		_value = value;
	}

	Script IScriptInvocation.Target => _script ?? throw new InvalidOperationException( "Create an invocation with Script.With before using it." );

	TResult IScriptInvocation.Apply<TResult, TNext>( TNext next ) => next.Apply( _script.Instance.With( _name, _value ) );

	/// <summary>
	/// Adds a temporary argument. The name must be referenced by the script; the last value wins.
	/// </summary>
	public ScriptInvocation<ScriptInvocation<T>, TValue> With<TValue>( string name, TValue value ) where TValue : allows ref struct
	{
		ScriptInvocationBuilder.CheckArgument( this, name );
		return new( this, name, value );
	}

	/// <summary>
	/// Checks compilation without running script expressions. Failure sets Script.LastError.
	/// </summary>
	public bool Compile() => ScriptInvocationBuilder.Compile( this );

	/// <summary>
	/// Runs synchronously. Returns false and sets Script.LastError on failure.
	/// </summary>
	public bool Run( int instructionLimit = ScriptSystem.InstructionLimit ) => ScriptInvocationBuilder.Run( this, instructionLimit );

	/// <summary>
	/// Runs and reads the result. Returns default on failure or when no value is returned.
	/// </summary>
	public TResult RunAndReturn<TResult>( int instructionLimit = ScriptSystem.InstructionLimit ) => ScriptInvocationBuilder.RunAndReturn<TResult, ScriptInvocation<T>>( this, instructionLimit );
}

/// <summary>
/// A chain of temporary script arguments. Values stay on the stack and cannot escape into script storage.
/// </summary>
/// <typeparam name="TPrevious">The preceding invocation, inferred by With.</typeparam>
/// <typeparam name="T">The argument type, which may be a ref struct.</typeparam>
[Expose]
public readonly ref struct ScriptInvocation<TPrevious, T> : IScriptInvocation
	where TPrevious : IScriptInvocation, allows ref struct
	where T : allows ref struct
{
	private readonly TPrevious _previous;
	private readonly string _name;
	private readonly T _value;

	internal ScriptInvocation( TPrevious previous, string name, T value )
	{
		_previous = previous;
		_name = name;
		_value = value;
	}

	Script IScriptInvocation.Target => _previous.Target;

	TResult IScriptInvocation.Apply<TResult, TNext>( TNext next ) =>
		_previous.Apply<TResult, ScriptInvocationBuilder.Append<T, TResult, TNext>>( new( _name, _value, next ) );

	/// <summary>
	/// Adds a temporary argument. The name must be referenced by the script; the last value wins.
	/// </summary>
	public ScriptInvocation<ScriptInvocation<TPrevious, T>, TValue> With<TValue>( string name, TValue value ) where TValue : allows ref struct
	{
		ScriptInvocationBuilder.CheckArgument( this, name );
		return new( this, name, value );
	}

	/// <summary>
	/// Checks compilation without running script expressions. Failure sets Script.LastError.
	/// </summary>
	public bool Compile() => ScriptInvocationBuilder.Compile( this );

	/// <summary>
	/// Runs synchronously. Returns false and sets Script.LastError on failure.
	/// </summary>
	public bool Run( int instructionLimit = ScriptSystem.InstructionLimit ) => ScriptInvocationBuilder.Run( this, instructionLimit );

	/// <summary>
	/// Runs and reads the result. Returns default on failure or when no value is returned.
	/// </summary>
	public TResult RunAndReturn<TResult>( int instructionLimit = ScriptSystem.InstructionLimit ) => ScriptInvocationBuilder.RunAndReturn<TResult, ScriptInvocation<TPrevious, T>>( this, instructionLimit );
}
