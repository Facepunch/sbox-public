using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Sandbox.Upgraders;
using Sentry;

namespace Sandbox
{
	public partial class Hotload
	{
		private readonly Dictionary<Type, Type> SubstituteTypeCache = new Dictionary<Type, Type>();
		private readonly Dictionary<Type, InstanceTimingEntry> TypeTimings = new Dictionary<Type, InstanceTimingEntry>();
		private readonly Dictionary<IInstanceProcessor, InstanceTimingEntry> ProcessorTimings = new Dictionary<IInstanceProcessor, InstanceTimingEntry>();

		private readonly Queue<InstanceTask> DefaultInstanceTaskQueue = new Queue<InstanceTask>();
		private readonly Queue<InstanceTask> LateInstanceTaskQueue = new Queue<InstanceTask>();

		private readonly Dictionary<Assembly, (Type Type, FieldInfo[] Fields)[]> StaticFieldCache = new();

		private HotloadResult CurrentResult;

		private ReferencePath CurrentPath;
		private FieldInfo CurrentSrcField;
		private FieldInfo CurrentDstField;

		/// <summary>
		/// Merge chains of swaps. For example, if A is swapped with B, and B is swapped with C, simplify to A swapping with C.
		/// </summary>
		private void SimplifySwaps()
		{
			foreach ( var oldAsm in Swaps.Keys.ToArray() )
			{
				var newAsm = Swaps[oldAsm];
				var count = Swaps.Count;

				while ( newAsm != null && Swaps.TryGetValue( newAsm, out var nextAsm ) )
				{
					if ( count-- < 0 )
					{
						Log( HotloadEntryType.Error, $"Assembly swap cycle detected involving {FormatAssemblyName( oldAsm )}." );
						newAsm = oldAsm;
						break;
					}

					newAsm = nextAsm;
				}

				Swaps[oldAsm] = newAsm;
			}
		}

		/// <summary>
		/// Cycle though all types in all watched assemblies.
		/// Find statics, iterate over all their fields recursively.
		/// Replace any instances of classes that are defined in the assemblies added using ReplacingAssembly
		/// </summary>
		public HotloadResult UpdateReferences()
		{
			// If there's nothing to do, early out
			if ( Swaps.Count == 0 )
				return HotloadResult.NoActionSingleton;

			CurrentResult = new HotloadResult();

			TypeTimings.Clear();
			ProcessorTimings.Clear();

			var timer = new Stopwatch();
			timer.Start();

			SimplifySwaps();

			// Remove cached static fields for assemblies that aren't watched any more
			foreach ( var cachedAsm in StaticFieldCache.Keys.ToArray() )
			{
				if ( Swaps.ContainsKey( cachedAsm ) || !_watchedAssemblies.ContainsKey( cachedAsm ) )
				{
					StaticFieldCache.Remove( cachedAsm );
				}
			}

			// Make sure instance upgraders are set up
			foreach ( var upgrader in AllUpgraders.Values )
			{
				if ( !upgrader.IsInitialized )
					upgrader.Initialize( this );
			}

			var defaultUpgrader = GetUpgrader<DefaultUpgrader>();

			try
			{
				RootUpgraderGroup.HotloadStart();

				var staticFieldStartTime = timer.Elapsed;
				var watchedAssemblies = _watchedAssemblies.Keys
					.Union( Swaps.Keys )
					.Union( New.Where( x => !Swaps.ContainsValue( x ) ) );

				foreach ( var (type, fields) in watchedAssemblies.SelectMany( GetWatchedFields ) )
				{
					UpdateReferencesInType( type, fields );
				}

				CurrentResult.StaticFieldTime = (timer.Elapsed - staticFieldStartTime).TotalMilliseconds;

				var watchedInstanceStartTime = timer.Elapsed;

				foreach ( var instance in WatchedInstances )
				{
					CurrentPath = ReferencePath.GetRoot( instance.GetType() );
					defaultUpgrader.ProcessObjectFields( instance );
					CurrentPath = null;
				}

				CurrentResult.WatchedInstanceTime = (timer.Elapsed - watchedInstanceStartTime).TotalMilliseconds;

				var instanceQueueStartTime = timer.Elapsed;

				ProcessInstanceQueue();

				CurrentResult.InstanceQueueTime = (timer.Elapsed - instanceQueueStartTime).TotalMilliseconds;
			}
			finally
			{
				RootUpgraderGroup.HotloadComplete();

				if ( TryGetUpgrader<AutoSkipUpgrader>( out var upgrader ) )
				{
					CurrentResult.AutoSkippedTypes.AddRange( upgrader
						.SkippedTypes
						.Select( x => x.ToSimpleString() ) );
				}

				ClearFieldDefaults();
				DefaultInstanceTaskQueue.Clear();
				LateInstanceTaskQueue.Clear();
				SubstituteTypeCache.Clear();
				ScopeMethodOrdinals.Clear();
				AnonymousTypes.Clear();
				RootUpgraderGroup.ClearCache();

				// update the watched assemblies if we swapped those

				foreach ( var swap in Swaps )
				{
					if ( swap.Key is null ) continue;
					if ( !_watchedAssemblies.TryGetValue( swap.Key, out var filter ) ) continue;

					UnwatchAssembly( swap.Key );

					if ( swap.Value is null ) continue;

					WatchAssembly( swap.Value, filter );
				}

				foreach ( var asm in New )
				{
					WatchAssembly( asm );
				}

				Swaps.Clear();
				New.Clear();
			}

			CurrentResult.ProcessingTime = timer.Elapsed.TotalMilliseconds;

			return CurrentResult;
		}

