// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Thread-local context for converting call stacks, frames, and methods.
/// </summary>
internal sealed class ThreadConversionContext
{
	public required Dictionary<CallStackIndex, int> MapCallStackIndexToFirefox { get; init; }
	public required Dictionary<CodeAddressIndex, int> MapCodeAddressIndexToFirefox { get; init; }
	public required Dictionary<MethodIndex, int> MapMethodIndexToFirefox { get; init; }
	public required Dictionary<string, int> MapStringToFirefox { get; init; }
	public required Dictionary<CodeAddressIndex, int> MapCodeAddressIndexToMethodIndexFirefox { get; init; }
	public required int ProfileThreadIndex { get; init; }
}

/// <summary>
/// Converts an ETW trace file to a Firefox profile.
/// </summary>
public sealed class EtwConverterToFirefox : IDisposable
{
	private readonly Dictionary<ModuleFileIndex, int> _mapModuleFileIndexToFirefox;
	private readonly HashSet<ModuleFileIndex> _setManagedModules;
	private readonly SymbolReader _symbolReader;
	private readonly ETWTraceEventSource _etl;
	private readonly TraceLog _traceLog;
	private ModuleFileIndex _clrJitModuleIndex = ModuleFileIndex.Invalid;
	private ModuleFileIndex _coreClrModuleIndex = ModuleFileIndex.Invalid;
	private int _profileThreadIndex;
	private readonly FirefoxProfiler.Profile _profile;

	/// <summary>
	/// A generic other category.
	/// </summary>
	public const int CategoryOther = 0;

	/// <summary>
	/// The kernel category.
	/// </summary>
	public const int CategoryKernel = 1;

	/// <summary>
	/// The native category.
	/// </summary>
	public const int CategoryNative = 2;

	/// <summary>
	/// The managed category.
	/// </summary>
	public const int CategoryManaged = 3;

	/// <summary>
	/// The GC category.
	/// </summary>
	public const int CategoryGc = 4;

	/// <summary>
	/// The JIT category.
	/// </summary>
	public const int CategoryJit = 5;

	/// <summary>
	/// The CLR category.
	/// </summary>
	public const int CategoryClr = 6;

	private EtwConverterToFirefox( string traceFilePath, string additionalSymbolPath = null )
	{
		_etl = new ETWTraceEventSource( traceFilePath );
		_traceLog = TraceLog.OpenOrConvert( traceFilePath );

		var symPath = new SymbolPath( @"SRV*https://msdl.microsoft.com/download/symbols" );
		if ( additionalSymbolPath != null )
		{
			symPath.Add( additionalSymbolPath );
		}
		symPath.InsureHasCache( symPath.DefaultSymbolCache() ).CacheFirst();

		_symbolReader = new SymbolReader( TextWriter.Null, symPath.ToString() );
		_symbolReader.Options = SymbolReaderOptions.None;
		_symbolReader.SecurityCheck = ( pdbPath ) => true;

		_profile = CreateProfile();

		_mapModuleFileIndexToFirefox = new();
		_setManagedModules = new();
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_symbolReader.Dispose();
		_traceLog.Dispose();
		_etl.Dispose();
	}

	/// <summary>
	/// Converts an ETW trace file to a Firefox profile.
	/// </summary>
	/// <param name="traceFilePath">The ETW trace file to convert.</param>
	/// <param name="options">The options used for converting.</param>
	/// <param name="processIds">The list of process ids to extract from the ETL file.</param>
	/// <returns>The converted Firefox profile.</returns>
	public static FirefoxProfiler.Profile Convert( string traceFilePath, List<int> processIds, string additionalSymbolPath = null )
	{
		using var converter = new EtwConverterToFirefox( traceFilePath, additionalSymbolPath );
		return converter.Convert( processIds );
	}

	private FirefoxProfiler.Profile Convert( List<int> processIds )
	{
		// MSNT_SystemTrace/Image/KernelBase - ThreadID="-1" ProcessorNumber="9" ImageBase="0xfffff80074000000" 

		// We don't have access to physical CPUs
		//profile.Meta.PhysicalCPUs = Environment.ProcessorCount / 2;
		//profile.Meta.CPUName = ""; // TBD

		_profileThreadIndex = 0;

		foreach ( var processId in processIds )
		{
			var process = _traceLog.Processes.LastProcessWithID( processId );

			ConvertProcess( process );
		}

		return _profile;
	}

