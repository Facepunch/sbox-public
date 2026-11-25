namespace Sandbox;

internal static class ReflectionUtility
{
	public static void RunAllStaticConstructors( string assemblyName )
	{
		var asm = Assembly.Load( assemblyName );
		RunAllStaticConstructors( asm );
	}

	public static void RunAllStaticConstructors( Assembly asm )
	{
		List<Exception> exceptions = null;

		foreach ( var t in asm.GetTypes() )
		{
			try
			{
				System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor( t.TypeHandle );
			}
			catch ( Exception ex )
			{
				exceptions ??= new List<Exception>();
				exceptions.Add( ex );
			}
		}

		if ( exceptions is not { Count: > 0 } ) return;

		throw exceptions.Count == 1 ? exceptions[0] : new AggregateException( exceptions );
	}

	/// <summary>
	/// Empty the runtime's static cache of generic method instantiations made through reflection
	/// (<c>RuntimeTypeCache.s_methodInstantiations</c>) when it holds one tied to a collectible assembly. An instantiation
	/// whose own types are all non-collectible is cached there for good, keyed by a method info that holds the type it was
	/// reflected through: one reflected through a game's type (<c>Component.__sync_GetValue&lt;bool&gt;</c> looked up on a game
	/// component) keeps that game's assembly context, its statics and its last scene alive after it closes. The cache starts
	/// empty and fills again as it's used, and generic method infos compare by method, not identity. True if it was emptied.
	/// </summary>
	public static bool ForgetCollectibleMethodInstantiations()
	{
		var cache = typeof( Type ).Assembly.GetType( "System.RuntimeType+RuntimeTypeCache" );
		var field = cache?.GetField( "s_methodInstantiations", BindingFlags.NonPublic | BindingFlags.Static );
		if ( field is null || field.IsInitOnly || !field.FieldType.IsValueType ) return false;

		if ( !MethodInstantiations( field ).Any( TiedToCollectible ) ) return false;

		field.SetValue( null, Activator.CreateInstance( field.FieldType ) );
		return true;
	}

	/// <summary>The methods in the runtime's static instantiation cache, for <see cref="ForgetCollectibleMethodInstantiations"/> and tests.</summary>
	internal static IEnumerable<MethodInfo> MethodInstantiations( FieldInfo field = null )
	{
		field ??= typeof( Type ).Assembly.GetType( "System.RuntimeType+RuntimeTypeCache" )?.GetField( "s_methodInstantiations", BindingFlags.NonPublic | BindingFlags.Static );
		var table = field?.GetValue( null );
		var inner = table?.GetType().GetField( "m_Table", BindingFlags.NonPublic | BindingFlags.Instance )?.GetValue( table );
		var keysField = inner?.GetType().GetFields( BindingFlags.NonPublic | BindingFlags.Instance ).FirstOrDefault( f => f.FieldType.IsArray && f.Name.Contains( "key", StringComparison.OrdinalIgnoreCase ) );
		if ( keysField?.GetValue( inner ) is not Array keys ) return [];
		return keys.OfType<MethodInfo>().ToArray();
	}

	/// <summary>Whether a method was reflected through, declared on or instantiated over a type from a collectible assembly.</summary>
	internal static bool TiedToCollectible( MethodInfo method )
	{
		static bool Collectible( Type type ) => type is not null && type.Assembly.IsCollectible;
		return Collectible( method.ReflectedType ) || Collectible( method.DeclaringType ) || method.GetGenericArguments().Any( Collectible );
	}