		private void ScheduleInstanceTask( IInstanceProcessor handler, object oldInstance, object newInstance )
		{
			DefaultInstanceTaskQueue.Enqueue( new InstanceTask( handler, oldInstance, newInstance, CurrentSrcField, CurrentDstField, CurrentPath ) );
		}

		private void ScheduleLateInstanceTask( IInstanceProcessor handler, object oldInstance, object newInstance )
		{
			LateInstanceTaskQueue.Enqueue( new InstanceTask( handler, oldInstance, newInstance, CurrentSrcField, CurrentDstField, CurrentPath ) );
		}

		private void ProcessInstanceQueue()
		{
			var metaSw = new Stopwatch();
			var sw = new Stopwatch();

			while ( DefaultInstanceTaskQueue.TryDequeue( out var task ) || LateInstanceTaskQueue.TryDequeue( out task ) )
			{
				sw.Restart();

				CurrentPath = task.Path;
				CurrentSrcField = task.SrcField;
				CurrentDstField = task.DstField;

				var instanceCount = task.Handler.ProcessInstance( task.OldInstance, task.NewInstance );
				var instanceType = task.OldInstance.GetType();

				CurrentPath = null;
				CurrentSrcField = null;
				CurrentDstField = null;

				var elapsed = sw.Elapsed;

				metaSw.Start();

				CurrentResult.InstancesProcessed += instanceCount;

				//
				// record timing information
				// this should help diagnose potentially slow classes
				// that maybe don't need hotloading, so you can disable with
				// the Skip attribute
				//
				{
					string rootString = null;

					if ( IncludeTypeTimings )
					{
						if ( !TypeTimings.TryGetValue( instanceType, out var tt ) )
						{
							var typeName = instanceType.ToSimpleString();

							// Check for type name collisions
							if ( !CurrentResult.TypeTimings.TryGetValue( typeName, out tt ) )
							{
								CurrentResult.TypeTimings.Add( typeName,
									tt = new InstanceTimingEntry( TraceRoots ) );
							}

							TypeTimings.Add( instanceType, tt );
						}

						AddTiming( tt, instanceCount, elapsed, TraceRoots ? task.Path : null, ref rootString );
					}

					if ( IncludeProcessorTimings )
					{
						if ( !ProcessorTimings.TryGetValue( task.Handler, out var pt ) )
						{
							var typeName = task.Handler.GetType().ToSimpleString();

							// Check for type name collisions
							if ( !CurrentResult.ProcessorTimings.TryGetValue( typeName, out pt ) )
							{
								CurrentResult.ProcessorTimings.Add( typeName, pt = new InstanceTimingEntry( TraceRoots ) );
							}

							ProcessorTimings.Add( task.Handler, pt );
						}

						AddTiming( pt, instanceCount, elapsed, TraceRoots ? task.Path : null, ref rootString );
					}
				}

				metaSw.Stop();
			}

			CurrentResult.DiagnosticsTime = metaSw.Elapsed.TotalMilliseconds;
		}

