using System;

namespace ReflectionTests;

[TestClass]
[DoNotParallelize]
public class MemberAttributesTest
{
	System.Reflection.Assembly ThisAssembly => GetType().Assembly;

	[TestMethod]
	public void GetCustomAttributeMatchesOfTypeFirstOrDefault()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, false );

		var property = tl.GetType<ClassWithAttributedProps>().GetProperty( nameof( ClassWithAttributedProps.Marked ) );
		Assert.AreEqual( 6, property.Attributes.Length );

		AssertFirstMatch<MarkerAttribute>( property, true );
		AssertFirstMatch<DerivedMarkerAttribute>( property, true );
		AssertFirstMatch<GenericMarkerAttribute<int>>( property, true );
		AssertFirstMatch<GenericMarkerAttribute<string>>( property, true );
		AssertFirstMatch<GenericMarkerAttribute<float>>( property, false );
		AssertFirstMatch<UnusedAttribute>( property, false );
		AssertFirstMatch<Attribute>( property, true );

		tl.RemoveAssembly( ThisAssembly );
	}

	[TestMethod]
	public void GetCustomAttributeKeepsArrayOrder()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, false );

		var property = tl.GetType<ClassWithAttributedProps>().GetProperty( nameof( ClassWithAttributedProps.Marked ) );

		var other = new OtherAttribute();
		var derived = new DerivedMarkerAttribute( 1 );
		var first = new MarkerAttribute( 2 );
		var second = new MarkerAttribute( 3 );
		property.Attributes = [null, other, derived, first, second];

		Assert.AreSame( derived, AssertFirstMatch<MarkerAttribute>( property, true ) );
		Assert.AreSame( derived, AssertFirstMatch<DerivedMarkerAttribute>( property, true ) );
		Assert.AreSame( other, AssertFirstMatch<Attribute>( property, true ) );

		property.Attributes = [second, first];
		Assert.AreSame( second, AssertFirstMatch<MarkerAttribute>( property, true ) );

		property.Attributes = [null, null];
		AssertFirstMatch<Attribute>( property, false );

		property.Attributes = [];
		AssertFirstMatch<MarkerAttribute>( property, false );

		property.Attributes = null;
		AssertFirstMatch<MarkerAttribute>( property, false );

		tl.RemoveAssembly( ThisAssembly );
	}

	static T AssertFirstMatch<T>( MemberDescription member, bool expectMatch ) where T : Attribute
	{
		var expected = member.Attributes?.OfType<T>().FirstOrDefault();
		var actual = member.GetCustomAttribute<T>();

		Assert.AreSame( expected, actual );
		Assert.AreEqual( expectMatch, actual is not null );

		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 100; i++ )
			_ = member.GetCustomAttribute<T>();

		Assert.AreEqual( 0L, GC.GetAllocatedBytesForCurrentThread() - before );
		return actual;
	}
}

[AttributeUsage( AttributeTargets.All, AllowMultiple = true )]
public class MarkerAttribute( int value ) : Attribute
{
	public int Value { get; } = value;
}

public class DerivedMarkerAttribute( int value ) : MarkerAttribute( value );

[AttributeUsage( AttributeTargets.All, AllowMultiple = true )]
public class GenericMarkerAttribute<T> : Attribute;

public class OtherAttribute : Attribute;

public class UnusedAttribute : Attribute;

[Expose]
public class ClassWithAttributedProps
{
	[Other, Marker( 1 ), DerivedMarker( 2 ), Marker( 3 ), GenericMarker<int>, GenericMarker<string>]
	public int Marked { get; set; }
}