	/// <summary>
	/// Converts an ETW trace process to a Firefox profile.
	/// </summary>
	/// <param name="process">The process to convert.</param>
	private void ConvertProcess( TraceProcess process )
	{
		if ( _profile.Meta.Product == string.Empty )
		{
			_profile.Meta.Product = process.Name;
		}

		var processStartTime = new DateTimeOffset( process.StartTime.ToUniversalTime() ).ToUnixTimeMilliseconds();
		var processEndTime = new DateTimeOffset( process.EndTime.ToUniversalTime() ).ToUnixTimeMilliseconds();
		if ( processStartTime < _profile.Meta.StartTime )
		{
			_profile.Meta.StartTime = processStartTime;
		}
		if ( processEndTime > _profile.Meta.EndTime )
		{
			_profile.Meta.EndTime = processEndTime;
		}

		var profilingStartTime = process.StartTimeRelativeMsec;
		if ( profilingStartTime < _profile.Meta.ProfilingStartTime )
		{
			_profile.Meta.ProfilingStartTime = profilingStartTime;
		}
		var profilingEndTime = process.EndTimeRelativeMsec;
		if ( profilingEndTime > _profile.Meta.ProfilingEndTime )
		{
			_profile.Meta.ProfilingEndTime = profilingEndTime;
		}

		LoadModules( process );

		var gcHeapStatsEvents = new ConcurrentBag<(double, GCHeapStatsEvent)>();
		var jitCompilePendingMethodId = new ConcurrentDictionary<long, (JitCompileEvent, double)>();

		// Sort threads by CPU time
		var threads = process.Threads.ToList();
		threads.Sort( ( a, b ) => b.CPUMSec.CompareTo( a.CPUMSec ) );

		// Filter threads upfront
		var threadVisited = new HashSet<int>();
		var validThreads = threads
			.Where( t => t.CPUMSec > 0 && threadVisited.Add( t.ThreadID ) )
			.Select( ( thread, index ) => (thread, originalIndex: index) )
			.ToList();

		double maxCpuTime = validThreads.Count > 0 ? validThreads[0].thread.CPUMSec : 0;
		int threadIndexWithMaxCpuTime = validThreads.Count > 0 ? _profileThreadIndex : -1;

		var processName = $"{process.Name} ({process.ProcessID})";

		// Process threads in parallel
		var processedThreads = new ConcurrentDictionary<int, (FirefoxProfiler.Thread profileThread, double cpuMSec)>();

		Parallel.ForEach( validThreads, validThread =>
		{
			var (thread, threadIndex) = validThread;

			// Per-thread local dictionaries for thread-safe processing
			var localMapCallStackIndexToFirefox = new Dictionary<CallStackIndex, int>();
			var localMapCodeAddressIndexToFirefox = new Dictionary<CodeAddressIndex, int>();
			var localMapMethodIndexToFirefox = new Dictionary<MethodIndex, int>();
			var localMapStringToFirefox = new Dictionary<string, int>( StringComparer.Ordinal );
			var localMapCodeAddressIndexToMethodIndexFirefox = new Dictionary<CodeAddressIndex, int>();

			Stack<(double, GCSuspendExecutionEngineEvent)> gcSuspendEEEvents = new();
			Stack<double> gcRestartEEEvents = new();
			Stack<(double, GCEvent)> gcStartStopEvents = new();

			var threadBaseName = thread.ThreadInfo is not null
				? $"{thread.ThreadInfo} ({thread.ThreadID})"
				: $"Thread ({thread.ThreadID})";
			var threadName = $"{threadIndex} - {threadBaseName}";

			var profileThread = new FirefoxProfiler.Thread
			{
				Name = threadName,
				ProcessName = processName,
				ProcessStartupTime = thread.StartTimeRelativeMSec,
				RegisterTime = thread.StartTimeRelativeMSec,
				ProcessShutdownTime = thread.EndTimeRelativeMSec,
				UnregisterTime = thread.EndTimeRelativeMSec,
				ProcessType = "default",
				Pid = $"{process.ProcessID}",
				Tid = $"{thread.ThreadID}",
				ShowMarkersInTimeline = true
			};

			// Create thread-local context for conversion methods
			var threadContext = new ThreadConversionContext
			{
				MapCallStackIndexToFirefox = localMapCallStackIndexToFirefox,
				MapCodeAddressIndexToFirefox = localMapCodeAddressIndexToFirefox,
				MapMethodIndexToFirefox = localMapMethodIndexToFirefox,
				MapStringToFirefox = localMapStringToFirefox,
				MapCodeAddressIndexToMethodIndexFirefox = localMapCodeAddressIndexToMethodIndexFirefox,
				ProfileThreadIndex = threadIndex
			};

			Commands.Log( $"Converting Events for Thread: {profileThread.Name}" );

			var samples = profileThread.Samples;
			var markers = profileThread.Markers;

			samples.ThreadCPUDelta = new List<int?>();
			samples.TimeDeltas = new List<double>();
			samples.WeightType = "samples";

			//const TraceEventID GCStartEventID = (TraceEventID) 1;
			//const TraceEventID GCStopEventID = (TraceEventID) 2;
			const TraceEventID GCRestartEEStopEventID = (TraceEventID)3;
			//const TraceEventID GCHeapStatsEventID = (TraceEventID) 4;
			//const TraceEventID GCCreateSegmentEventID = (TraceEventID) 5;
			//const TraceEventID GCFreeSegmentEventID = (TraceEventID) 6;
			const TraceEventID GCRestartEEStartEventID = (TraceEventID)7;
			const TraceEventID GCSuspendEEStopEventID = (TraceEventID)8;
			//const TraceEventID GCSuspendEEStartEventID = (TraceEventID) 9;
			//const TraceEventID GCAllocationTickEventID = (TraceEventID) 10;

			double startTime = 0;
			double switchTimeInMsec = 0.0;
			//double switchTimeOutMsec = 0.0;
			foreach ( var evt in thread.EventsInThread )
			{
				if ( evt.Opcode != (TraceEventOpcode)46 )
				{
					if ( evt.Opcode == (TraceEventOpcode)0x24 && evt is CSwitchTraceData switchTraceData )
					{
						if ( evt.ThreadID == thread.ThreadID && switchTraceData.OldThreadID != thread.ThreadID )
						{
							// Old Thread -> This Thread
							// Switch-in
							switchTimeInMsec = evt.TimeStampRelativeMSec;
						}
						//else if (evt.ThreadID != thread.ThreadID && switchTraceData.OldThreadID == thread.ThreadID)
						//{
						//    // This Thread -> Other Thread
						//    // Switch-out
						//    switchTimeOutMsec = evt.TimeStampRelativeMSec;
						//}
					}

					if ( evt.ThreadID == thread.ThreadID )
					{
						if ( evt is MethodJittingStartedTraceData methodJittingStarted )
						{
							var signature = methodJittingStarted.MethodSignature;
							var indexOfParent = signature.IndexOf( '(' );
							if ( indexOfParent >= 0 )
							{
								signature = signature.Substring( indexOfParent );
							}

							var jitCompile = new JitCompileEvent
							{
								FullName =
									$"{methodJittingStarted.MethodNamespace}.{methodJittingStarted.MethodName}{signature}",
								MethodILSize = methodJittingStarted.MethodILSize
							};

							jitCompilePendingMethodId.TryAdd( methodJittingStarted.MethodID,
								(jitCompile, evt.TimeStampRelativeMSec) );
						}
						else if ( evt is MethodLoadUnloadTraceDataBase methodLoadUnloadVerbose )
						{
							if ( jitCompilePendingMethodId.TryRemove( methodLoadUnloadVerbose.MethodID,
									out var jitCompilePair ) )
							{
								markers.StartTime.Add( jitCompilePair.Item2 );
								markers.EndTime.Add( evt.TimeStampRelativeMSec );
								markers.Category.Add( CategoryJit );
								markers.Phase.Add( FirefoxProfiler.MarkerPhase.Interval );
								markers.ThreadId.Add( threadContext.ProfileThreadIndex );
								markers.Name.Add( GetOrCreateString( "JitCompile", profileThread, threadContext ) );
								markers.Data.Add( jitCompilePair.Item1 );
								markers.Length++;
							}
						}
						else if ( evt is GCHeapStatsTraceData gcHeapStats )
						{
							markers.StartTime.Add( evt.TimeStampRelativeMSec );
							markers.EndTime.Add( evt.TimeStampRelativeMSec );
							markers.Category.Add( CategoryGc );
							markers.Phase.Add( FirefoxProfiler.MarkerPhase.Instance );
							markers.ThreadId.Add( threadContext.ProfileThreadIndex );
							markers.Name.Add( GetOrCreateString( $"GCHeapStats", profileThread, threadContext ) );

							var heapStatEvent = new GCHeapStatsEvent
							{
								TotalHeapSize = gcHeapStats.TotalHeapSize,
								TotalPromoted = gcHeapStats.TotalPromoted,
								GenerationSize0 = gcHeapStats.GenerationSize0,
								TotalPromotedSize0 = gcHeapStats.TotalPromotedSize0,
								GenerationSize1 = gcHeapStats.GenerationSize1,
								TotalPromotedSize1 = gcHeapStats.TotalPromotedSize1,
								GenerationSize2 = gcHeapStats.GenerationSize2,
								TotalPromotedSize2 = gcHeapStats.TotalPromotedSize2,
								GenerationSize3 = gcHeapStats.GenerationSize3,
								TotalPromotedSize3 = gcHeapStats.TotalPromotedSize3,
								GenerationSize4 = gcHeapStats.GenerationSize4,
								TotalPromotedSize4 = gcHeapStats.TotalPromotedSize4,
								FinalizationPromotedSize = gcHeapStats.FinalizationPromotedSize,
								FinalizationPromotedCount = gcHeapStats.FinalizationPromotedCount,
								PinnedObjectCount = gcHeapStats.PinnedObjectCount,
								SinkBlockCount = gcHeapStats.SinkBlockCount,
								GCHandleCount = gcHeapStats.GCHandleCount
							};

							gcHeapStatsEvents.Add( (evt.TimeStampRelativeMSec, heapStatEvent) );

							markers.Data.Add( heapStatEvent );
							markers.Length++;
						}
						else if ( evt is GCAllocationTickTraceData allocationTick )
						{
							markers.StartTime.Add( evt.TimeStampRelativeMSec );
							markers.EndTime.Add( evt.TimeStampRelativeMSec );
							markers.Category.Add( CategoryGc );
							markers.Phase.Add( FirefoxProfiler.MarkerPhase.Instance );
							markers.ThreadId.Add( threadContext.ProfileThreadIndex );
							markers.Name.Add( GetOrCreateString( $"{threadIndex} - GC Alloc ({thread.ThreadID})", profileThread, threadContext ) );

							var allocationTickEvent = new GCAllocationTickEvent
							{
								AllocationAmount = allocationTick.AllocationAmount,
								AllocationKind = allocationTick.AllocationKind switch
								{
									GCAllocationKind.Small => "Small",
									GCAllocationKind.Large => "Large",
									GCAllocationKind.Pinned => "Pinned",
									_ => "Unknown"
								},
								TypeName = allocationTick.TypeName,
								HeapIndex = allocationTick.HeapIndex
							};
							markers.Data.Add( allocationTickEvent );
							markers.Length++;
						}
						else if ( evt.ProviderGuid == ClrTraceEventParser.ProviderGuid )
						{
							if ( evt is GCStartTraceData gcStart )
							{
								var gcEvent = new GCEvent
								{
									Reason = gcStart.Reason.ToString(),
									Count = gcStart.Count,
									Depth = gcStart.Depth,
									GCType = gcStart.Type.ToString()
								};

								gcStartStopEvents.Push( (evt.TimeStampRelativeMSec, gcEvent) );
							}
							else if ( evt is GCEndTraceData gcEnd && gcStartStopEvents.Count > 0 )
							{
								var (gcEventStartTime, gcEvent) = gcStartStopEvents.Pop();

								markers.StartTime.Add( gcEventStartTime );
								markers.EndTime.Add( evt.TimeStampRelativeMSec );
								markers.Category.Add( CategoryGc );
								markers.Phase.Add( FirefoxProfiler.MarkerPhase.Interval );
								markers.ThreadId.Add( threadContext.ProfileThreadIndex );
								markers.Name.Add( GetOrCreateString( $"GC Event", profileThread, threadContext ) );
								markers.Data.Add( gcEvent );
								markers.Length++;
							}
							else if ( evt is GCSuspendEETraceData gcSuspendEE )
							{
								var gcSuspendEEEvent = new GCSuspendExecutionEngineEvent
								{
									Reason = gcSuspendEE.Reason.ToString(),
									Count = gcSuspendEE.Count
								};

								gcSuspendEEEvents.Push( (evt.TimeStampRelativeMSec, gcSuspendEEEvent) );
							}
							else if ( evt.ID == GCSuspendEEStopEventID && evt is GCNoUserDataTraceData &&
									 gcSuspendEEEvents.Count > 0 )
							{
								var (gcSuspendEEEventStartTime, gcSuspendEEEvent) = gcSuspendEEEvents.Pop();

								markers.StartTime.Add( gcSuspendEEEventStartTime );
								markers.EndTime.Add( evt.TimeStampRelativeMSec );
								markers.Category.Add( CategoryGc );
								markers.Phase.Add( FirefoxProfiler.MarkerPhase.Interval );
								markers.ThreadId.Add( threadContext.ProfileThreadIndex );
								markers.Name.Add( GetOrCreateString( $"GC Suspend EE", profileThread, threadContext ) );
								markers.Data.Add( gcSuspendEEEvent );
								markers.Length++;
							}
							else if ( evt.ID == GCRestartEEStartEventID && evt is GCNoUserDataTraceData )
							{
								gcRestartEEEvents.Push( evt.TimeStampRelativeMSec );
							}
							else if ( evt.ID == GCRestartEEStopEventID && evt is GCNoUserDataTraceData &&
									 gcRestartEEEvents.Count > 0 )
							{
								var gcRestartEEEventStartTime = gcRestartEEEvents.Pop();

								markers.StartTime.Add( gcRestartEEEventStartTime );
								markers.EndTime.Add( evt.TimeStampRelativeMSec );
								markers.Category.Add( CategoryGc );
								markers.Phase.Add( FirefoxProfiler.MarkerPhase.Interval );
								markers.ThreadId.Add( threadContext.ProfileThreadIndex );
								markers.Name.Add( GetOrCreateString( $"GC Restart EE", profileThread, threadContext ) );
								markers.Data.Add( null );
								markers.Length++;
							}
						}
					}

					continue;
				}

				if ( evt.ProcessID != process.ProcessID || evt.ThreadID != thread.ThreadID )
				{
					continue;
				}

				//Console.WriteLine($"PERF {evt}");

				var callStackIndex = evt.CallStackIndex();
				if ( callStackIndex == CallStackIndex.Invalid )
				{
					continue;
				}

				// Add sample
				var firefoxCallStackIndex = ConvertCallStack( callStackIndex, profileThread, threadContext );

				var deltaTime = evt.TimeStampRelativeMSec - startTime;
				samples.TimeDeltas.Add( deltaTime );
				samples.Stack.Add( firefoxCallStackIndex );
				var cpuDeltaMs = (long)((evt.TimeStampRelativeMSec - switchTimeInMsec) * 1_000_000.0);
				if ( cpuDeltaMs > 0 )
				{
					samples.ThreadCPUDelta.Add( (int)cpuDeltaMs );
				}
				else
				{
					samples.ThreadCPUDelta.Add( 0 );
				}

				switchTimeInMsec = evt.TimeStampRelativeMSec;
				samples.Length++;
				startTime = evt.TimeStampRelativeMSec;
			}

			processedThreads.TryAdd( threadIndex, (profileThread, thread.CPUMSec) );
		} );

		// Merge results in order
		foreach ( var validThread in validThreads.OrderBy( t => t.originalIndex ) )
		{
			var threadIndex = validThread.originalIndex;
			if ( processedThreads.TryGetValue( threadIndex, out var result ) )
			{
				_profile.Threads.Add( result.profileThread );

				// Make visible threads in the UI that consume a minimum amount of CPU time
				if ( result.cpuMSec > 10 )
				{
					_profile.Meta.InitialVisibleThreads!.Add( _profileThreadIndex );
				}

				// We will select by default the thread that has the maximum activity
				if ( result.cpuMSec > maxCpuTime )
				{
					maxCpuTime = result.cpuMSec;
					threadIndexWithMaxCpuTime = _profileThreadIndex;
				}

				_profileThreadIndex++;
			}
		}

		// If we have GCHeapStatsEvents, we can create a Memory track
		if ( !gcHeapStatsEvents.IsEmpty )
		{
			var sortedGcHeapStatsEvents = gcHeapStatsEvents.OrderBy( e => e.Item1 ).ToList();

			var gcHeapStatsCounter = new FirefoxProfiler.Counter()
			{
				Name = "GCHeapStats",
				Category = "Memory", // Category must be Memory otherwise it won't be displayed
				Description = "GC Heap Stats",
				Color = FirefoxProfiler.ProfileColor.Orange, // Doesn't look like it is used
				Pid = $"{process.ProcessID}",
				MainThreadIndex = threadIndexWithMaxCpuTime,
			};

			//gcHeapStatsCounter.Samples.Number = new();
			gcHeapStatsCounter.Samples.Time = new();

			_profile.Counters ??= new();
			_profile.Counters.Add( gcHeapStatsCounter );

			long previousTotalHeapSize = 0;

			// Bug in Memory, they discard the first sample
			// and it is then not recording the first TotalHeapSize which is the initial value
			// So we force to create a dummy empty entry
			// https://github.com/firefox-devtools/profiler/blob/e9fe870f2a85b1c8771b1d671eb316bd1f5723ec/src/profile-logic/profile-data.js#L1732-L1753
			gcHeapStatsCounter.Samples.Time!.Add( 0 );
			gcHeapStatsCounter.Samples.Count.Add( 0 );
			gcHeapStatsCounter.Samples.Length++;

			foreach ( var evt in sortedGcHeapStatsEvents )
			{
				gcHeapStatsCounter.Samples.Time!.Add( evt.Item1 );
				// The memory track is special and is assuming a delta
				var deltaMemory = evt.Item2.TotalHeapSize - previousTotalHeapSize;
				gcHeapStatsCounter.Samples.Count.Add( deltaMemory );
				gcHeapStatsCounter.Samples.Length++;
				previousTotalHeapSize = evt.Item2.TotalHeapSize;
			}
		}

		if ( threads.Count > 0 )
		{
			// Always make at least the first thread visible (that is taking most of the CPU time)
			if ( !_profile.Meta.InitialVisibleThreads!.Contains( threadIndexWithMaxCpuTime ) )
			{
				_profile.Meta.InitialVisibleThreads.Add( threadIndexWithMaxCpuTime );
			}

			_profile.Meta.InitialSelectedThreads!.Add( threadIndexWithMaxCpuTime );
		}
	}

