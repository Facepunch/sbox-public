namespace Sandbox;

public abstract partial class Component
{
	/// <summary>
	/// When implemented on a <see cref="Component"/> or <see cref="GameObjectSystem"/> it can read and write
	/// data to and from a network snapshot.
	/// </summary>
	public interface INetworkSnapshot
	{
		/// <summary>
		/// Read data from the snapshot.
		/// </summary>
		void ReadSnapshot( ref ByteStream reader )
		{

		}

		/// <summary>
		/// Write data to the snapshot.
		/// </summary>
		void WriteSnapshot( ref ByteStream writer )
		{

		}
	}
}
