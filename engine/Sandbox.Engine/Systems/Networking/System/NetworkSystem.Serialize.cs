using Mono.Cecil;
using Sandbox.Engine;
using Sandbox.Internal;

namespace Sandbox.Network;

internal partial class NetworkSystem
{
	internal TypeLibrary TypeLibrary { get; set; }

	internal void Serialize<T>( T data, ref ByteStream bs )
	{
		TypeLibrary.ToBytes( data, ref bs );
	}

	internal T Deserialize<T>( ReadOnlySpan<byte> data )
	{
		return TypeLibrary.FromBytes<T>( data );
	}
}