	// we don't want to load symbols for all 150 modules, this would be waste of time
	// only load modules that actually contain relevant information for use
	private static readonly HashSet<string> allowedModules = ["kernel32", "ntdll", "hostpolicy", "hostfxr", "gdi", "win32u", "clrjit", "coreclr", "qwindows", "Qt5Core", "Qt5Widgets", "engine2", "tier0", "sbox", "sbox-dev", "meshsystem", "animationsystem", "resourcecompiler", "materialsystem2", "toolframework2", "assetsystem", "hammer", "rendersystemvulkan", "filesystem_stdio"];

	/// <summary>
	/// Loads the modules - and symbols for a given process.
	/// </summary>
	/// <param name="process">The process to load the modules.</param>
	private void LoadModules( TraceProcess process )
	{
		Commands.Log( $"Loading Modules for process {process.Name} ({process.ProcessID})" );

		_setManagedModules.Clear();
		_clrJitModuleIndex = ModuleFileIndex.Invalid;
		_coreClrModuleIndex = ModuleFileIndex.Invalid;

		var allModules = process.LoadedModules.Where( module => allowedModules.Contains( module.ModuleFile.Name ) ).ToList();

		// Pre-process modules to identify special indices and managed modules (single-threaded, fast)
		foreach ( var module in allModules )
		{
			var fileName = Path.GetFileName( module.FilePath );
			if ( fileName.Equals( "clrjit.dll", StringComparison.OrdinalIgnoreCase ) )
			{
				_clrJitModuleIndex = module.ModuleFile.ModuleFileIndex;
			}
			else if ( fileName.Equals( "coreclr.dll", StringComparison.OrdinalIgnoreCase ) )
			{
				_coreClrModuleIndex = module.ModuleFile.ModuleFileIndex;
			}

			if ( module is TraceManagedModule managedModule )
			{
				_setManagedModules.Add( managedModule.ModuleFile.ModuleFileIndex );

				foreach ( var otherModule in allModules.Where( x => x is not TraceManagedModule ) )
				{
					if ( string.Equals( managedModule.FilePath, otherModule.FilePath, StringComparison.OrdinalIgnoreCase ) )
					{
						_setManagedModules.Add( otherModule.ModuleFile.ModuleFileIndex );
					}
				}
			}
		}

		// Filter modules that need symbol loading
		var modulesToLoad = allModules
			.Where( module => !_mapModuleFileIndexToFirefox.ContainsKey( module.ModuleFile.ModuleFileIndex ) )
			.ToList();

		// Parallel symbol loading - the slow part
		var loadedModules = new ConcurrentBag<(TraceLoadedModule module, FirefoxProfiler.Lib lib)>();
		var processedCount = 0;

		Parallel.ForEach( modulesToLoad, module =>
		{
			var currentCount = Interlocked.Increment( ref processedCount );
			Commands.Log( $"Loading Symbols [{currentCount}/{modulesToLoad.Count}] for Module `{module.Name}`" );

			var lib = new FirefoxProfiler.Lib
			{
				Name = module.Name,
				AddressStart = module.ImageBase,
				AddressEnd = module.ModuleFile.ImageEnd,
				Path = module.ModuleFile.FilePath,
				DebugPath = module.ModuleFile.PdbName,
				DebugName = module.ModuleFile.PdbName,
				BreakpadId = $"0x{module.ModuleID:X16}",
				Arch = "x64" // TODO
			};

			// Symbol lookup is the expensive operation - done in parallel
			_traceLog!.CodeAddresses.LookupSymbolsForModule( _symbolReader, module.ModuleFile );

			loadedModules.Add( (module, lib) );
		} );

		// Add to profile in a single-threaded manner to maintain order consistency
		foreach ( var (module, lib) in loadedModules )
		{
			if ( !_mapModuleFileIndexToFirefox.ContainsKey( module.ModuleFile.ModuleFileIndex ) )
			{
				_mapModuleFileIndexToFirefox.Add( module.ModuleFile.ModuleFileIndex, _profile.Libs.Count );
				_profile.Libs.Add( lib );
			}
		}
	}

