namespace Sandbox;

/// <summary>
/// A reusable script created by <see cref="Game.Scripting"/>. Arguments expire after each invocation;
/// local variables are fresh each run. Use sequentially on the owning context's thread.
/// </summary>
[Expose]
public sealed partial class Script
{
	private readonly ScriptSystem _system;
	private readonly Breen.Script _instance;
	private bool _running;

	/// <summary>The most recent failure, or null after a successful invocation.</summary>
	public ScriptError LastError { get; private set; }

	internal Script( ScriptSystem system, Breen.Script instance )
	{
		_system = system;
		_instance = instance;
	}

	/// <summary>
	/// Whether this script references a named external argument. Use before supplying optional host inputs.
	/// Names are case sensitive; locals do not count as external arguments.
	/// </summary>
	public bool HasArgument( string name )
	{
		_system.CheckAccess();
		return _instance.HasInput( name );
	}

	/// <summary>
	/// Supplies a named input for the next invocation only. Unknown names and changes during execution
	/// are API usage errors. Supply every input needed to bind host member calls before running.
	/// </summary>
	public void SetArgument<T>( string name, T value )
	{
		_system.CheckAccess();
		if ( _running ) throw new InvalidOperationException( "Arguments cannot be changed during execution." );
		_instance.Set( name, value );
	}

	/// <summary>Runs the script, returning false and setting LastError on failure.</summary>
	public bool Run( int instructionLimit = ScriptSystem.InstructionLimit ) =>
		Invoke<object, Breen.ScriptInputs>( false, out _, default, instructionLimit, false );

	/// <summary>
	/// Runs the script and reads its result. Returns default on failure or when no value is returned.
	/// An incompatible return type is reported through LastError.
	/// </summary>
	public T RunAndReturn<T>( int instructionLimit = ScriptSystem.InstructionLimit )
	{
		Invoke<T, Breen.ScriptInputs>( true, out var value, default, instructionLimit, false );
		return value;
	}

	/// <summary>
	/// Creates a stack-only invocation with a temporary argument. Chain With to supply more arguments,
	/// then call Run or Compile. Names must be referenced by the script; repeated names replace earlier values.
	/// </summary>
	public ScriptInvocation<T> With<T>( string name, T value ) where T : allows ref struct
	{
		CheckArgument( name );
		return new( this, name, value );
	}

	internal Breen.Script Instance => _instance;

	internal void CheckArgument( string name )
	{
		CheckArguments();
		ArgumentNullException.ThrowIfNull( name );
		// Invalid source has no reliable input table. Compile and Run report its diagnostic.
		if ( !_instance.HasInput( name ) && !_instance.Diagnostics.Any( x => x.Severity == Breen.Severity.Error ) )
			throw new ArgumentException( $"'{name}' is not referenced by this script.", nameof( name ) );
	}

	internal void CheckArguments()
	{
		_system.CheckAccess();
		if ( _running ) throw new InvalidOperationException( "Arguments cannot be changed during execution." );
	}

	internal bool Invoke<T, TInputs>( bool readReturn, out T value, Breen.ScriptInvocation<TInputs> invocation,
		int instructionLimit, bool hasArguments = true ) where TInputs : Breen.IScriptInputs, allows ref struct
	{
		// A foreign thread must not touch the instance, including its cleanup and error state.
		_system.CheckThread();
		value = default;
		// Reject reentry before cleanup: the outer invocation still owns its arguments.
		if ( _running )
		{
			LastError = new( "SCR_REENTRY", "A script cannot run recursively." );
			return false;
		}

		LastError = null;
		bool entered = false;
		_running = true;
		try
		{
			_system.Enter();
			entered = true;
			instructionLimit = Math.Min( instructionLimit, ScriptSystem.InstructionLimit );
			var result = hasArguments ? invocation.Run( instructionLimit ) : _instance.Run( instructionLimit );
			if ( !result.Success )
			{
				LastError = result.Error.ToEngineError();
				return false;
			}

			if ( readReturn && _instance.HasReturnValue )
			{
				try
				{
					value = _instance.ReturnValue.Get<T>();
				}
				catch ( Exception exception )
				{
					LastError = new( "SCR_RETURN_TYPE", exception.Message );
					return false;
				}
			}
			LastError = null;
			return true;
		}
		catch ( Exception exception )
		{
			LastError = new( "SCR_EXECUTION", exception.Message );
			return false;
		}
		finally
		{
			_instance.ClearInputs();
			if ( entered ) _system.Exit();
			_running = false;
		}
	}
}
