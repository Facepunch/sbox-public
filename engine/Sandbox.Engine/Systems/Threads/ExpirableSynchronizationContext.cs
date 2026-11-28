using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;

namespace Sandbox.Tasks;

internal class ExpirableSynchronizationContext : SynchronizationContext
{
	static readonly ProfileScope CpuContinuations = new( "Tasks.ProcessContinuations" );
	static readonly ProfileScope CpuWait = new( "Tasks.WaitAndPump" );
	[SkipHotload] static readonly ConditionalWeakTable<Type, ProfileScope> StateMachineProfileScopes = new();
	static FieldInfo AwaitContinuationAction;

	public const int MaxTimeBetweenYieldsMillis = 1_000;

	[SkipHotload]
	private readonly HashSet<IAsyncStateMachine> CancelledStateMachines = new();

	#region Persistent Tasks

	[SkipHotload]
	private static readonly HashSet<Assembly> PersistentTaskAssemblies = new();

	[SkipHotload]
	private static readonly HashSet<Type> PersistentTaskDeclaringTypes = new();

	[SkipHotload]
	private static readonly Dictionary<Type, HashSet<string>> PersistentTaskMethods = new();

	public static void AllowPersistentTaskMethods( Assembly asm )
	{
		lock ( PersistentTaskAssemblies )
		{
			PersistentTaskAssemblies.Add( asm );
		}
	}

	public static void ForbidPersistentTaskMethods( Assembly asm )
	{
		lock ( PersistentTaskAssemblies )
		{
			PersistentTaskAssemblies.Remove( asm );
		}
	}

	#endregion

	internal int Frame;

	private ExpirableSynchronizationContext _descendant;

	/// <summary>
	/// When true, any continuations that attempt to run on this instance will
	/// log an exception, unless whitelisted by <see cref="AllowPersistentTaskMethods"/>.
	/// </summary>
	internal bool HasExpired => _descendant != null || _closed;

	private volatile bool _closed;

	/// <summary>
	/// True once <see cref="Close"/> has run: nothing queued to this instance, or posted to it later, runs.
	/// </summary>
	internal bool IsClosed => _closed;

	public int QueueCount => m_queue.Reader.Count;

	private readonly ConcurrentQueue<ExecutingJob> _executingJobs;
	private readonly Stopwatch _timer = Stopwatch.StartNew();

	private int _currentlyProcessingThreadCount;
	public bool WarnNonYieldingTasks { get; }

	/// <param name="warnNonYieldingTasks">If true, warn when tasks don't yield after <see cref="MaxTimeBetweenYieldsMillis"/>.</param>
	public ExpirableSynchronizationContext( bool warnNonYieldingTasks )
	{
		SetWaitNotificationRequired();

		WarnNonYieldingTasks = warnNonYieldingTasks;

		if ( WarnNonYieldingTasks )
		{
			_executingJobs = new ConcurrentQueue<ExecutingJob>();
			_ = Task.Run( WatchDogAsync );
		}
	}

	/// <summary>
	/// Logs a warning if any actions posted to this sync context take
	/// too long before returning.
	/// </summary>
	private async Task WatchDogAsync()
	{
		while ( !HasExpired || _currentlyProcessingThreadCount > 0 )
		{
			if ( !_executingJobs.TryPeek( out var next ) )
			{
				await Task.Delay( MaxTimeBetweenYieldsMillis );
				continue;
			}

			var runningTime = _timer.Elapsed - next.StartTime;

			if ( !next.IsCompleted && runningTime.TotalMilliseconds < MaxTimeBetweenYieldsMillis )
			{
				await Task.Delay( MaxTimeBetweenYieldsMillis - (int)runningTime.TotalMilliseconds );
			}

			_executingJobs.TryDequeue( out _ );

			if ( next.IsCompleted )
			{
				continue;
			}

			var name = next.State is Delegate deleg
				? deleg.ToSimpleString()
				: next.State.ToString();

			Log.Warning( $"A task has been running without yielding for more than {MaxTimeBetweenYieldsMillis}ms: {name}" );
		}
	}

