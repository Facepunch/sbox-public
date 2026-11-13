using Sandbox.Engine;

namespace Sandbox;

/// <summary>
/// Executes source chunks and commands in the current game state. Loaded definitions are shared,
/// while each execution has private locals. Identical source can share compiled code without sharing execution state.
/// Async functions resume during the owning game tick. Await their tasks instead of blocking the engine thread.
/// Scripting is unavailable in the menu.
/// </summary>
[Expose]
public sealed partial class ScriptSystem
{
	// A runaway-script guard, rather than a budget for ordinary gameplay logic.
	internal const int InstructionLimit = 10_000_000;
	private const int CacheCapacity = 256;
	private readonly GlobalContext _context;
	private readonly int _threadId = Environment.CurrentManagedThreadId;
	private readonly Breen.ScriptSystem _runtime;
	private readonly TypeLibraryScriptResolver _resolver;
	private bool _disposed;
	private int _depth;
	private Breen.ScriptModule Module { get; }

	/// <summary>
	/// Runtime values shared by all scripts in this game context.
	/// </summary>
	public ScriptStore Globals { get; }

	/// <summary>
	/// The most recent source compilation, publication or startup failure, or null after a successful load.
	/// </summary>
	public Script.ScriptError LastError => _fileLoadError ?? _fileStartupErrors.Values.FirstOrDefault() ?? Module.LastError?.ToEngineError();

	/// <summary>
	/// Whether publication or hotload failed after commitment and this context must be restarted.
	/// This remains readable when execution is blocked, as do the retained diagnostics.
	/// </summary>
	public bool RequiresReload
	{
		get
		{
			CheckContextAccess();
			return Module.IsFaulted || _hotloadFailed;
		}
	}

	/// <summary>
	/// Structural dependencies still missing from the current state and retained load/startup failures.
	/// Named-file startup failures remain until that file is changed or removed. Reading diagnostics never
	/// retries execution and remains possible after a publication fault in the owning context.
	/// </summary>
	public IReadOnlyList<Script.ScriptError> Diagnostics
	{
		get
		{
			CheckContextAccess();
			return Module.Diagnostics.Select( diagnostic => diagnostic.ToEngineError() )
				.Concat( _fileStartupErrors.Values )
				.Concat( _fileLoadError is null ? [] : new[] { _fileLoadError } ).Distinct().ToArray();
		}
	}

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
		if ( context == GlobalContext.Menu )
		{
			throw new InvalidOperationException( "Scripting is unavailable in the menu." );
		}

		_context = context;
		_resolver = new( context.TypeLibrary );
		_runtime = new( new()
		{
			Resolver = _resolver,
			CacheCapacity = CacheCapacity,
			GlobalStaticTypes = [typeof( Internal.GlobalGameNamespace )]
		} );
		Globals = new( this );
		_runtime.SetStore( "global", Globals.Instance );
		Module = new( _runtime, InstructionLimit, CheckAccess, assemblyOptions: new()
		{
			Name = "Sandbox.Game.Scripts",
			ReuseUnchangedAssembly = true,
			DeclarationAttributes = DeclarationAttributes,
			DeclarationAttributesKey = metadata => (metadata.FilePath, metadata.Line, metadata.Summary),
			FieldInitializerAttribute = typeof( Upgraders.InitializedByAttribute ).GetConstructor( [typeof( string )] ),
			ValidateTypes = ValidateTypes,
			AssemblyChanged = ReplaceAssembly
		} );
	}

	/// <summary>
	/// Executes source in the current state. Matching functions and whole types are replaced; omitted definitions remain.
	/// Each call has fresh private locals. The optional filename is used for diagnostics and source navigation.
	/// Contexts owning named files, including scanned script resources, must use LoadFiles for edits or CreateScript for temporary commands.
	/// Clients receiving the host's scripts must use temporary commands instead of publishing local declarations.
	/// </summary>
	/// <param name="source">The source chunk to compile and execute.</param>
	/// <param name="filePath">An optional diagnostic filename; it does not own declarations.</param>
	/// <returns>True on success; otherwise <see cref="LastError"/> describes the failure.</returns>
	public bool Load( string source, string filePath = "" )
	{
		CheckSourceAccess();
		ArgumentNullException.ThrowIfNull( source );
		if ( _namedFiles.Count > 0 )
		{
			throw new InvalidOperationException( "This context owns named script files. Use LoadFiles for updates or CreateScript for temporary commands." );
		}
		_fileLoadError = null;
		Module.StopWarmup();
		Module.SetStartupStore( "local", new() );
		var result = Module.Execute( source, filePath );
		if ( !Module.IsFaulted )
		{
			WarmMethods();
		}
		return result.Success;
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
			return Module.Run<T>( source );
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
		var instance = Module.CreateCommand( source );
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
		return new Script.Analysis( Module.AnalyzeEditor( Breen.ScriptDocument.Parse( source ), metadata ) );
	}

	internal void CheckAccess()
	{
		CheckContextAccess();
		if ( Module.IsFaulted || _hotloadFailed ) throw new InvalidOperationException( "Script assembly hotload failed. Restart the game context before running scripts." );
	}

	private void CheckContextAccess()
	{
		CheckThread();
		if ( _disposed ) throw new InvalidOperationException( "This scripting context has ended." );
		if ( GlobalContext.Current != _context ) throw new InvalidOperationException( "Scripts must be used in their owning game context." );
		// Honor contexts where access is temporarily disabled, such as static initialization.
		_ = _context.TypeLibrary;
	}

	internal void CheckThread()
	{
		if ( Environment.CurrentManagedThreadId != _threadId ) throw new InvalidOperationException( "Scripts must be used on their owning thread." );
	}

	internal void InvalidateBindings()
	{
		if ( _publishingAssembly )
		{
			Module.InvalidateAssemblyBindings();
		}
		else
		{
			_runtime.InvalidateBindings();
		}
	}

	internal void Enter()
	{
		CheckAccess();
		if ( _depth >= 32 ) throw new InvalidOperationException( "The script invocation depth limit was exceeded." );
		_depth++;
	}

	internal void Exit() => _depth--;

	/// <summary>
	/// Resumes a bounded batch of async script continuations on the owning context's thread each frame.
	/// </summary>
	internal void Tick()
	{
		if ( _disposed || Module.IsFaulted || _hotloadFailed ) return;

		CheckAccess();
		_runtime.PumpAsync( maxContinuations: 100, instructionLimit: InstructionLimit );
	}

	internal void Dispose()
	{
		if ( _disposed )
		{
			return;
		}
		Module.StopWarmup();
		Module.Dispose();
		foreach ( var library in _libraries )
		{
			if ( _assembly is not null )
			{
				library.RemoveAssembly( _assembly );
			}
		}
		if ( _assembly is not null )
		{
			_context.ReplaceScriptAssembly( _assembly, null, () => { } );
		}
		_assembly = null;
		_libraries.Clear();
		_disposed = true;
		_runtime.ClearCaches();
	}
}
