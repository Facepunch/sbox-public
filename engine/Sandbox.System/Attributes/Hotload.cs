
namespace Sandbox
{
	/// <summary>
	/// Skip processing a specific field, or any fields in a type marked by this attribute. Field
	/// processing will still occur if a type marked by this attribute was defined in a swapped assembly.
	/// </summary>
	/// <remarks>
	/// This is nice for speeding up hotloading, particularly when used on types with lots of fields, or
	/// on fields that are the only path to large networks of objects that all don't need replacing during the hotload.
	/// </remarks>
	[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, Inherited = false )]
	public sealed class SkipHotloadAttribute : Attribute { }

	/// <summary>
	/// When applied to a member with <see cref="Dictionary{TKey,TValue}"/> or <see cref="HashSet{T}"/> type,
	/// don't warn if the key of an item becomes null during a hotload because a type is removed. You should
	/// only use this attribute if you're sure that it's safe to quietly remove entries.
	/// </summary>
	[AttributeUsage( AttributeTargets.Field | AttributeTargets.Property )]
	public sealed class SuppressNullKeyWarningAttribute : Attribute { }

	/// <summary>
	/// During hotloads, instances of types implementing this interface will be notified when
	/// they get replaced.
	/// </summary>
	public interface IHotloadManaged
	{
		/// <summary>
		/// Called when this instance is about to be replaced during a hotload.
		/// The implementor may optionally write to the <paramref name="state"/>
		/// dictionary, which gets passed to the new replacing instance when
		/// <see cref="Created"/> is called on it.
		/// </summary>
		/// <param name="state">Dictionary to store values to pass to the new instance.</param>
		void Destroyed( Dictionary<string, object> state ) { }

		/// <summary>
		/// Called when this instance has been created during a hotload, replacing an
		/// instance from an older version of the containing assembly. The <paramref name="state"/>
		/// parameter will contain any values populated when <see cref="Destroyed"/> was called
		/// on the old instance that was replaced.
		/// </summary>
		/// <param name="state">Dictionary containing values written by the old instance.</param>
		void Created( IReadOnlyDictionary<string, object> state ) { }

		/// <summary>
		/// Called when this instance is about to be processed, but not replaced.
		/// </summary>
		void Persisted() { }

		/// <summary>
		/// Called when this instance could not be upgraded during a hotload, and any references
		/// to it have been replaced with null. This is a good time to clean up any unmanaged resources
		/// related to this instance.
		/// </summary>
		void Failed() { }
	}

	[AttributeUsage( AttributeTargets.Assembly )]
	public sealed class SupportsILHotloadAttribute( string previousAssemblyVersion ) : Attribute
	{
		public string PreviousAssemblyVersion { get; } = previousAssemblyVersion;
	}

	[AttributeUsage( AttributeTargets.Method )]
	public sealed class MethodBodyChangeAttribute( string changedAssemblyVersion ) : Attribute
	{
		public string ChangedAssemblyVersion { get; } = changedAssemblyVersion;
	}

	public enum PropertyAccessor
	{
		Get,
		Set
	}

	[AttributeUsage( AttributeTargets.Property, AllowMultiple = true )]
	public sealed class PropertyAccessorBodyChangeAttribute( PropertyAccessor accessor, string changedAssemblyVersion ) : Attribute
	{
		public PropertyAccessor Accessor { get; } = accessor;
		public string ChangedAssemblyVersion { get; } = changedAssemblyVersion;
	}
}