		private void AddTiming( InstanceTimingEntry entry, int instanceCount, TimeSpan elapsed, ReferencePath path, ref string rootString )
		{
			entry.Instances += instanceCount;
			entry.Milliseconds += elapsed.TotalMilliseconds;

			if ( TraceRoots && path != null )
			{
				rootString ??= path.Root.ToString();

				if ( entry.Roots.TryGetValue( rootString, out var rootTiming ) )
				{
					rootTiming.Instances += instanceCount;
					rootTiming.Milliseconds += elapsed.TotalMilliseconds;
				}
				else
				{
					entry.Roots.Add( rootString, new TimingEntry( instanceCount, elapsed ) );
				}
			}
		}

		private static bool IsMatchingName( AssemblyName a, AssemblyName b )
		{
			return string.Equals( a.Name, b.Name, StringComparison.OrdinalIgnoreCase ) &&
				(a.Version?.Equals( b.Version ) ?? false);
		}

		private IEnumerable<(Type Type, FieldInfo[] Fields)> GetWatchedFields( Assembly asm )
		{
			if ( Swaps.ContainsKey( asm ) )
			{
				return GetWatchedFieldsUncached( asm, true );
			}

			if ( !New.Contains( asm ) )
			{
				var references = asm.GetReferencedAssemblies();

				// For assemblies that aren't getting swapped, check for references to swapped assemblies.

				foreach ( var reference in references )
				{
					var matchingSwap = Swaps.SingleOrDefault( x => IsMatchingName( x.Key.GetName(), reference ) );

					if ( matchingSwap.Key == null )
					{
						continue;
					}

					if ( matchingSwap.Value != null && references.Any( x => IsMatchingName( x, matchingSwap.Value.GetName() ) ) )
					{
						// Both the old and new versions of a swapped assembly are referenced by asm.
						// This will probably only happen in Sandbox.Test.Unit's hotload tests, and it's intentional.

						Log( HotloadEntryType.Information, $"Both old and new versions of an assembly are referenced by a non-swapped assembly. ({FormatAssemblyName( asm )} references {FormatAssemblyName( matchingSwap.Key )} and {FormatAssemblyName( matchingSwap.Value )})" );
						continue;
					}

					// A reference to an assembly that's getting swapped was found.
					// This is technically a fault, but we'll let it slide with a message for now since tools relies on it.

					Log( HotloadEntryType.Information, $"Skipping static fields from a non-swapped assembly that references a swapped assembly. ({FormatAssemblyName( asm )} references {FormatAssemblyName( matchingSwap.Key )})" );
					return Enumerable.Empty<(Type, FieldInfo[])>();
				}
			}

			if ( StaticFieldCache.TryGetValue( asm, out var cached ) )
			{
				return cached;
			}

			cached = GetWatchedFieldsUncached( asm, false ).ToArray();
			StaticFieldCache.Add( asm, cached );

			return cached;
		}

		private IEnumerable<(Type Type, FieldInfo[] Fields)> GetWatchedFieldsUncached( Assembly asm, bool isFromSwappedAsm )
		{
			if ( IsAssemblyIgnored( asm ) ) yield break;

			var filter = _watchedAssemblies.GetValueOrDefault( asm );

			foreach ( var type in asm.GetTypes() )
			{
				if ( filter?.Invoke( type ) is false ) continue;

				var fields = GetWatchedFields( type, isFromSwappedAsm ).ToArray();

				if ( fields.Length == 0 ) continue;

				yield return (type, fields);
			}
		}

