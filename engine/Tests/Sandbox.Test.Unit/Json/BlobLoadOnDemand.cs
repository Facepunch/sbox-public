using System.Text.Json.Nodes;

namespace JsonTests;

/// <summary>
/// A blob that opts into <see cref="BlobData.LoadOnDemand"/>: loading keeps its bytes, and only
/// touching <see cref="Value"/> deserializes them.
/// </summary>
public class OnDemandBlob : BlobData
{
	public static int Deserializations;

	protected override bool LoadOnDemand => true;

	int _value;

	public int Value
	{
		get { EnsureLoaded(); return _value; }
		set { EnsureLoaded(); _value = value; }
	}

	/// <summary>Read at load time by <see cref="ReadSummary"/>, without deserializing.</summary>
	public int Summary { get; private set; }

	public bool Loaded => IsLoaded;

	public override void Serialize( ref Writer writer ) => writer.Stream.Write( _value );

	protected override void ReadSummary( ref Reader reader ) => Summary = reader.Stream.Read<int>();

	public override void Deserialize( ref Reader reader )
	{
		Deserializations++;
		_value = reader.Stream.Read<int>();
	}
}

[TestClass]
[DoNotParallelize]
public class BlobLoadOnDemandTest
{
	static JsonNode Save( OnDemandBlob blob )
	{
		using var blobs = BlobDataSerializer.Capture();
		var node = Json.ToNode( blob );
		blobs.SaveTo( node );
		return node;
	}

	static OnDemandBlob Load( JsonNode node )
	{
		using var blobs = BlobDataSerializer.LoadFrom( node );
		return Json.FromNode( node, typeof( OnDemandBlob ) ) as OnDemandBlob;
	}

	[TestMethod]
	public void LoadingKeepsTheDataUntilFirstUse()
	{
		var node = Save( new OnDemandBlob { Value = 7 } );
		var before = OnDemandBlob.Deserializations;

		var loaded = Load( node );

		Assert.IsFalse( loaded.Loaded, "Loading only keeps the stored data" );
		Assert.AreEqual( before, OnDemandBlob.Deserializations, "Nothing is deserialized at load" );
		Assert.AreEqual( 7, loaded.Summary, "The summary is read at load" );

		Assert.AreEqual( 7, loaded.Value );
		Assert.IsTrue( loaded.Loaded );
		Assert.AreEqual( before + 1, OnDemandBlob.Deserializations, "First use deserializes once" );
	}

	[TestMethod]
	public void UntouchedBlobSavesTheDataItWasLoadedFrom()
	{
		var node = Save( new OnDemandBlob { Value = 7 } );
		var loaded = Load( node );

		var resaved = Save( loaded );

		Assert.IsFalse( loaded.Loaded, "Saving doesn't need to load it" );
		Assert.AreEqual( node["$blob"]?.ToString(), resaved["$blob"]?.ToString(), "Same content, same blob id" );
		Assert.AreEqual( 7, Load( resaved ).Value );
	}

	[TestMethod]
	public void WriteBeforeFirstReadIsWhatGetsSaved()
	{
		var loaded = Load( Save( new OnDemandBlob { Value = 7 } ) );

		loaded.Value = 9;

		Assert.AreEqual( 9, loaded.Value );
		Assert.AreEqual( 9, Load( Save( loaded ) ).Value, "The new value is saved, not the stored data" );
	}
}