	public record struct Data( SendOrPostCallback Callback, object State, ExpirableSynchronizationContext Source, Sandbox.Engine.GlobalContext Context );

	private readonly Channel<Data> m_queue = Channel.CreateUnbounded<Data>();

	public override SynchronizationContext CreateCopy()
	{
		return new ExpirableSynchronizationContext( WarnNonYieldingTasks );
	}

	#region Finding State Machine Type

	private static FieldInfo AwaiterTaskField =
		typeof( TaskAwaiter ).GetField( "m_task", BindingFlags.Instance | BindingFlags.NonPublic );

	private static IEnumerable<Task> GetAwaitedTasks( IAsyncStateMachine stateMachine )
	{
		// Compiler-generated state machines store task awaiters in fields
		// with names like <>u__123. Find those, and yield any non-null tasks.
		// We expect there to be at most one, but look for more so that the caller
		// can assert() that.

		var type = stateMachine?.GetType();

		while ( type != null )
		{
			foreach ( var field in type.GetFields( BindingFlags.Instance | BindingFlags.NonPublic ) )
			{
				if ( !field.Name.StartsWith( "<>u__" ) ) continue;

				FieldInfo taskField;

				if ( field.FieldType == typeof( TaskAwaiter ) )
				{
					taskField = AwaiterTaskField;
				}
				else if ( field.FieldType.IsConstructedGenericType && field.FieldType.GetGenericTypeDefinition() == typeof( TaskAwaiter<> ) )
				{
					taskField = field.FieldType.GetField( "m_task", BindingFlags.Instance | BindingFlags.NonPublic );
				}
				else
				{
					continue;
				}

				var awaiter = field.GetValue( stateMachine )!;

				if ( taskField.GetValue( awaiter ) is Task task )
				{
					yield return task;
				}
			}

			type = type.BaseType;
		}
	}

	private static Type AsyncMethodBuilderCoreType { get; } = typeof( RuntimeHelpers ).Assembly.GetType( "System.Runtime.CompilerServices.AsyncMethodBuilderCore" );

	private static Func<Action, Action> TryGetStateMachineForDebugger { get; } = AsyncMethodBuilderCoreType
		.GetMethod( nameof( TryGetStateMachineForDebugger ), BindingFlags.Static | BindingFlags.NonPublic )
		.CreateDelegate<Func<Action, Action>>();

	private static Func<Action, Task> TryGetContinuationTask { get; } = AsyncMethodBuilderCoreType
		.GetMethod( nameof( TryGetContinuationTask ), BindingFlags.Static | BindingFlags.NonPublic )
		.CreateDelegate<Func<Action, Task>>();

	private static readonly Regex StateMachineMethodNameRegex = new Regex( @"^<(?<name>[^>]+)>d__[0-9]+(`[0-9]+)?$" );
	private static readonly Regex LambdaStateMachineNameRegex = new Regex( @"^<<(?<name>[^>]+)>(b__[0-9_]+|g__(?<local>[^|]+)\|[0-9_]+)>d(__[0-9]+)?(`[0-9]+)?$" );

	/// <summary>
	/// "Spawn.Lambda" for an async lambda's state machine &lt;&lt;Spawn&gt;b__0&gt;d, "Spawn.Local" for a local function.
	/// </summary>
	static string CompilerGeneratedMethodName( string stateMachineName )
	{
		var match = LambdaStateMachineNameRegex.Match( stateMachineName );
		if ( !match.Success ) return null;

		var local = match.Groups["local"];
		return $"{match.Groups["name"].Value}.{(local.Success ? local.Value : "Lambda")}";
	}

