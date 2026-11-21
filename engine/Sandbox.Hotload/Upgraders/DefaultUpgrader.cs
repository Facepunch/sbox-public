using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Sandbox.Upgraders;

#nullable enable

/// <summary>
/// This field is initialized by a named method, for use when hotloading.
/// If no method name is given, the field is deliberately left uninitialized.
/// </summary>
[AttributeUsage( AttributeTargets.Field )]
public sealed class InitializedByAttribute : Attribute
{
	public string? MethodName { get; }

	public InitializedByAttribute( string? methodName )
	{
		MethodName = methodName;
	}
}

/// <summary>
/// This upgrader will use reflection to go through each field of a new instance, and
/// populate it with an equivalent value found from the old instance. For newly-added
/// fields, it attempts to determine a default value from the constructor of the type.
/// </summary>
[UpgraderGroup( typeof( RootUpgraderGroup ), GroupOrder.Last )]
public class DefaultUpgrader : Hotload.InstanceUpgrader
{
	private record struct CompletionTask( IHotloadManaged? OldInstance, IHotloadManaged? NewInstance, Dictionary<string, object?>? State );

	private Stack<CompletionTask> CompletionTasks { get; } = new Stack<CompletionTask>();
	private List<Dictionary<string, object?>> StateDictPool { get; } = new List<Dictionary<string, object?>>();

	private Dictionary<(Type? OldType, Type NewType, bool CanSkip), (FieldInfo? SrcField, FieldInfo DstField)[]> FieldCache { get; } = new();

	private const int StateDictPoolCapacity = 1024;

	private SkipUpgrader SkipUpgrader { get; set; } = null!;
	private AutoSkipUpgrader AutoSkipUpgrader { get; set; } = null!;

	protected override void OnInitialize()
	{
		SkipUpgrader = GetUpgrader<SkipUpgrader>();
		AutoSkipUpgrader = GetUpgraderOrDefault<AutoSkipUpgrader>();
	}

	private bool TypeHasChanged( Type type )
	{
		return GetNewType( type ) != type;
	}

	protected override void OnHotloadStart()
	{
		var toRemove = FieldCache.Keys
			.Where( x => TypeHasChanged( x.NewType ) )
			.ToArray();

		foreach ( var key in toRemove )
		{
			FieldCache.Remove( key );
		}
	}

	protected override void OnClearCache()
	{
		var toRemove = FieldCache.Keys
			.Where( x => x.OldType != x.NewType )
			.ToArray();

		foreach ( var key in toRemove )
		{
			FieldCache.Remove( key );
		}
	}

	public override bool ShouldProcessType( Type type )
	{
		return true;
	}

	protected override void OnHotloadComplete()
	{
		// hotload done, notify everything that changed
		while ( CompletionTasks.TryPop( out var task ) )
		{
			try
			{
				if ( task.NewInstance != null )
				{
					task.NewInstance.Created( task.State );
				}
				else
				{
					task.OldInstance?.Failed();
				}

			}
#if !HOTLOAD_NOCATCH
			catch ( Exception e )
			{
				Log( e );
			}
#endif
			finally
			{

			}

			if ( task.State != null && StateDictPool.Count < StateDictPoolCapacity )
			{
				task.State.Clear();
				StateDictPool.Add( task.State );
			}
		}
	}

	protected override bool OnTryCreateNewInstance( object oldInstance, out object? newInstance )
	{
		var oldType = oldInstance.GetType();
		var newType = GetNewType( oldType );

		if ( newType == null )
		{
			if ( oldInstance is IHotloadManaged managed )
			{
				CompletionTasks.Push( new CompletionTask( managed, null, null ) );
			}

			newInstance = null;
			return true;
		}

		newInstance = newType != oldType ? RuntimeHelpers.GetUninitializedObject( newType ) : oldInstance;

		PostCreateInstance( oldInstance, newInstance );

		return true;
	}

	protected override bool OnTryUpgradeInstance( object oldInstance, object newInstance, bool createdElsewhere )
	{
		if ( oldInstance is Delegate || newInstance is Delegate )
		{
			return false;
		}

		SuppressFinalize( oldInstance, newInstance );

		if ( createdElsewhere )
		{
			PostCreateInstance( oldInstance, newInstance );
		}

		return true;
	}

	private void PostCreateInstance( object oldInstance, object newInstance )
	{
		if ( oldInstance.GetType().IsValueType )
		{
			// Value-types should be immediately processed
			ProcessInstance( oldInstance, newInstance );
			return;
		}

		AddCachedInstance( oldInstance, newInstance );

		ScheduleProcessInstance( oldInstance, newInstance );
	}

