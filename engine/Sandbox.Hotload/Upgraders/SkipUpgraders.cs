using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Reflection;

namespace Sandbox.Upgraders
{
	[UpgraderGroup( typeof( ReferenceTypeUpgraderGroup ), GroupOrder.First ), AttemptBefore( typeof( CachedUpgrader ) )]
	public class SkipUpgrader : Hotload.InstanceUpgrader
	{
		/// <summary>
		/// Types that we can safely skip, that we can't add a <see cref="SkipHotloadAttribute"/> to.
		/// </summary>
		private static readonly HashSet<Type> AdditionalSkipableTypes = new HashSet<Type>()
		{
			typeof(string),
			typeof(Guid),
			typeof(Decimal),
			typeof(Dictionary<,>),
			typeof(List<>),
			typeof(Queue<>),
			typeof(ConcurrentQueue<>),
			typeof(HashSet<>),
			typeof(ConcurrentDictionary<,>),
			typeof(ConditionalWeakTable<,>),
			typeof(WeakReference<>)
		};

		private readonly HashSet<Type> SkipableTypes = new HashSet<Type>( AdditionalSkipableTypes );

		private bool IsGenericArgumentSkippable( Type type )
		{
			return type.IsPrimitive || (type.IsValueType || type.IsSealed) && ShouldProcessType( type );
		}

		public void AddSkippedType<T>()
		{
			SkipableTypes.Add( typeof( T ) );
		}

		public void AddSkippedType( Type type )
		{
			SkipableTypes.Add( type );
		}

		public override bool ShouldProcessType( Type type )
		{
			if ( SkipableTypes.Contains( type ) ) return true;

			if ( GetNewType( type ) != type ) return false;

			if ( type.IsConstructedGenericType )
			{
				var genericType = type.GetGenericTypeDefinition();

				return ShouldProcessType( genericType ) && type.GetGenericArguments().All( IsGenericArgumentSkippable );
			}

			if ( IsAssemblyIgnored( type.Assembly ) ) return true;
			if ( type.GetCustomAttribute<SkipHotloadAttribute>() != null ) return true;

			return false;
		}

		protected override bool OnTryCreateNewInstance( object oldInstance, out object newInstance )
		{
			newInstance = oldInstance;

			return true;
		}

		protected override bool OnTryUpgradeInstance( object oldInstance, object newInstance, bool createdElsewhere )
		{
			return true;
		}
	}

	/// <summary>
	/// Instance upgrader that will try to automatically find types are definitely skippable. This upgrader isn't
	/// added automatically, you can enable it by calling <see cref="Hotload.AddUpgrader"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// We attempt this almost last (just before <see cref="DefaultUpgrader"/>) so that any upgraders
	/// that handle specific types will be chosen first, and therefore stop those types from being skipped.
	/// Adds any skippable types it finds to a cache, and forces <see cref="SkipUpgrader"/> to process them.
	/// </para>
	/// <para>
	/// This performs an under-approximation, but you can use <see cref="SkipHotloadAttribute"/> to mark any types it
	/// misses that you know are safe to skip.
	/// </para>
	/// </remarks>
	[DisableAutoCreation, UpgraderGroup( typeof( RootUpgraderGroup ), GroupOrder.Last ), AttemptBefore( typeof( DefaultUpgrader ) )]
	public class AutoSkipUpgrader : Hotload.InstanceUpgrader
	{
		internal readonly Dictionary<Type, bool> AllFieldsSkippableCache = new Dictionary<Type, bool>();

		/// <summary>
		/// The set of types that have been determined to be safe to skip.
		/// </summary>
		public IEnumerable<Type> SkippedTypes => SkippedTypeList
			.OrderBy( x => x.ToString() );

		private RootUpgraderGroup RootUpgraderGroup;
		private ReferenceTypeUpgraderGroup ReferenceTypeUpgraderGroup;
		private SkipUpgrader SkipUpgrader;

		private readonly List<Type> SkippedTypeList = new List<Type>();

		protected override void OnInitialize()
		{
			RootUpgraderGroup = GetUpgrader<RootUpgraderGroup>();
			ReferenceTypeUpgraderGroup = GetUpgrader<ReferenceTypeUpgraderGroup>();
			SkipUpgrader = GetUpgrader<SkipUpgrader>();
		}

		protected override void OnHotloadStart()
		{
			SkippedTypeList.Clear();
		}

		protected override void OnClearCache()
		{
			AllFieldsSkippableCache.Clear();
		}

		private bool AllFieldsSkippable( Type type )
		{
			if ( type.IsPrimitive ) return true;
			if ( type.IsPointer ) return true;

			if ( AllFieldsSkippableCache.TryGetValue( type, out var cached ) ) return cached;

			// Add an entry in the cache now to avoid infinite recursion
			// TODO: This will lead to an under-approximation of which types can be skipped
			AllFieldsSkippableCache.Add( type, false );

			// We definitely shouldn't skip if this type was swapped
			if ( GetNewType( type ) != type )
			{
				return false;
			}

			var firstUpgrader = ReferenceTypeUpgraderGroup
				.GetUpgradersForType( type )
				.FirstOrDefault( x => !(x is CachedUpgrader) );

			// We shouldn't skip if any upgrader (besides SkipUpgrader, CachedUpgrader) wants to process this type
			if ( firstUpgrader != null )
			{
				if ( !(firstUpgrader is SkipUpgrader) ) return false;

				return AllFieldsSkippableCache[type] = true;
			}

			// Recurse for base type
			if ( type.BaseType != null && !AllFieldsSkippable( type.BaseType ) )
			{
				return false;
			}

			const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

			foreach ( var fieldInfo in type.GetFields( flags ) )
			{
				// We only care about fields added by this type, not any inherited fields,
				// because SkipHotloadAttribute isn't inherited.
				if ( fieldInfo.DeclaringType != type ) continue;
				if ( fieldInfo.HasAttribute<SkipHotloadAttribute>() ) continue;

				if ( !fieldInfo.FieldType.IsValueType && !fieldInfo.FieldType.IsSealed )
				{
					// If a field is an unsealed reference type, it could contain an instance
					// of a deriving type that shouldn't be skipped
					return false;
				}

				// Recurse on field type
				if ( !AllFieldsSkippable( fieldInfo.FieldType ) ) return false;
			}

			SkippedTypeList.Add( type );
			RootUpgraderGroup.SetUpgradersForType( type, SkipUpgrader );

			return AllFieldsSkippableCache[type] = true;
		}

		public override bool ShouldProcessType( Type type )
		{
			return AllFieldsSkippable( type );
		}

		protected override bool OnTryCreateNewInstance( object oldInstance, out object newInstance )
		{
			newInstance = oldInstance;
			return true;
		}

		protected override bool OnTryUpgradeInstance( object oldInstance, object newInstance, bool createdElsewhere )
		{
			return true;
		}
	}
}