	private static bool TryGetStateMachineInfo( object state,
		out IAsyncStateMachine stateMachine, out bool isCancelled,
		out Type declaringType, out string methodName )
	{
		stateMachine = null;
		isCancelled = false;
		declaringType = null;
		methodName = null;

		if ( state is not Action action )
		{
			return false;
		}

		if ( action.Target?.GetType() is { FullName: "System.Threading.Tasks.SynchronizationContextAwaitTaskContinuation+<>c__DisplayClass6_0" } targetType )
		{
			action = (Action)targetType.GetField( "action", BindingFlags.Instance | BindingFlags.Public )
				.GetValue( action.Target );
		}

		var task = TryGetContinuationTask( action );
		var moveNext = TryGetStateMachineForDebugger( action );

		stateMachine = moveNext?.Target as IAsyncStateMachine;
		isCancelled = task?.IsCanceled ?? false;

		if ( stateMachine == null )
		{
			return false;
		}

		var stateMachineType = stateMachine.GetType();

		declaringType = stateMachineType.DeclaringType;

		var match = StateMachineMethodNameRegex.Match( stateMachineType.Name );

		if ( match.Success )
		{
			// Make the name a bit nicer than <Example>d__23
			methodName = match.Groups["name"].Value;
		}

		return true;
	}

	#endregion

	private static bool CanTaskMethodPersist( Type declaringType, string methodName )
	{
		lock ( PersistentTaskAssemblies )
		{
			if ( PersistentTaskAssemblies.Contains( declaringType.Assembly ) ) return true;

			if ( declaringType.Assembly.GetCustomAttribute<TasksPersistOnContextResetAttribute>() != null )
			{
				PersistentTaskAssemblies.Add( declaringType.Assembly );
				return true;
			}
		}

		if ( PersistentTaskDeclaringTypes.Contains( declaringType ) ) return true;
		if ( PersistentTaskMethods.TryGetValue( declaringType, out var methodSet ) && methodSet.Contains( methodName ) ) return true;

		if ( declaringType.IsConstructedGenericType )
		{
			var genericTypeDef = declaringType.GetGenericTypeDefinition();

			if ( PersistentTaskDeclaringTypes.Contains( genericTypeDef ) ) return true;
			if ( PersistentTaskMethods.TryGetValue( genericTypeDef, out var methodSet2 ) && methodSet2.Contains( methodName ) ) return true;
		}

		return false;
	}

	private static bool IsAwaitingCancelledTask( IAsyncStateMachine stateMachine )
	{
		// The state machine will have a bunch of
		// fields storing TaskAwaiters, only one of which will be
		// assigned at a time. Here we get the task of the first
		// assigned awaiter, and check if it's cancelled.

		var awaited = GetAwaitedTasks( stateMachine ).ToArray();

		Assert.True( awaited.Length <= 1 );

		if ( awaited.Length == 1 )
		{
			return awaited[0].IsCanceled;
		}

		return false;
	}

	// For safety
	private const int MaxCancellationCount = 1024;

	private bool CanHandleCancellation( IAsyncStateMachine stateMachine )
	{
		lock ( CancelledStateMachines )
		{
			return CancelledStateMachines.Count < MaxCancellationCount
				&& CancelledStateMachines.Add( stateMachine );
		}
	}

	/// <summary>
	/// Returns true if <see cref="HasExpired"/> is false, or if <paramref name="state"/> represents
	/// a task method that is allowed to persist after context expiry. Logs an error otherwise.
	/// </summary>
	private bool CheckValid( object state, out bool isCancelled )
	{
		isCancelled = false;

		// Closed at shutdown: dropped without a warning, logging may already be gone
		if ( _closed ) return false;

		if ( !HasExpired ) return true;

		var methodInfo = string.Empty;

		if ( TryGetStateMachineInfo( state, out var stateMachine, out isCancelled,
			out var declaringType, out var taskMethodName ) )
		{
			if ( isCancelled )
			{
				return true;
			}

			// Manually whitelisted methods can always persist

			if ( CanTaskMethodPersist( declaringType, taskMethodName ) )
			{
				return true;
			}

			// Cancelled tasks should persist to clean up, but only once

			if ( IsAwaitingCancelledTask( stateMachine ) && CanHandleCancellation( stateMachine ) )
			{
				isCancelled = true;
				return true;
			}

			methodInfo = $" in task method {declaringType}.{taskMethodName}";
		}

		Log.Warning( $"Attempted to use an expired {nameof( SynchronizationContext )}{methodInfo}\n" +
					 $"This is probably because a task was left running after ending a game session." );

		return false;
	}

