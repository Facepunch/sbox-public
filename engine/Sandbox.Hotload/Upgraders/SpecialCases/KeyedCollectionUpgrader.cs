using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace Sandbox.Upgraders.SpecialCases;

#nullable enable

/// <summary>
/// Base class for upgraders that process collections that store items with unique keys,
/// like <see cref="Dictionary{TKey,TValue}"/> and <see cref="HashSet{T}"/>.
/// </summary>
internal abstract class KeyedCollectionUpgrader<TItem>( Type genericTypeDefinition ) : Hotload.InstanceUpgrader
{
	/// <summary>
	/// Which collection type does this upgrader process?
	/// </summary>
	protected Type GenericTypeDefinition { get; } = genericTypeDefinition;

	/// <summary>
	/// Collection accessor to avoid reflection if there isn't already a non-generic interface.
	/// </summary>
	protected interface ICollectionWrapper : IEnumerable<TItem>
	{
		int? Count { get; }
		void Clear();
		bool Add( TItem item );
	}

	#region Overridable

	protected virtual object? GetComparer( object collection )
	{
		return GetComparerProperty( collection.GetType() )?.GetValue( collection );
	}

	protected abstract object? GetKey( TItem item );
	protected abstract ICollectionWrapper Wrap( object collection );

	#endregion

	#region InstanceUpgrader Implementation

	public override bool ShouldProcessType( Type type )
	{
		return type.IsConstructedGenericType
			&& type.GetGenericTypeDefinition() == GenericTypeDefinition;
	}

	protected override bool OnTryCreateNewInstance( object oldInstance, out object? newInstance )
	{
		var oldType = oldInstance.GetType();
		var newType = GetNewType( oldType );
		var oldComparer = GetComparer( oldInstance );

		// A collection whose type and comparer are unchanged is upgraded in place. A replacement would
		// stand in for it, empty, until the late queue fills it. Code that runs during the swap, such as
		// a replaced type's static constructor, would read that empty collection.
		if ( newType == oldType && (oldComparer is null || ReferenceEquals( GetNewInstance( oldComparer ), oldComparer )) )
		{
			newInstance = oldInstance;
			return true;
		}

		if ( oldComparer is { } comparer && GetComparerConstructor( oldType ) is not null )
		{
			newInstance = Activator.CreateInstance( newType, GetNewInstance( comparer ) )!;
		}
		else
		{
			newInstance = Activator.CreateInstance( newType )!;
		}

		return true;
	}

	protected override bool OnTryUpgradeInstance( object oldInstance, object newInstance, bool createdElsewhere )
	{
		AddCachedInstance( oldInstance, newInstance );
		SuppressFinalize( oldInstance, newInstance );

		var oldPath = CurrentPath;

		if ( TracePaths )
		{
			CurrentPath = oldPath[typeof( int )];
		}

		var wrapped = Wrap( oldInstance );

		try
		{
			foreach ( var item in wrapped )
			{
				var key = GetKey( item );

				// Force all the keys to be upgraded, so that by the time we do OnProcessInstance
				// we can safely call GetHashCode on them and get the right values
				GetNewInstance( key );
			}
		}
		finally
		{
			CurrentPath = oldPath;
		}

		// Add to late queue so that any keys can have their fields populated
		// before being added to the dictionary, to ensure GetHashCode() / Equals() works

		ScheduleLateProcessInstance( oldInstance, newInstance );

		return true;
	}

	protected override int OnProcessInstance( object oldInstance, object newInstance )
	{
		var oldWrapped = Wrap( oldInstance );
		var newWrapped = Wrap( newInstance );

		// If we're editing in-place, make a copy of the old collection

		var oldItems = ReferenceEquals( oldInstance, newInstance )
			? oldWrapped.ToArray()
			: oldWrapped.AsEnumerable();

		// No other code should be running right now, but let's
		// double-check that nothing else is adding to the collection
		// while this method runs.

		var expectedItemCount = 0;
		var warnedThreadInterference = false;
		var warnedKeyCollision = false;

		var oldPath = CurrentPath;

		if ( TracePaths )
		{
			CurrentPath = oldPath[typeof( int )];
		}

		try
		{
			// Upgrade every item before emptying the collection. Upgrading an item can create an instance
			// of a replaced type, which runs that type's static constructor, and that code may read this
			// very collection. It must find the old contents, not a half-filled table.

			var upgraded = new List<TItem>();

			foreach ( var oldItem in oldItems )
			{
				upgraded.Add( GetNewInstance( oldItem ) );
			}

			newWrapped.Clear();

			foreach ( var upgradedItem in upgraded )
			{
				// Keys can never be null, warn if a key became null during this hotload

				if ( upgradedItem is not { } newItem || GetKey( newItem ) is not { } newKey )
				{
					if ( !CurrentDstField.HasAttribute<SuppressNullKeyWarningAttribute>() )
					{
						Log( HotloadEntryType.Warning, $"Encountered null key when upgrading a collection", CurrentDstField );
					}

					continue;
				}

				// Error if another thread is interfering during the hotload, we should be the only managed thread running.
				// Some collections don't have a Count, so have to skip the check there

				if ( newWrapped.Count is { } count && count != expectedItemCount && !warnedThreadInterference )
				{
					warnedThreadInterference = true;
					Log( HotloadEntryType.Error, $"Another thread is modifying this collection during hotload", CurrentDstField );
				}

				// Try adding the item, keys might start colliding if instances overriding Equals have changed during the hotload,
				// or Equals was overridden incorrectly and depended on something mutable.

				try
				{
					if ( newWrapped.Add( newItem ) )
					{
						expectedItemCount++;
						continue;
					}
				}
				catch ( ArgumentException )
				{
					// We handle this exception below, same as AddItem returning false
				}

				if ( warnedKeyCollision ) continue;

				warnedKeyCollision = true;
				Log( HotloadEntryType.Error, $"Collection keys ({newKey}) have started colliding during hotload", CurrentDstField );
			}

			return 1;
		}
		finally
		{
			CurrentPath = oldPath;
		}
	}

	#endregion

	#region Static Helpers

	/// <summary>
	/// Look for a property named <c>Comparer</c> in the given <paramref name="type"/>.
	/// </summary>
	private static PropertyInfo? GetComparerProperty( Type type )
	{
		var property = type.GetProperty( nameof( HashSet<object>.Comparer ),
			BindingFlags.Instance | BindingFlags.Public );

		// Should support IComparer and IEqualityComparer

		return property;
	}

	/// <summary>
	/// Look for a constructor in <paramref name="type"/> with exactly one parameter,
	/// with that parameter matching the type of the property found with
	/// <see cref="GetComparerProperty"/>.
	/// </summary>
	private static ConstructorInfo? GetComparerConstructor( Type type )
	{
		if ( GetComparerProperty( type ) is not { } property ) return null;

		foreach ( var ctor in type.GetConstructors() )
		{
			if ( ctor.GetParameters() is not { Length: 1 } parameters ) continue;

			if ( parameters[0].ParameterType == property.PropertyType )
			{
				return ctor;
			}
		}

		return null;
	}

	#endregion
}
