using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

namespace BytePackTests;

public record struct PackedGrabState
{
	public bool Active { get; set; }
	public bool Pulling { get; set; }
	public GameObject GameObject { get; set; }
	public Vector3 LocalOffset { get; set; }
	public Vector3 LocalNormal { get; set; }
	public Rotation GrabOffset { get; set; }
	public float GrabDistance { get; set; }
	public Vector3 PullApex { get; set; }
	public float PullStrength { get; set; }
}

public enum PackedMode { None, Hold, Pull }

public struct PackedMixed
{
	public PackedGrabState State;
	public PackedMode Mode;
	public int? MaybeCount;
	public Vector3? MaybePosition;
	public object Boxed;
	public List<int> Numbers;
	public string Label;
	private float _secret;

	public float Secret { readonly get => _secret; set => _secret = value; }
}

public readonly record struct PackedReadOnly( int Id, string Name, Vector3 Position );

public struct PackedCycle
{
	public List<object> Items;
}

/// <summary>
/// No GetHashCode or Equals override, like most plain game structs.
/// </summary>
public struct PlainSyncStruct
{
	public Vector3 Position;
	public int Count;
	public string Name;
}

[TestClass, DoNotParallelize]
public class StructPackingTest
{
	static TypeLibrary tl;

	[ClassInitialize]
	public static void ClassInitialize( TestContext context )
	{
		tl = new TypeLibrary();
		tl.AddAssembly( typeof( Vector3 ).Assembly, false );
		tl.AddAssembly( typeof( Bootstrap ).Assembly, false );
		tl.AddAssembly( typeof( StructPackingTest ).Assembly, true );
	}

	static PackedGrabState Grab => new()
	{
		Active = true,
		LocalOffset = new Vector3( 1, 2, 3 ),
		LocalNormal = Vector3.Up,
		GrabOffset = Rotation.FromYaw( 45 ),
		GrabDistance = 128,
		PullApex = new Vector3( 4, 5, 6 ),
		PullStrength = 0.5f
	};

	static PackedMixed Mixed => new()
	{
		State = Grab,
		Mode = PackedMode.Pull,
		MaybeCount = 7,
		Boxed = new Vector3( 9, 8, 7 ),
		Numbers = new List<int> { 1, 2, 3 },
		Label = "mixed",
		Secret = 4.5f
	};

	/// <summary>
	/// What the reflection packer wrote: the type header, then every field as its own value, by name descending.
	/// </summary>
	static void WriteFieldByField( ref ByteStream bs, object value )
	{
		var type = value.GetType();
		bs.Write( (byte)BytePack.Identifier.Runtime );
		bs.Write( tl.GetType( type ).Identity );

		var fields = type.GetFields( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance )
			.OrderByDescending( x => x.Name )
			.ThenBy( x => x.DeclaringType!.Name );

		foreach ( var field in fields )
		{
			var fieldValue = field.GetValue( value );
			var fieldType = fieldValue?.GetType();

			if ( fieldType is { IsValueType: true, IsPrimitive: false, IsEnum: false } && !SandboxedUnsafe.IsAcceptablePod( fieldType ) && tl.GetType( fieldType ) is not null && fieldType.Assembly == typeof( StructPackingTest ).Assembly )
			{
				WriteFieldByField( ref bs, fieldValue );
				continue;
			}

			tl.ToBytes( fieldValue, ref bs );
		}
	}

	static byte[] FieldByField( object value )
	{
		var bs = ByteStream.Create( 256 );
		try
		{
			WriteFieldByField( ref bs, value );
			return bs.ToArray();
		}
		finally
		{
			bs.Dispose();
		}
	}

	static void AssertWire<T>( T value )
	{
		var expected = FieldByField( value );
		CollectionAssert.AreEqual( expected, tl.ToBytes( value ), "typed" );
		CollectionAssert.AreEqual( expected, tl.ToBytes( (object)value ), "boxed" );
	}

	[TestMethod]
	public void WireMatchesFieldByField()
	{
		AssertWire( new PackedGrabState() );
		AssertWire( Grab );
		AssertWire( new PackedMixed() );
		AssertWire( Mixed );
		AssertWire( new PackedReadOnly( 3, "three", Vector3.One ) );
	}

	static void AssertRoundTrip<T>( T value )
	{
		var bytes = tl.ToBytes( value );
		Assert.AreEqual( value, tl.FromBytes<T>( bytes ) );
		Assert.AreEqual( (object)value, tl.FromBytes<object>( bytes ) );
	}

	[TestMethod]
	public void RoundTrips()
	{
		AssertRoundTrip( new PackedGrabState() );
		AssertRoundTrip( Grab );
		AssertRoundTrip( new PackedReadOnly( 3, "three", Vector3.One ) );
		AssertRoundTrip( new PackedReadOnly() );

		var mixed = tl.FromBytes<PackedMixed>( tl.ToBytes( Mixed ) );
		Assert.AreEqual( Mixed.State, mixed.State );
		Assert.AreEqual( PackedMode.Pull, mixed.Mode );
		Assert.AreEqual( 7, mixed.MaybeCount );
		Assert.IsNull( mixed.MaybePosition );
		Assert.AreEqual( new Vector3( 9, 8, 7 ), mixed.Boxed );
		CollectionAssert.AreEqual( new List<int> { 1, 2, 3 }, mixed.Numbers );
		Assert.AreEqual( "mixed", mixed.Label );
		Assert.AreEqual( 4.5f, mixed.Secret );
	}