	/// <summary>
	/// Finds all static fields assignable to <paramref name="targetType"/> across all types
	/// in the given assembly and sets them to null. Useful for releasing references during teardown.
	/// </summary>
	public static void NullStaticReferencesOfType( Assembly asm, Type targetType )
	{
		var logger = Logging.GetLogger( "Reflection" );

		// GetTypes() can throw ReflectionTypeLoadException on assemblies
		// whose AssemblyLoadContext has started unloading. Fall back to
		// the subset of types that loaded successfully.
		Type[] types;

		try
		{
			types = asm.GetTypes();
		}
		catch ( ReflectionTypeLoadException e )
		{
			types = e.Types.Where( t => t is not null ).ToArray();
		}

		foreach ( var type in types )
		{
			if ( type.IsGenericTypeDefinition ) continue;

			var fields = type.GetFields( BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic );

			foreach ( var field in fields )
			{
				if ( field.IsLiteral ) continue; // skip constants

				try
				{
					// Direct match — null the field itself
					if ( targetType.IsAssignableFrom( field.FieldType ) )
					{
						if ( field.IsInitOnly )
						{
							// This is a tiny bit dangerous ideally we just dont have any readonly statuc fields holding on to resources
							logger.Warning( $"NullStaticReferences: force-clearing readonly field {type.Name}.{field.Name} ({targetType.Name}) — consider removing 'readonly'" );
							if ( !ForceNullStaticField( field ) ) logger.Warning( $"Failed to force-clear readonly {type.Name}.{field.Name}" );
							continue;
						}

						field.SetValue( null, null );
						continue;
					}

					if ( !TryClearCollectionOfType( field.GetValue( null ), targetType ) ) continue;
				}
				catch ( Exception e )
				{
					logger.Warning( e, $"Failed to clean {type.Name}.{field.Name}" );
				}
			}
		}
	}

	/// <summary>
	/// If <paramref name="value"/> is a collection containing elements assignable to
	/// <paramref name="targetType"/>, remove those elements (or clear the whole collection).
	/// Returns true if any work was done.
	/// </summary>
	private static bool TryClearCollectionOfType( object value, Type targetType )
	{
		if ( value is null ) return false;

		var valueType = value.GetType();
		// Default ImmutableArray<T> throws when accessed through IList.
		if ( valueType.IsGenericType &&
			valueType.GetGenericTypeDefinition() == typeof( System.Collections.Immutable.ImmutableArray<> ) &&
			value.Equals( Activator.CreateInstance( valueType ) ) )
			return false;

		// Check if this is a generic collection whose element type matches
		if ( HasGenericElementOfType( valueType, targetType ) )
		{
			// IDictionary<K,V> — clear if either key or value type matches
			if ( value is System.Collections.IDictionary dict )
			{
				dict.Clear();
				return true;
			}

			// IList — remove matching items in reverse to avoid index shifting; arrays can't shrink, so theirs are nulled
			if ( value is System.Collections.IList list )
			{
				if ( list.IsReadOnly ) return false;

				for ( var i = list.Count - 1; i >= 0; i-- )
				{
					if ( list[i] is null || !targetType.IsInstanceOfType( list[i] ) ) continue;

					if ( list.IsFixedSize ) list[i] = null;
					else list.RemoveAt( i );
				}

				return true;
			}

			// Generic fallback — covers HashSet<T>, ConcurrentBag<T>, HashSetEx<T>, etc.
			// that don't implement IDictionary or IList but have a parameterless Clear().
			var clearMethod = valueType.GetMethod( "Clear", Type.EmptyTypes );
			if ( clearMethod is not null && !clearMethod.IsStatic )
			{
				clearMethod.Invoke( value, null );
				return true;
			}
		}

		// Multi-dimensional arrays can't be indexed through IList, and arrays of value types hold no references
		if ( value is Array array && (array.Rank != 1 || valueType.GetElementType().IsValueType) )
			return false;

		// Non-generic IList fallback (e.g. ArrayList) — remove matching items
		if ( value is System.Collections.IList nonGenericList && !nonGenericList.IsReadOnly )
		{
			var removed = false;

			for ( var i = nonGenericList.Count - 1; i >= 0; i-- )
			{
				if ( nonGenericList[i] is not null && targetType.IsInstanceOfType( nonGenericList[i] ) )
				{
					if ( nonGenericList.IsFixedSize ) nonGenericList[i] = null;
					else nonGenericList.RemoveAt( i );
					removed = true;
				}
			}

			return removed;
		}

		return false;
	}

