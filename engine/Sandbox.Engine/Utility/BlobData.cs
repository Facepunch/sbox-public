namespace Sandbox;

/// <summary>
/// Base class for properties that should be serialized to binary format instead of JSON.
/// Used for large data structures that would be inefficient as JSON.
/// </summary>
public abstract class BlobData
{
	/// <summary>
	/// The version of this binary data format. Used for upgrade paths.
	/// </summary>
	public virtual int Version => 1;

	/// <summary>
	/// Serialize this object to binary format.
	/// </summary>
	public abstract void Serialize( ref Writer writer );

	/// <summary>
	/// Deserialize this object from binary format.
	/// </summary>
	public abstract void Deserialize( ref Reader reader );

	/// <summary>
	/// Optional upgrade path for old data versions. Called if the data version is older than current Version.
	/// </summary>
	public virtual void Upgrade( ref Reader reader, int fromVersion )
	{
		Deserialize( ref reader );
	}

	/// <summary>
	/// Context for writing binary blob data. Wraps ByteStream for allocation-free serialization.
	/// </summary>
	public ref struct Writer
	{
		/// <summary>
		/// The underlying byte stream.
		/// </summary>
		public ByteStream Stream;
	}

	/// <summary>
	/// Context for reading binary blob data. Wraps ByteStream for allocation-free deserialization.
	/// </summary>
	public ref struct Reader
	{
		/// <summary>
		/// The underlying byte stream.
		/// </summary>
		public ByteStream Stream;

		/// <summary>
		/// The version of the data being read.
		/// </summary>
		public int DataVersion;
	}

}