	private Dictionary<string, object?> GetStateDict()
	{
		if ( StateDictPool.Count > 0 )
		{
			var item = StateDictPool[^1];
			StateDictPool.RemoveAt( StateDictPool.Count - 1 );

			if ( item.Count > 0 )
			{
				Log( HotloadEntryType.Error, $"State dictionary has been modified after usage." );
				return new Dictionary<string, object?>();
			}

			return item;
		}

		return new Dictionary<string, object?>();
	}

	protected override int OnProcessInstance( object oldInstance, object newInstance )
	{
		var skipped = ReferenceEquals( oldInstance, newInstance );

		if ( skipped && oldInstance is IHotloadManaged managed )
		{
			try
			{
				managed.Persisted();
			}
#if !HOTLOAD_NOCATCH
			catch ( Exception e )
			{
				Log( e );
			}
#endif
			finally { }
		}

		ProcessObjectFields( oldInstance, newInstance );

		if ( skipped )
		{
			return 1;
		}

		Dictionary<string, object?>? state = null;

		if ( oldInstance is IHotloadManaged oldManaged )
		{
			try
			{
				state = GetStateDict();
				oldManaged.Destroyed( state );
			}
#if !HOTLOAD_NOCATCH
			catch ( Exception e )
			{
				Log( e );
			}
#endif
			finally { }
		}

		if ( newInstance is IHotloadManaged newManaged )
		{
			ProcessStateObject( state );

			state ??= GetStateDict();

			CompletionTasks.Push( new CompletionTask( oldInstance as IHotloadManaged, newManaged, state ) );
		}

		return 1;
	}

	[ThreadStatic]
	private static List<(string Key, object? Value)>? _sStateObjectReplacements;

	private void ProcessStateObject( Dictionary<string, object?>? state )
	{
		if ( state == null ) return;

		_sStateObjectReplacements ??= new List<(string, object?)>();

		foreach ( var pair in state )
		{
			if ( pair.Value == null ) continue;

			var type = pair.Value.GetType();
			if ( type.IsPrimitive )
			{
				continue;
			}

			var newInst = GetNewInstance( pair.Value );
			if ( ReferenceEquals( pair.Value, newInst ) ) continue;

			_sStateObjectReplacements.Add( (pair.Key, newInst) );
		}

		foreach ( var replacement in _sStateObjectReplacements )
		{
			state[replacement.Key] = replacement.Value;
		}
	}

	public static Regex BackingFieldRegex { get; } = new Regex( @"^<(?<name>[^>]+)>k__BackingField$" );

	/// <summary>
	/// Get all fields on this type, and types it inherits from, that we should process.
	/// </summary>
	private (FieldInfo? SrcField, FieldInfo DstField)[] GetFieldsToProcess( Type? oldType, Type newType, bool canSkip )
	{
		var key = (oldType, newType, canSkip);

		if ( FieldCache.TryGetValue( key, out var cached ) )
		{
			return cached;
		}

		cached = GetFieldsToProcessUncached( oldType, newType, canSkip ).ToArray();

		FieldCache.Add( key, cached );

		return cached;
	}

	/// <summary>
	/// For each type in <paramref name="newType"/>'s hierarchy, try to find a matching type in <paramref name="oldType"/>'s hierarchy.
	/// If no match is found, yields <c>(null, dstType)</c>. Ordered by most derived type first.
	/// </summary>
	private IEnumerable<(Type? SrcType, Type DstType)> MatchTypeHierarchies( Type? oldType, Type newType )
	{
		var oldBaseType = oldType;
		var newBaseType = newType;

		while ( newBaseType is not null )
		{
			if ( FindMatchingOldTypeInHierarchy( oldBaseType, newBaseType ) is { } matchingOldType )
			{
				yield return (matchingOldType, newBaseType);

				oldBaseType = matchingOldType.BaseType;
			}
			else
			{
				yield return (null, newBaseType);
			}

			newBaseType = newBaseType.BaseType;
		}
	}

	private Type? FindMatchingOldTypeInHierarchy( Type? oldType, Type newType )
	{
		while ( oldType is not null )
		{
			if ( GetNewType( oldType ) is { } newFromOldType )
			{
				if ( newFromOldType == newType ) return oldType;

				// Allow Example<T1> as a match for Example<T2>

				if ( newFromOldType.IsGenericType && newType.IsGenericType && newFromOldType.GetGenericTypeDefinition() == newType.GetGenericTypeDefinition() )
				{
					return oldType;
				}
			}

			oldType = oldType.BaseType;
		}

		return null;
	}

