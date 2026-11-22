using System.Threading;

namespace Sandbox;

internal partial class BytePack
{
	/// <summary>
	/// A packer that can write and read its value type without boxing it.
	/// </summary>
	internal interface IValuePacker<T>
	{
		void Write( ref ByteStream bs, T value );
		T Read( ref ByteStream bs );

		/// <summary>
		/// Read as part of another value, for packers that read nested values.
		/// </summary>
		T Read( ref ByteStream bs, int depth ) => Read( ref bs );
	}

	public class Packer
	{
		protected static readonly ThreadLocal<HashSet<object>> _visited = new( () => new HashSet<object>( ReferenceEqualityComparer.Instance ) );

		public virtual Type TargetType { get; }
		internal virtual Identifier Header { get; }
		internal virtual int TypeIdentifier { get; }
		internal bool UsesCollectionFormat { get; private set; }

		public virtual void Write( ref ByteStream bs, object obj )
		{
			throw new NotImplementedException();
		}

		// Pass depth + 1 to anything nested, or a hostile payload recurses without bound.
		public virtual object Read( ref ByteStream data, int depth )
		{
			throw new NotImplementedException();
		}

		public virtual void WriteTypeIdentifier( ref ByteStream bs, Type targetType )
		{
			bs.Write( Header );
		}

		BytePack parent;

		internal void Init( BytePack bytePack )
		{
			parent = bytePack;

			if ( TargetType is not null )
			{
				// Collections keep their built-in wire format even if a runtime packer for
				// the same type was installed while reading a message.
				UsesCollectionFormat = TargetType.IsBasedOnGenericType( typeof( List<> ) )
					|| TargetType.IsBasedOnGenericType( typeof( Dictionary<,> ) );
				parent.types[TargetType] = this;
			}

			if ( TypeIdentifier != default )
			{
				parent.typeHandler[TypeIdentifier] = this;
			}

			parent.handlers[Header] = this;
		}

		internal Packer GetHandlerFor( ref ByteStream bs )
		{
			var ident = bs.Read<Identifier>();

			if ( ident == 0 )
				return null;

			if ( ident == Identifier.Runtime )
			{
				return parent.GetOrCreatePacker( bs.Read<int>() );
			}

			return parent.handlers[ident];
		}

		internal Packer GetHandlerFor( Type type )
		{
			return parent.GetOrCreatePacker( type );
		}

		internal object Deserialize( ref ByteStream bs, int depth ) => parent.Deserialize( ref bs, depth );
		internal void Serialize( ref ByteStream bs, object obj ) => parent.Serialize( ref bs, obj );

		/// <summary>
		/// Write a value as its own type, which doesn't box plain values. Same bytes as <see cref="Serialize"/>.
		/// </summary>
		internal void SerializeValue<T>( ref ByteStream bs, T value ) => parent.Serialize( ref bs, value );

		/// <summary>
		/// Read a nested value as its own type. A null reads as default, like setting a field through reflection.
		/// </summary>
		internal T DeserializeValue<T>( ref ByteStream bs, int depth ) => parent.DeserializeValue<T>( ref bs, depth );
	}
}