		private IEnumerable<FieldInfo> GetWatchedFields( Type type, bool isFromSwappedAsm )
		{
			var typeInfo = type.GetTypeInfo();
			var skipUpgrader = GetUpgrader<SkipUpgrader>();
			var autoSkipUpgrader = TryGetUpgrader<AutoSkipUpgrader>( out var upgrader ) ? upgrader : null;

			// Ignore anything in the hotload assembly
			if ( IsAssemblyIgnored( typeInfo.Assembly ) ) yield break;

			// TODO: Currently cannot find static fields in instances of generic types
			if ( typeInfo.ContainsGenericParameters ) yield break;

			// Type definition has a SkipAttribute 
			if ( typeInfo.GetCustomAttribute<SkipHotloadAttribute>() != null ) yield break;

			// Attempting to change fields in <PrivateImplementationDetails> will fail
			if ( type.Name == "<PrivateImplementationDetails>" ) yield break;

			// Skip static compiler generated types (dynamic caches, etc?)
			if ( typeInfo.IsSealed && isFromSwappedAsm && DelegateUpgrader.IsCompilerGenerated( type ) ) yield break;

			const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

			// Iterate all statics
			foreach ( var staticField in type.GetFields( flags ) )
			{
				// Ignore removed or const values
				if ( staticField.IsLiteral ) continue;

				// Ignore if marked with SkipAttribute 
				if ( staticField.HasAttribute<SkipHotloadAttribute>() ) continue;

				if ( !isFromSwappedAsm && staticField.FieldType.IsSealed )
				{
					if ( skipUpgrader.ShouldProcessType( staticField.FieldType ) ) continue;
					if ( autoSkipUpgrader?.ShouldProcessType( staticField.FieldType ) ?? false ) continue;
				}

				yield return staticField;
			}
		}

		internal void UpdateReferencesInType( Type t, FieldInfo[] staticFields )
		{
			var subType = GetNewType( t );

			// Ignore removed types
			if ( subType == null ) return;

			// Iterate all statics
			foreach ( var staticField in staticFields )
			{
				var newField = subType != t
					? subType.GetField( staticField.Name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly )
					: staticField;

				// Ignore removed fields
				if ( newField == null ) continue;

				// Ignore const values
				if ( newField.IsLiteral ) continue;

				// Ignore if marked with SkipAttribute 
				if ( newField.HasAttribute<SkipHotloadAttribute>() ) continue;

				object curVal;

				try
				{
					curVal = staticField.GetValue( null );
				}
				catch ( TargetInvocationException e )
				{
					if ( e.InnerException is TypeInitializationException )
					{
						Log( HotloadEntryType.Warning, $"{e.InnerException}", t );
						break;
					}

					Log( e.InnerException, member: t );
					continue;
				}

				if ( curVal is null ) continue;

				if ( staticField.TryGetBackedProperty( out var staticProperty ) )
				{
					CurrentPath = ReferencePath.GetRoot( staticProperty );
				}
				else
				{
					CurrentPath = ReferencePath.GetRoot( staticField );
				}

				CurrentSrcField = staticField;
				CurrentDstField = newField;

				try
				{
					if ( newField.IsInitOnly )
					{
						// We can't assign a new value to this field, so fall
						// back to upgrading the new instance in-place

						var newVal = newField.GetValue( null );

						if ( newVal is null ) continue;

						RootUpgraderGroup.TryUpgradeInstance( curVal, newVal );
						continue;
					}

					var curValType = curVal.GetType();

					if ( staticField == newField && !curValType.IsValueType && !IsSwappedType( curValType ) )
					{
						// We're processing a static field in a non-swapped type, and the value type isn't
						// swapped either. Try upgrading in-place.

						if ( RootUpgraderGroup.TryUpgradeInstance( curVal, curVal ) )
						{
							continue;
						}
					}

					// Default to creating a new instance to upgrade the old one to

					newField.SetValue( null, GetNewInstance( curVal ) );
				}
#if !HOTLOAD_NOCATCH
				catch ( Exception e )
				{
					Log( e, member: newField );
				}
#endif
				finally
				{
					CurrentPath = null;
					CurrentSrcField = null;
					CurrentDstField = null;
				}
			}
		}