	/// <summary>
	/// Converts an ETW call stack to a Firefox call stack.
	/// </summary>
	/// <param name="callStackIndex">The ETW callstack index to convert.</param>
	/// <param name="profileThread">The current Firefox thread.</param>
	/// <param name="context">The thread-local conversion context.</param>
	/// <returns>The converted Firefox call stack index.</returns>
	private int ConvertCallStack( CallStackIndex callStackIndex, FirefoxProfiler.Thread profileThread, ThreadConversionContext context )
	{
		if ( callStackIndex == CallStackIndex.Invalid ) return -1;

		var parentCallStackIndex = _traceLog.CallStacks.Caller( callStackIndex );
		var fireFoxParentCallStackIndex = ConvertCallStack( parentCallStackIndex, profileThread, context );

		return ConvertCallStack( callStackIndex, fireFoxParentCallStackIndex, profileThread, context );
	}

	/// <summary>
	/// Converts an ETW call stack to a Firefox call stack.
	/// </summary>
	/// <param name="callStackIndex">The ETW callstack index to convert.</param>
	/// <param name="firefoxParentCallStackIndex">The parent Firefox callstack index.</param>
	/// <param name="profileThread">The current Firefox thread.</param>
	/// <param name="context">The thread-local conversion context.</param>
	/// <returns>The converted Firefox call stack index.</returns>
	private int ConvertCallStack( CallStackIndex callStackIndex, int firefoxParentCallStackIndex, FirefoxProfiler.Thread profileThread, ThreadConversionContext context )
	{
		if ( context.MapCallStackIndexToFirefox.TryGetValue( callStackIndex, out var index ) )
		{
			return index;
		}
		var stackTable = profileThread.StackTable;

		var firefoxCallStackIndex = stackTable.Length;
		context.MapCallStackIndexToFirefox.Add( callStackIndex, firefoxCallStackIndex );

		var codeAddressIndex = _traceLog.CallStacks.CodeAddressIndex( callStackIndex );
		var frameTableIndex = ConvertFrame( codeAddressIndex, profileThread, context, out var category, out var subCategory );

		stackTable.Frame.Add( frameTableIndex );
		stackTable.Category.Add( category );
		stackTable.Subcategory.Add( subCategory );
		stackTable.Prefix.Add( firefoxParentCallStackIndex < 0 ? null : (int)firefoxParentCallStackIndex );
		stackTable.Length++;

		return firefoxCallStackIndex;
	}

