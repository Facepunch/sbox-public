using Sandbox.Engine;

namespace Sandbox;

/// <summary>
/// Creates scripts in the current menu or game context. Identical source shares cached code,
/// while each script has independent arguments and execution state.
/// </summary>
[Expose]
public sealed class ScriptSystem
{
	// A runaway-script guard, rather than a budget for ordinary gameplay logic.
	internal const int InstructionLimit = 10_000_000;
	private const int CacheCapacity = 256;
	private readonly GlobalContext _context;
	private readonly int _threadId = Environment.CurrentManagedThreadId;
	private readonly Breen.ScriptSystem _runtime;
	private bool _disposed;
	private int _depth;

	/// <summary>
	/// Runtime values shared by all scripts in this game or menu context.
	/// </summary>
	public ScriptStore Globals { get; }

	/// <summary>
	/// Creates an independent store. Share it between scripts belonging to the same owner.
	/// </summary>
	public ScriptStore CreateStore()
	{
		CheckAccess();
		return new( this );
	}

	internal ScriptSystem( GlobalContext context )
	{
		_context = context;
		_runtime = new( new() { Resolver = new TypeLibraryScriptResolver( context.TypeLibrary ), CacheCapacity = CacheCapacity } );
		Globals = new( this );
		_runtime.SetStore( "global", Globals.Instance );
	}

	/// <summary>
	/// Runs a script using cached code and returns its value.
	/// Throws if execution fails, no value is returned, or the return type is incompatible with <typeparamref name="T"/>.
	/// </summary>
	/// <typeparam name="T">The exact value type or compatible reference type of the returned value.</typeparam>
	public T Run<T>( string source )
	{
		Enter();
		try
		{
			return _runtime.RunScript<T>( source, instructionLimit: InstructionLimit );
		}
		catch ( Breen.ScriptException exception )
		{
			throw new Script.ScriptException( exception.Code, exception.Message, exception.InnerException );
		}
		finally
		{
			Exit();
		}
	}

	/// <summary>
	/// Creates a reusable script. Syntax and execution errors are reported through <see cref="Script.LastError"/>.
	/// Binding and IL compilation occur on the first run, once arguments are available.
	/// Supply a local store to share persistent fields or preserve them across source edits.
	/// If omitted, the script receives its own local store.
	/// </summary>
	public Script CreateScript( string source, ScriptStore locals = null )
	{
		CheckAccess();
		ArgumentNullException.ThrowIfNull( source );
		locals?.CheckAccess( this );
		var instance = _runtime.CreateScript( source );
		instance.SetStore( "local", (locals ?? CreateStore()).Instance );
		return new Script( this, instance );
	}

	/// <summary>
	/// Analyzes source for editor display without running scripts, getters or host methods.
	/// Inputs describe types, not values. Call on the owning context's thread. The returned
	/// snapshot can be queried independently; recreate it after source, inputs or permissions change.
	/// </summary>
	public Script.Analysis Analyze( string source, IEnumerable<Script.Input> inputs = null )
	{
		CheckAccess();
		ArgumentNullException.ThrowIfNull( source );
		var metadata = inputs?.Select( input => input.ToBreenInput() );
		return new Script.Analysis( _runtime.AnalyzeEditor( Breen.ScriptDocument.Parse( source ), metadata ) );
	}

	internal void CheckAccess()
	{
		CheckThread();
		if ( _disposed ) throw new InvalidOperationException( "This scripting context has ended." );
		if ( GlobalContext.Current != _context ) throw new InvalidOperationException( "Scripts must be used in their owning game or menu context." );
		// Honor contexts where access is temporarily disabled, such as static initialization.
		_ = _context.TypeLibrary;
	}

	internal void CheckThread()
	{
		if ( Environment.CurrentManagedThreadId != _threadId ) throw new InvalidOperationException( "Scripts must be used on their owning thread." );
	}

	internal void Enter()
	{
		CheckAccess();
		if ( _depth >= 32 ) throw new InvalidOperationException( "The script invocation depth limit was exceeded." );
		_depth++;
	}

	internal void Exit() => _depth--;

	internal void InvalidateBindings()
	{
		_runtime.InvalidateBindings();
	}

	internal void Dispose()
	{
		_disposed = true;
		_runtime.ClearCaches();
	}
}