	public override void Send( SendOrPostCallback d, object state )
	{
		if ( !CheckValid( state, out _ ) ) return;

		// TODO: Should we wrap with SynchronizationContext.SetSynchronizationContext( this ) ?

		d( state );
	}

	public override void Post( SendOrPostCallback d, object state )
	{
		if ( !CheckValid( state, out var isCancelled ) ) return;

		var target = GetCurrentContext();
		if ( target.IsClosed ) return;

		var data = new Data( d, state, isCancelled ? this : target, Sandbox.Engine.GlobalContext.Current );

		target.m_queue.Writer.TryWrite( data );
	}

	/// <summary>
	/// The process is ending and the native engine with it: drops what is queued here and everything posted from now on,
	/// and <see cref="Wait"/> stops running queued work. A continuation left over from a game or menu task (a Workshop query
	/// polling Steam, say) would otherwise run in a later wait on this thread, the runtime's own at exit included, and call
	/// into native code that has been unloaded.
	/// </summary>
	public void Close()
	{
		_closed = true;
		m_queue.Writer.TryComplete();

		while ( m_queue.Reader.TryRead( out _ ) ) { }
	}

	public void Expire( ExpirableSynchronizationContext newInstance )
	{
		_descendant = newInstance;

		while ( m_queue.Reader.TryRead( out var data ) )
		{
			if ( CheckValid( data.State, out var isCancelled ) )
			{
				newInstance.m_queue.Writer.TryWrite( new Data( data.Callback, data.State, isCancelled ? this : newInstance, data.Context ) );
			}
		}
	}

	private ExpirableSynchronizationContext GetCurrentContext()
	{
		var ctx = this;

		while ( ctx._descendant != null )
		{
			ctx = ctx._descendant;
		}

		return ctx;
	}

	[ThreadStatic]
	private static ExpirableSynchronizationContext _sCurrentProcessingContext;

	/// <summary>
	/// Inside a queue being processed on this thread, where processing another does nothing.
	/// </summary>
	internal static bool IsProcessing => _sCurrentProcessingContext != null;

	private class ExecutingJob
	{
		public object State { get; init; }
		public TimeSpan StartTime { get; init; }
		public bool IsCompleted { get; set; }
	}

	public void ProcessQueue()
	{
		if ( _sCurrentProcessingContext != null )
		{
			return;
		}

		if ( HasExpired ) return;
		if ( m_queue.Reader.Count == 0 ) return;

		using var cpu = CpuContinuations.Start();
		var profiling = Profiler.ScopesActive;
		var maxProcess = m_queue.Reader.Count + 8;
		var oldContext = Current;
		SetSynchronizationContext( this );

		Interlocked.Increment( ref Frame );
		Interlocked.Increment( ref _currentlyProcessingThreadCount );

		try
		{
			_sCurrentProcessingContext = this;

			while ( m_queue.Reader.TryRead( out var data ) )
			{
				if ( data.Source != this )
				{
					SetSynchronizationContext( data.Source );
				}

				// Only allocate an ExecutingJob when the watchdog is active;
				// when _executingJobs is null this would create thousands of pointless objects.
				ExecutingJob job = null;

				if ( _executingJobs is not null )
				{
					job = new ExecutingJob { State = data.State, StartTime = _timer.Elapsed };
					_executingJobs.Enqueue( job );
				}

				var enterContext = data.Context is not null && data.Context != Sandbox.Engine.GlobalContext.Current;
				var contextScope = enterContext ? new Sandbox.Engine.GlobalContext.GlobalContextScope( data.Context ) : default;

				try
				{
					using var callback = profiling ? GetContinuationProfileScope( data ).Start() : default;
					data.Callback( data.State );
				}
				catch ( OperationCanceledException )
				{
					// fine
				}
				catch ( System.Exception e )
				{
					Log.Error( e );
				}
				finally
				{
					if ( enterContext ) contextScope.Dispose();

					job?.IsCompleted = true;

					if ( data.Source != this )
					{
						SetSynchronizationContext( this );
					}
				}

				maxProcess--;

				if ( maxProcess <= 0 )
					break;
			}
		}
		finally
		{
			Interlocked.Decrement( ref _currentlyProcessingThreadCount );

			_sCurrentProcessingContext = null;
			SetSynchronizationContext( oldContext );
		}
	}