	/// <summary>
	/// Converts an ETW code address to a Firefox frame.
	/// </summary>
	/// <param name="codeAddressIndex">The ETW code address index.</param>
	/// <param name="profileThread">The current Firefox thread.</param>
	/// <param name="context">The thread-local conversion context.</param>
	/// <param name="category">The category of the frame.</param>
	/// <param name="subCategory">The subcategory of the frame.</param>
	/// <returns>The converted Firefox frame table index.</returns>
	private int ConvertFrame( CodeAddressIndex codeAddressIndex, FirefoxProfiler.Thread profileThread, ThreadConversionContext context, out int category, out int subCategory )
	{
		var frameTable = profileThread.FrameTable;

		if ( context.MapCodeAddressIndexToFirefox.TryGetValue( codeAddressIndex, out var firefoxFrameTableIndex ) )
		{
			category = frameTable.Category[firefoxFrameTableIndex]!.Value;
			subCategory = frameTable.Subcategory[firefoxFrameTableIndex]!.Value;
			return firefoxFrameTableIndex;
		}

		firefoxFrameTableIndex = frameTable.Length;
		context.MapCodeAddressIndexToFirefox.Add( codeAddressIndex, firefoxFrameTableIndex );

		var module = _traceLog.CodeAddresses.ModuleFile( codeAddressIndex );
		var absoluteAddress = _traceLog.CodeAddresses.Address( codeAddressIndex );
		var offsetIntoModule = module is not null ? (int)(absoluteAddress - module.ImageBase) : 0;

		frameTable.Address.Add( offsetIntoModule );
		frameTable.InlineDepth.Add( 0 );

		bool isManaged = false;
		if ( module is not null )
		{
			isManaged = _setManagedModules.Contains( module.ModuleFileIndex );
		}

		subCategory = 0;

		if ( isManaged )
		{
			category = CategoryManaged;
		}
		else
		{
			bool isKernel = (absoluteAddress >> 56) == 0xFF;
			category = isKernel ? CategoryKernel : CategoryNative;

			if ( module != null )
			{
				if ( module.ModuleFileIndex == _clrJitModuleIndex )
				{
					category = CategoryJit;
				}
				else if ( module.ModuleFileIndex == _coreClrModuleIndex )
				{
					category = CategoryClr;
				}
			}
		}

		var methodIndex = _traceLog.CodeAddresses.MethodIndex( codeAddressIndex );
		var firefoxMethodIndex = ConvertMethod( codeAddressIndex, methodIndex, profileThread, context );

		if ( methodIndex != MethodIndex.Invalid )
		{
			var nameIndex = profileThread.FuncTable.Name[firefoxMethodIndex];
			var fullMethodName = profileThread.StringArray[nameIndex];
			var isGC = fullMethodName.StartsWith( "WKS::gc", StringComparison.OrdinalIgnoreCase ) || fullMethodName.StartsWith( "SVR::gc", StringComparison.OrdinalIgnoreCase );
			if ( isGC )
			{
				category = CategoryGc;
			}
		}

		frameTable.Category.Add( category );
		frameTable.Subcategory.Add( subCategory );

		frameTable.Func.Add( firefoxMethodIndex );

		frameTable.NativeSymbol.Add( null );
		frameTable.InnerWindowID.Add( null );
		frameTable.Implementation.Add( null );

		frameTable.Line.Add( null );
		frameTable.Column.Add( null );
		frameTable.Length++;

		return firefoxFrameTableIndex;
	}

