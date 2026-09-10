using System.Threading;

namespace Sandbox;

internal partial class BytePack
{
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

		public virtual object Read( ref ByteStream data )
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

		internal object Deserialize( ref ByteStream bs ) => parent.Deserialize( ref bs );
		internal void Serialize( ref ByteStream bs, object obj ) => parent.Serialize( ref bs, obj );
	}
}