	// Keyed by the runtime's state machine box type, one per async method, so a lookup reads the target's type and allocates nothing
	static ProfileScope GetContinuationProfileScope( Data data )
	{
		if ( data.State is not Action action ) return ProfileCallbackScopes.ForMethod( data.Callback.Method );

		var targetType = action.Target?.GetType();
		if ( targetType is { FullName: "System.Threading.Tasks.SynchronizationContextAwaitTaskContinuation+<>c__DisplayClass6_0" } )
		{
			AwaitContinuationAction ??= targetType.GetField( "action", BindingFlags.Instance | BindingFlags.Public );
			if ( AwaitContinuationAction?.GetValue( action.Target ) is not Action inner ) return ProfileCallbackScopes.ForMethod( action.Method );
			action = inner;
			targetType = action.Target?.GetType();
		}

		if ( targetType is not null && StateMachineProfileScopes.TryGetValue( targetType, out var cached ) ) return cached;

		ProfileScope scope = null;
		try
		{
			if ( TryGetStateMachineForDebugger( action )?.Target is IAsyncStateMachine machine )
				scope = StateMachineProfileScope( machine.GetType() );
		}
		catch ( Exception ) { } // Runtime-internal name discovery must never prevent a continuation from running.
		scope ??= ProfileCallbackScopes.ForMethod( action.Method );

		if ( targetType is not null && targetType.Name.Contains( "StateMachineBox" ) ) StateMachineProfileScopes.AddOrUpdate( targetType, scope );
		return scope;
	}

	static ProfileScope StateMachineProfileScope( Type machineType )
	{
		var match = StateMachineMethodNameRegex.Match( machineType.Name );
		var method = match.Success ? match.Groups["name"].Value : CompilerGeneratedMethodName( machineType.Name );

		// Lambdas and local functions live on compiler generated classes, name them after the type that wrote them
		var type = machineType.DeclaringType;
		while ( type is not null && type.Name.StartsWith( "<>" ) )
			type = type.DeclaringType;

		return type is not null && method is not null ? ProfileCallbackScopes.ForType( type, method + ".Continuation" ) : null;
	}

	public override int Wait( IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout )
	{
		const int WAIT_TIMEOUT = 0x102; // 258

		// A zero timeout is a poll, and invalid timeouts should be rejected by the runtime. A closed context runs nothing.
		if ( _closed || millisecondsTimeout != Timeout.Infinite && millisecondsTimeout <= 0 )
			return base.Wait( waitHandles, waitAll, millisecondsTimeout );

		using var cpu = CpuWait.Start();
		var started = Stopwatch.GetTimestamp();

		while ( true )
		{
			//
			// Wait for at most 2 milliseconds before pumping queued work.
			//
			var waitTime = millisecondsTimeout == Timeout.Infinite ? 2
				: (int)Math.Clamp( Math.Ceiling( millisecondsTimeout - Stopwatch.GetElapsedTime( started ).TotalMilliseconds ), 0, 2 );
			var val = base.Wait( waitHandles, waitAll, waitTime );

			//
			// If we didn't time out, then we probably finished waiting, so just return
			//
			if ( val != WAIT_TIMEOUT ) return val;

			//
			// Include time spent processing the queue in the timeout.
			//
			if ( millisecondsTimeout != Timeout.Infinite
				&& Stopwatch.GetElapsedTime( started ).TotalMilliseconds >= millisecondsTimeout )
				return val;

			//
			// Keep processing the task queue while we're waiting
			//
			ProcessQueue();
		}
	}
}