	/// <summary>
	/// Nulls a static readonly (initonly) field by emitting a DynamicMethod that uses
	/// <c>stsfld</c> directly, bypassing the CLR verifier check that normal reflection
	/// enforces on initonly fields.
	/// </summary>
	private static bool ForceNullStaticField( FieldInfo field )
	{
		if ( field.DeclaringType is null ) return false;

		try
		{
			var dm = new System.Reflection.Emit.DynamicMethod(
				$"__ForceNull_{field.DeclaringType.Name}_{field.Name}",
				typeof( void ),
				Type.EmptyTypes,
				field.DeclaringType.Module,
				skipVisibility: true );

			var il = dm.GetILGenerator();
			il.Emit( System.Reflection.Emit.OpCodes.Ldnull );
			il.Emit( System.Reflection.Emit.OpCodes.Stsfld, field );
			il.Emit( System.Reflection.Emit.OpCodes.Ret );

			((Action)dm.CreateDelegate( typeof( Action ) ))();
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Returns true if the type itself or any of its implemented generic interfaces
	/// (IEnumerable&lt;T&gt;, IDictionary&lt;K,V&gt;, etc.) has a generic argument assignable
	/// to <paramref name="targetType"/>. This covers types like <c>HashSetEx&lt;T&gt;</c>
	/// that don't implement standard collection interfaces directly.
	/// </summary>
	private static bool HasGenericElementOfType( Type collectionType, Type targetType )
	{
		// Check the type's own generic arguments first (e.g. HashSetEx<T>, HashSet<T>)
		if ( collectionType.IsGenericType )
		{
			foreach ( var arg in collectionType.GetGenericArguments() )
			{
				if ( targetType.IsAssignableFrom( arg ) )
					return true;
			}
		}

		foreach ( var iface in collectionType.GetInterfaces() )
		{
			if ( !iface.IsGenericType ) continue;

			foreach ( var arg in iface.GetGenericArguments() )
			{
				if ( targetType.IsAssignableFrom( arg ) )
					return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Pre-compile all methods in the assembly on a background thread to reduce the risk of
	/// on-demand JIT stalls during gameplay. Returns immediately. Cancels automatically
	/// if the assembly's load context begins unloading, so type references are released
	/// promptly and don't delay hotloads.
	/// </summary>
	public static System.Threading.Tasks.Task PreJITAsync( Assembly asm )
	{
		var logger = Logging.GetLogger( "PreJit" );
		var alc = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( asm );

		// cts is always disposed in the Task's finally block below.
#pragma warning disable CA2000
		var cts = new System.Threading.CancellationTokenSource();
#pragma warning restore CA2000
		Action<System.Runtime.Loader.AssemblyLoadContext> unloadHandler = null;

		if ( alc is not null && alc != System.Runtime.Loader.AssemblyLoadContext.Default )
		{
			// The unload can run this as the task finishes and disposes cts: the handler list was taken before it was removed
			unloadHandler = _ =>
			{
				try { cts.Cancel(); }
				catch ( ObjectDisposedException ) { }
			};
			alc.Unloading += unloadHandler;
		}

		return System.Threading.Tasks.Task.Run( () =>
		{
			try
			{
				Type[] types;
				try
				{
					types = asm.GetTypes();
				}
				catch ( ReflectionTypeLoadException e )
				{
					types = e.Types.Where( t => t is not null ).ToArray();
				}

				const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;

				foreach ( var t in types )
				{
					if ( cts.IsCancellationRequested ) break;
					if ( t.IsGenericTypeDefinition ) continue;
					if ( t.BaseType == typeof( MulticastDelegate ) ) continue;

					try
					{
						foreach ( var method in t.GetMethods( flags ) )
						{
							if ( method.IsAbstract ) continue;
							if ( method.ContainsGenericParameters ) continue;

							// Preparing a p/invoke resolves its native library right here, which
							// throws for anything that isn't shipped on this platform. There's no
							// managed code to JIT anyway, so there's nothing to gain by trying.
							if ( (method.Attributes & MethodAttributes.PinvokeImpl) != 0 ) continue;

							try
							{
								System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod( method.MethodHandle );
							}
							catch ( Exception e )
							{
								logger.Warning( e, $"{e.Message} - {t}.{method}" );
							}
						}

						foreach ( var ctor in t.GetConstructors( flags ) )
						{
							if ( ctor.ContainsGenericParameters ) continue;

							try
							{
								System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod( ctor.MethodHandle );
							}
							catch ( Exception e )
							{
								logger.Warning( e, $"{e.Message} - {t}.{ctor}" );
							}
						}
					}
					catch ( Exception e )
					{
						logger.Warning( e, $"PreJIT failed for type {t}" );
					}
				}
			}
			finally
			{
				if ( unloadHandler is not null )
					alc.Unloading -= unloadHandler;

				cts.Dispose();
			}
		} );
	}
}