	/// <summary>
	/// Converts an ETW method to a Firefox method.
	/// </summary>
	/// <param name="codeAddressIndex">The original code address.</param>
	/// <param name="methodIndex">The method index. Can be invalid.</param>
	/// <param name="profileThread">The current Firefox thread.</param>
	/// <param name="context">The thread-local conversion context.</param>
	/// <returns>The converted Firefox method index.</returns>
	private int ConvertMethod( CodeAddressIndex codeAddressIndex, MethodIndex methodIndex, FirefoxProfiler.Thread profileThread, ThreadConversionContext context )
	{
		var funcTable = profileThread.FuncTable;
		int firefoxMethodIndex;
		if ( methodIndex == MethodIndex.Invalid )
		{
			if ( context.MapCodeAddressIndexToMethodIndexFirefox.TryGetValue( codeAddressIndex, out var index ) )
			{
				return index;
			}
			firefoxMethodIndex = funcTable.Length;
			context.MapCodeAddressIndexToMethodIndexFirefox[codeAddressIndex] = firefoxMethodIndex;
		}
		else if ( context.MapMethodIndexToFirefox.TryGetValue( methodIndex, out var index ) )
		{
			return index;
		}
		else
		{
			firefoxMethodIndex = funcTable.Length;
			context.MapMethodIndexToFirefox.Add( methodIndex, firefoxMethodIndex );
		}

		if ( methodIndex == MethodIndex.Invalid )
		{
			funcTable.Name.Add( GetOrCreateString( $"0x{_traceLog.CodeAddresses.Address( codeAddressIndex ):X16}", profileThread, context ) );
			funcTable.IsJS.Add( false );
			funcTable.RelevantForJS.Add( false );
			funcTable.Resource.Add( -1 );
			funcTable.FileName.Add( null );
			funcTable.LineNumber.Add( null );
			funcTable.ColumnNumber.Add( null );
		}
		else
		{
			var fullMethodName = _traceLog.CodeAddresses.Methods.FullMethodName( methodIndex ) ?? $"0x{_traceLog.CodeAddresses.Address( codeAddressIndex ):X16}";

			var firefoxMethodNameIndex = GetOrCreateString( fullMethodName, profileThread, context );
			funcTable.Name.Add( firefoxMethodNameIndex );
			funcTable.IsJS.Add( false );
			funcTable.RelevantForJS.Add( false );
			funcTable.FileName.Add( null );
			funcTable.LineNumber.Add( null );
			funcTable.ColumnNumber.Add( null );

			var moduleIndex = _traceLog.CodeAddresses.ModuleFileIndex( codeAddressIndex );
			if ( moduleIndex != ModuleFileIndex.Invalid && _mapModuleFileIndexToFirefox.TryGetValue( moduleIndex, out var firefoxModuleIndex ) )
			{
				funcTable.Resource.Add( profileThread.ResourceTable.Length );

				var moduleName = Path.GetFileName( _traceLog.ModuleFiles[moduleIndex].FilePath );
				profileThread.ResourceTable.Name.Add( GetOrCreateString( moduleName, profileThread, context ) );
				profileThread.ResourceTable.Lib.Add( firefoxModuleIndex );
				profileThread.ResourceTable.Length++;
			}
			else
			{
				funcTable.Resource.Add( -1 );
			}
		}

		funcTable.Length++;

		return firefoxMethodIndex;
	}