		private object GetNewInstance( object oldInstance )
		{
			if ( oldInstance == null )
				return null;

			// Check for specific InstanceUpgraders for handling special types
			if ( RootUpgraderGroup.TryCreateNewInstance( oldInstance, out var newInstance ) )
			{
				return newInstance;
			}

			// We shouldn't get here, the DefaultUpgrader will always be able to create new instances
			throw new NotImplementedException();
		}

		private void ReportSwappedOutAssemblyReference( Type type, Assembly oldAsm )
		{
			Log( HotloadEntryType.Warning, $"Unable to find a type substitution without referencing a swapped-out assembly ({FormatAssemblyName( oldAsm )}).", type );
		}

		/// <summary>
		/// Make sure a candidate type substitution isn't at all defined in a swapped-out assembly.
		/// This can go wrong if assembly B references assembly A, but only assembly A was swapped.
		/// </summary>
		private bool ValidateNewType( Type newType )
		{
			foreach ( var interfaceType in newType.GetInterfaces() )
			{
				if ( Swaps.ContainsKey( interfaceType.Assembly ) )
				{
					ReportSwappedOutAssemblyReference( newType, interfaceType.Assembly );
					return false;
				}
			}

			var baseType = newType;

			while ( baseType != null )
			{
				if ( Swaps.ContainsKey( baseType.Assembly ) )
				{
					ReportSwappedOutAssemblyReference( newType, baseType.Assembly );
					return false;
				}

				baseType = baseType.BaseType;
			}

			return true;
		}

		private Type GetSubstituteType( Assembly asm, Type oldType )
		{
			if ( asm == null )
			{
				// Assembly was unloaded in this swap
				return null;
			}

			if ( GeneratedName.TryParse( oldType.Name, out var name ) )
			{
				switch ( name.Kind )
				{
					case GeneratedNameKind.LambdaDisplayClass:
					case GeneratedNameKind.StateMachineType:
						return GetNewNestedGeneratedType( oldType, name );

					case GeneratedNameKind.ReadOnlyListType:
						return GetNewReadOnlyListType( oldType );

					case GeneratedNameKind.AnonymousType:
						return GetNewAnonymousType( oldType );

					default:
						Log( HotloadEntryType.Error, $"Unhandled compiler-generated type", oldType );
						return null;
				}
			}

			if ( oldType.FullName == null )
			{
				if ( oldType.DeclaringType == null ) return null;
				var newDeclType = GetSubstituteType( asm, oldType.DeclaringType );

				if ( oldType.IsGenericTypeParameter )
				{
					Assert.NotNull( oldType.DeclaringType );

					return newDeclType?.GetGenericArguments()[oldType.GenericParameterPosition];
				}

				if ( oldType.IsGenericMethodParameter )
				{
					Assert.NotNull( oldType.DeclaringMethod );

					var newMethod = GetNewInstance( oldType.DeclaringMethod ) as MethodBase;

					return newMethod?.GetGenericArguments()[oldType.GenericParameterPosition];
				}

				return newDeclType?.GetNestedType( oldType.Name, BindingFlags.Public | BindingFlags.NonPublic );
			}

			var sub = asm.GetType( oldType.FullName );

			if ( sub != null )
			{
				if ( !ValidateNewType( sub ) )
				{
					return null;
				}

				return sub;
			}

			// TODO

			return null;
		}