	[TestMethod]
	public void InsideCollections()
	{
		var list = new List<PackedGrabState> { Grab, default, Grab with { Pulling = true } };
		CollectionAssert.AreEqual( list, tl.FromBytes<List<PackedGrabState>>( tl.ToBytes( list ) ) );

		var objects = new List<object> { Grab, 3, Mixed.State };
		CollectionAssert.AreEqual( objects, tl.FromBytes<List<object>>( tl.ToBytes( objects ) ) );
	}

	[TestMethod]
	public void TypedPackAndUnpackDoNotAllocate()
	{
		var value = Grab;
		var bytes = tl.ToBytes( value );
		tl.FromBytes<PackedGrabState>( bytes );

		var bs = ByteStream.Create( 1024 );
		try
		{
			tl.ToBytes( value, ref bs );

			var before = GC.GetAllocatedBytesForCurrentThread();
			for ( var i = 0; i < 100; i++ )
			{
				bs.Position = 0;
				tl.ToBytes( value, ref bs );
			}

			Assert.AreEqual( 0, GC.GetAllocatedBytesForCurrentThread() - before, "pack" );
		}
		finally
		{
			bs.Dispose();
		}

		var reader = ByteStream.CreateReader( bytes );
		try
		{
			tl.FromBytes<PackedGrabState>( ref reader );

			var before = GC.GetAllocatedBytesForCurrentThread();
			for ( var i = 0; i < 100; i++ )
			{
				reader.Position = 0;
				tl.FromBytes<PackedGrabState>( ref reader );
			}

			Assert.AreEqual( 0, GC.GetAllocatedBytesForCurrentThread() - before, "unpack" );
		}
		finally
		{
			reader.Dispose();
		}
	}

	[TestMethod]
	public void CycleThroughBoxIsDetected()
	{
		var value = new PackedCycle { Items = new List<object>() };
		object boxed = value;
		value.Items.Add( boxed );

		Assert.ThrowsException<SerializationException>( () => tl.ToBytes( boxed ) );
		Assert.ThrowsException<SerializationException>( () => tl.ToBytes( value ) );
	}

	[TestMethod]
	public void NullForValueFieldReadsAsDefault()
	{
		var bs = ByteStream.Create( 64 );
		try
		{
			bs.Write( (byte)BytePack.Identifier.Runtime );
			bs.Write( tl.GetType( typeof( PlainSyncStruct ) ).Identity );
			bs.Write( (byte)BytePack.Identifier.Null ); // Position
			bs.Write( (byte)BytePack.Identifier.Null ); // Name
			bs.Write( (byte)BytePack.Identifier.Null ); // Count

			var value = tl.FromBytes<PlainSyncStruct>( bs.ToArray() );
			Assert.AreEqual( default, value.Position );
			Assert.AreEqual( 0, value.Count );
			Assert.IsNull( value.Name );
		}
		finally
		{
			bs.Dispose();
		}
	}

	[TestMethod]
	public void SyncHashSeesEveryField()
	{
		var previous = GlobalContext.Current.TypeLibrary;
		GlobalContext.Current.TypeLibrary = tl;

		try
		{
			var value = new PlainSyncStruct { Position = new Vector3( 1, 2, 3 ), Count = 1, Name = "a" };

			using var table = new NetworkTable();
			var entry = new NetworkTable.Entry { TargetType = typeof( PlainSyncStruct ), GetValue = () => value, SetValue = v => value = (PlainSyncStruct)v };
			table.Register( 1, entry );
			entry.IsDirty = false;

			var list = new List<PlainSyncStruct> { value };
			var listEntry = new NetworkTable.Entry { TargetType = typeof( List<PlainSyncStruct> ), GetValue = () => list, SetValue = v => list = (List<PlainSyncStruct>)v };
			table.Register( 2, listEntry );
			listEntry.IsDirty = false;
			list[0] = value with { Count = 3 };
			Assert.IsTrue( table.UpdateSlotHash( 2, list ), "list element" );
			Assert.IsTrue( table.UpdateSlotHash( 2, (object)new List<PlainSyncStruct> { value with { Name = "c" } } ), "boxed list element" );

			PlainSyncStruct? nullable = value;
			var nullableEntry = new NetworkTable.Entry { TargetType = typeof( PlainSyncStruct? ), GetValue = () => nullable, SetValue = v => nullable = (PlainSyncStruct?)v };
			table.Register( 3, nullableEntry );
			nullableEntry.IsDirty = false;
			Assert.IsTrue( table.UpdateSlotHash( 3, (PlainSyncStruct?)(value with { Count = 4 }) ), "nullable" );
			Assert.IsFalse( table.UpdateSlotHash( 3, (object)(value with { Count = 4 }) ), "boxed nullable hashes like typed" );

			Assert.IsTrue( table.UpdateSlotHash( 1, value with { Count = 2 } ), "typed" );
			Assert.IsFalse( table.UpdateSlotHash( 1, (object)(value with { Count = 2 }) ), "boxed hashes like typed" );
			Assert.IsTrue( table.UpdateSlotHash( 1, (object)(value with { Name = "b" }) ), "boxed" );
			Assert.IsFalse( table.UpdateSlotHash( 1, value with { Name = "b" } ), "typed hashes like boxed" );
		}
		finally
		{
			GlobalContext.Current.TypeLibrary = previous;
		}
	}
}
