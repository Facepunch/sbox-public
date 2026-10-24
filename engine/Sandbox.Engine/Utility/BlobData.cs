namespace Sandbox;

/// <summary>
/// For <see cref="BlobData"/> types whose binary data reference other assets by resource path.
/// Ensures asset references in blobs are recognized by the resource compiler.
/// </summary>
public interface IBlobReferences
{
	/// <summary>
	/// Enumerate the resource paths (e.g. model paths) referenced by this blob.
	/// </summary>
	IEnumerable<string> GetReferencedResources();
}

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
	/// When true, loading keeps only this blob's bytes and <see cref="Deserialize"/> runs the first time
	/// <see cref="EnsureLoaded"/> is called. For large data that is loaded far more often than it's used.
	/// Subclasses must call <see cref="EnsureLoaded"/> before reading or writing their data. A blob that was
	/// never loaded saves the bytes it was loaded from.
	/// </summary>
	protected virtual bool LoadOnDemand => false;

	/// <summary>
	/// For <see cref="LoadOnDemand"/> blobs: called at load time with the stored data, so cheap metadata
	/// (like element counts) can be read without deserializing everything. Need not read to the end.
	/// </summary>
	protected virtual void ReadSummary( ref Reader reader ) { }

	/// <summary>
	/// False while a <see cref="LoadOnDemand"/> blob still holds its stored data undeserialized.
	/// </summary>
	protected bool IsLoaded => _pending is null;

	/// <summary>
	/// Deserializes the stored data of a <see cref="LoadOnDemand"/> blob if that hasn't happened yet.
	/// Cheap once loaded; safe to call from any thread.
	/// </summary>
	protected void EnsureLoaded()
	{
		if ( _pending is null )
			return;

		lock ( _pendingLock )
		{
			if ( _pending is { } data )
			{
				BlobDataSerializer.LoadInto( this, data );
				_pending = null;
			}
		}
	}

	volatile byte[] _pending;
	object _pendingLock;

	/// <summary>
	/// The stored data of a blob that hasn't been loaded yet - saving writes it back as is.
	/// </summary>
	internal byte[] PendingData => _pending;

	/// <summary>
	/// Keeps <paramref name="data"/> (version prefix + payload) to deserialize on first use, if this blob
	/// loads on demand and the data is current. Data that needs upgrading is loaded right away.
	/// </summary>
	internal bool TryDeferLoad( byte[] data )
	{
		if ( !LoadOnDemand )
			return false;

		var stream = ByteStream.CreateReader( data );
		try
		{
			int dataVersion = stream.Read<int>();
			if ( dataVersion != Version )
				return false;

			var reader = new Reader { Stream = stream, DataVersion = dataVersion };
			ReadSummary( ref reader );
		}
		finally
		{
			stream.Dispose();
		}

		_pendingLock = new object();
		_pending = data;
		return true;
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