		/// <summary>
		/// In a swapped assembly find a replacement type for this type.
		/// Return null if no replacement is found.
		/// </summary>
		private Type GetNewType( Type oldType )
		{
			if ( SubstituteTypeCache.TryGetValue( oldType, out var newType ) ) return newType;

			if ( IsSwappedType( oldType ) )
			{
				var typeInfo = oldType.GetTypeInfo();
				var typeAssembly = typeInfo.Assembly;

				if ( typeInfo.IsArray )
				{
					newType = GetNewArrayType( oldType );
				}
				else if ( typeInfo.IsByRef )
				{
					newType = GetNewByRefType( oldType );
				}
				else if ( typeInfo.IsConstructedGenericType )
				{
					newType = GetNewGenericType( oldType );
				}
				else
				{
					newType = Swaps.TryGetValue( typeAssembly, out var swapAssembly )
						? GetSubstituteType( swapAssembly, oldType ) : oldType;
				}
			}
			else if ( ValidateNewType( oldType ) )
			{
				newType = oldType;
			}
			else
			{
				newType = null;
			}

			SubstituteTypeCache.Add( oldType, newType );
			return newType;
		}

		private Type GetNewArrayType( Type oldType )
		{
			var oldElemType = oldType.GetElementType();
			var newElemType = GetNewType( oldElemType );

			if ( newElemType == null )
			{
				return null;
			}

			if ( newElemType == oldElemType )
			{
				return oldType;
			}

			var rank = oldType.GetArrayRank();

			return rank == 1 ? newElemType.MakeArrayType() : newElemType.MakeArrayType( rank );
		}

		private Type GetNewByRefType( Type oldType )
		{
			var oldElemType = oldType.GetElementType();
			var newElemType = GetNewType( oldElemType );

			if ( newElemType == null )
			{
				return null;
			}

			if ( newElemType == oldElemType )
			{
				return oldType;
			}

			return newElemType.MakeByRefType();
		}

		private Type GetNewGenericType( Type oldType )
		{
			var oldDefType = oldType.GetGenericTypeDefinition();
			var newDefType = GetNewType( oldDefType );

			if ( newDefType == null )
			{
				return null;
			}

			var madeSubstitution = newDefType != oldDefType;

			var args = oldType.GetGenericArguments();

			for ( var i = 0; i < args.Length; ++i )
			{
				var argSubType = GetNewType( args[i] );

				if ( argSubType == null )
				{
					return null;
				}

				madeSubstitution |= argSubType != args[i];

				args[i] = argSubType;
			}

			return madeSubstitution ? newDefType.MakeGenericType( args ) : oldType;
		}

		private bool IsSwappedType( Type type )
		{
			if ( type == null )
			{
				return false;
			}

			if ( type.IsArray || type.IsByRef )
			{
				return IsSwappedType( type.GetElementType() );
			}

			if ( type.IsConstructedGenericType )
			{
				if ( IsSwappedType( type.GetGenericTypeDefinition() ) )
				{
					return true;
				}

				return type.GetGenericArguments().Any( IsSwappedType );
			}

			return Swaps.ContainsKey( type.Assembly );
		}

		private bool AreEqualTypes( Type a, Type b )
		{
			if ( a == b ) return true;
			if ( a == null || b == null ) return false;

			if ( a.IsArray && b.IsArray )
			{
				if ( a.GetArrayRank() != b.GetArrayRank() )
				{
					return false;
				}

				return AreEqualTypes( a.GetElementType(), b.GetElementType() );
			}

			if ( a.IsByRef && b.IsByRef )
			{
				return AreEqualTypes( a.GetElementType(), b.GetElementType() );
			}

			if ( a.IsGenericType && b.IsGenericType )
			{
				if ( !AreEqualTypes( a.GetGenericTypeDefinition(), b.GetGenericTypeDefinition() ) )
				{
					return false;
				}

				var aArgs = a.GetGenericArguments();
				var bArgs = b.GetGenericArguments();

				if ( aArgs.Length != bArgs.Length )
				{
					return false;
				}

				for ( var i = 0; i < aArgs.Length; ++i )
				{
					if ( !AreEqualTypes( aArgs[i], bArgs[i] ) )
					{
						return false;
					}
				}

				return true;
			}

			return false;
		}