	/// <summary>
	/// Gets or creates a string for the specified Firefox profile thread.
	/// </summary>
	/// <param name="text">The string to create.</param>
	/// <param name="profileThread">The current Firefox thread to create the string in.</param>
	/// <param name="context">The thread-local conversion context.</param>
	/// <returns>The index of the string in the Firefox profile thread.</returns>
	private static int GetOrCreateString( string text, FirefoxProfiler.Thread profileThread, ThreadConversionContext context )
	{
		if ( context.MapStringToFirefox.TryGetValue( text, out var index ) )
		{
			return index;
		}
		var firefoxStringIndex = profileThread.StringArray.Count;
		context.MapStringToFirefox.Add( text, firefoxStringIndex );

		profileThread.StringArray.Add( text );
		return firefoxStringIndex;
	}

	/// <summary>
	/// Creates a new Firefox profile.
	/// </summary>
	/// <returns>A new Firefox profile.</returns>
	private FirefoxProfiler.Profile CreateProfile()
	{
		var profile = new FirefoxProfiler.Profile
		{
			Meta =
			{
				StartTime = double.MaxValue,
				EndTime = 0.0f,
				ProfilingStartTime = double.MaxValue,
				ProfilingEndTime = 0.0f,
				Version = 29,
				PreprocessedProfileVersion = 51,
				Product = string.Empty,
				InitialSelectedThreads = [],
				Platform = $"{_traceLog.OSName} {_traceLog.OSVersion} {_traceLog.OSBuild}",
				Oscpu = $"{_traceLog.OSName} {_traceLog.OSVersion} {_traceLog.OSBuild}",
				LogicalCPUs = _traceLog.NumberOfProcessors,
				DoesNotUseFrameImplementation = true,
				Symbolicated = true,
				SampleUnits = new FirefoxProfiler.SampleUnits
				{
					Time = "ms",
					EventDelay = "ms",
					ThreadCPUDelta = "ns"
				},
				InitialVisibleThreads = [],
				Stackwalk = 1,
				Interval = _traceLog.SampleProfileInterval.TotalMilliseconds,
				Categories =
				[
					new FirefoxProfiler.Category()
					{
						Name = "Other",
						Color = FirefoxProfiler.ProfileColor.Grey,
						Subcategories =
						{
							"Other",
						}
					},
					new FirefoxProfiler.Category()
					{
						Name = "Kernel",
						Color = FirefoxProfiler.ProfileColor.Orange,
						Subcategories =
						{
							"Other",
						}
					},
					new FirefoxProfiler.Category()
					{
						Name = "Native",
						Color = FirefoxProfiler.ProfileColor.Blue,
						Subcategories =
						{
							"Other",
						}
					},
					new FirefoxProfiler.Category()
					{
						Name = ".NET",
						Color = FirefoxProfiler.ProfileColor.Green,
						Subcategories =
						{
							"Other",
						}
					},
					new FirefoxProfiler.Category()
					{
						Name = ".NET GC",
						Color = FirefoxProfiler.ProfileColor.Yellow,
						Subcategories =
						{
							"Other",
						}
					},
					new FirefoxProfiler.Category()
					{
						Name = ".NET JIT",
						Color = FirefoxProfiler.ProfileColor.Purple,
						Subcategories =
						{
							"Other",
						}
					},
					new FirefoxProfiler.Category()
					{
						Name = ".NET CLR",
						Color = FirefoxProfiler.ProfileColor.Grey,
						Subcategories =
						{
							"Other",
						}
					},
				],
				Abi = RuntimeInformation.RuntimeIdentifier,
				MarkerSchema =
				{
					JitCompileEvent.Schema(),
					GCEvent.Schema(),
					GCHeapStatsEvent.Schema(),
					GCAllocationTickEvent.Schema(),
					GCSuspendExecutionEngineEvent.Schema(),
					GCRestartExecutionEngineEvent.Schema(),
				}
			}
		};

		return profile;
	}
}