	/// <summary>
	/// For each field in <paramref name="newType"/>, try to find the matching field in <paramref name="oldType"/>. If no match is found,
	/// yields <c>(null, dstField)</c>, so we can initialize the new field to a default value.
	/// </summary>
	private IEnumerable<(FieldInfo? SrcField, FieldInfo DstField)> GetFieldsToProcessUncached( Type? oldType, Type newType, bool canSkip )
	{
		if ( canSkip && oldType != null )
		{
			if ( SkipUpgrader.ShouldProcessType( oldType ) ) yield break;
			if ( AutoSkipUpgrader?.ShouldProcessType( oldType ) ?? false ) yield break;
		}

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		foreach ( var (oldBaseType, newBaseType) in MatchTypeHierarchies( oldType, newType ).Reverse() )
		{
			foreach ( var dstField in newBaseType.GetFields( flags ) )
			{
				if ( dstField.DeclaringType != newBaseType ) continue;
				if ( dstField.FieldType.IsPointer ) continue;
				if ( dstField.HasAttribute<SkipHotloadAttribute>() ) continue;

				if ( canSkip && oldBaseType != null )
				{
					if ( dstField.FieldType.IsPrimitive ) continue;
					if ( dstField.FieldType.IsValueType || dstField.FieldType.IsSealed )
					{
						if ( SkipUpgrader.ShouldProcessType( dstField.FieldType ) ) continue;
						if ( AutoSkipUpgrader?.ShouldProcessType( dstField.FieldType ) ?? false ) continue;
					}
				}

				var srcField = oldBaseType == newBaseType ? dstField : oldBaseType?.GetField( dstField.Name, flags );

				if ( srcField != null )
				{
					var newSrcType = GetNewType( srcField.FieldType );

					// Check to see if the field has completely changed type, in which case handle it as a new field

					if ( newSrcType == null || !dstField.FieldType.IsAssignableFrom( newSrcType ) )
					{
						srcField = null;
						Log( HotloadEntryType.Warning, $"Field has changed type, so values of the old type will be discarded", dstField );
					}
				}

				yield return (srcField, dstField);
			}
		}
	}

	public void ProcessObjectFields( object instance )
	{
		ProcessObjectFields( instance, instance );
	}

	public void ProcessObjectFields( object oldInst, object newInst )
	{
		var oldType = oldInst.GetType();
		var newType = newInst.GetType();

		var sameInstance = ReferenceEquals( oldInst, newInst );

		var oldPath = CurrentPath;

		try
		{
			foreach ( var (srcField, dstField) in GetFieldsToProcess( oldType, newType, sameInstance ) )
			{
				if ( TracePaths )
				{
					CurrentPath = oldPath[dstField];
				}

				CurrentSrcField = srcField;
				CurrentDstField = dstField;

				try
				{
					object newValue;
					if ( srcField == null )
					{
						// An existing instance already has a value for this field. Only initialize fields on a new instance.
						if ( sameInstance ) continue;

						if ( dstField.GetCustomAttribute<InitializedByAttribute>() is { } initByAttrib )
						{
							if ( initByAttrib.MethodName is null )
							{
								// Deliberately not initialized
								continue;
							}

							var initMethod = dstField.DeclaringType!.GetMethod( initByAttrib.MethodName,
								BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );

							if ( initMethod is null )
							{
								Log( HotloadEntryType.Error, $"Invalid InitializedByAttribute, method not found.", dstField );
								continue;
							}

							initMethod.Invoke( newInst, null );
							continue;
						}

						if ( !TryGetDefaultValue( dstField, out newValue ) ) continue;
					}
					else
					{
						newValue = ProcessFieldValue( oldInst, srcField );
					}

					dstField.SetValue( newInst, newValue );
				}
#if !HOTLOAD_NOCATCH
				catch ( Exception e )
				{
					Log( e, member: dstField );
				}
#endif
				finally
				{

				}
			}
		}
		finally
		{
			CurrentPath = oldPath;

			CurrentSrcField = null;
			CurrentDstField = null;
		}
	}

	private object ProcessFieldValue( object oldInst, FieldInfo srcField )
	{
		var value = srcField.GetValue( oldInst );

		return GetNewInstance( value );
	}
}