		private bool AreEquivalentTypes( Type oldType, Type newType )
		{
			if ( oldType == newType ) return true;
			if ( oldType == null || newType == null ) return false;

			if ( oldType.IsArray != newType.IsArray ) return false;
			if ( oldType.IsArray && newType.IsArray )
			{
				if ( oldType.GetArrayRank() != newType.GetArrayRank() )
				{
					return false;
				}

				return AreEquivalentTypes( oldType.GetElementType(), newType.GetElementType() );
			}

			if ( oldType.IsByRef != newType.IsByRef ) return false;
			if ( oldType.IsByRef && newType.IsByRef )
			{
				return AreEquivalentTypes( oldType.GetElementType(), newType.GetElementType() );
			}

			if ( oldType.IsConstructedGenericType != newType.IsConstructedGenericType ) return false;
			if ( oldType.IsConstructedGenericType && newType.IsConstructedGenericType )
			{
				if ( !AreEquivalentTypes( oldType.GetGenericTypeDefinition(), newType.GetGenericTypeDefinition() ) )
				{
					return false;
				}

				var aArgs = oldType.GetGenericArguments();
				var bArgs = newType.GetGenericArguments();

				if ( aArgs.Length != bArgs.Length )
				{
					return false;
				}

				for ( var i = 0; i < aArgs.Length; ++i )
				{
					if ( !AreEquivalentTypes( aArgs[i], bArgs[i] ) )
					{
						return false;
					}
				}

				return true;
			}

			if ( oldType.IsGenericMethodParameter != newType.IsGenericMethodParameter ) return false;
			if ( oldType.IsGenericMethodParameter && newType.IsGenericMethodParameter )
			{
				// TODO: constraints

				return oldType.GenericParameterPosition == newType.GenericParameterPosition;
			}

			if ( oldType.IsGenericTypeParameter != newType.IsGenericTypeParameter ) return false;
			if ( oldType.IsGenericTypeParameter && newType.IsGenericTypeParameter )
			{
				// TODO: constraints

				return oldType.GenericParameterPosition == newType.GenericParameterPosition;
			}

			var subType = GetNewType( oldType );

			return subType != oldType && AreEqualTypes( subType, newType );
		}

		private void AddResultEntry( HotloadResultEntry entry )
		{
			CurrentResult?.AddEntry( entry );

			if ( _logger == null )
			{
				return;
			}

			using var scope = SentrySdk.PushScope();

			SentrySdk.ConfigureScope( x =>
			{
				x.Contexts["Source Member"] = new
				{
					FullName = entry.MemberString,
					Path = entry.Path?.ToString()
				};
			} );

			var details = entry.Message;

			if ( entry.Member != null )
			{
				details = $"{details}\n  Member: {entry.MemberString}";
			}

			if ( entry.Path != null )
			{
				details = $"{details}\n  Path: {entry.Path}";
			}

			switch ( entry.Type )
			{
				case HotloadEntryType.Trace:
					_logger.Trace( details );
					break;

				case HotloadEntryType.Information:
					_logger.Info( details );
					break;

				case HotloadEntryType.Warning:
					_logger.Warning( entry.Exception, details );
					break;

				case HotloadEntryType.Error:
					_logger.Error( entry.Exception, details );
					break;
			}
		}

		internal void Log( HotloadEntryType type, FormattableString message, MemberInfo member = null )
		{
			AddResultEntry( new HotloadResultEntry( type, message, member, TracePaths ? CurrentPath : null ) );
		}

		internal void Log( Exception exception, FormattableString message = null, MemberInfo member = null )
		{
			AddResultEntry( new HotloadResultEntry( exception, message, member, TracePaths ? CurrentPath : null ) );
		}
	}
}
