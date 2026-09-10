using Sandbox.UI;
namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class PanelDataBindingTest
{
	/// <summary>
	/// Builds a RootPanel with screen-like bounds, matching how the real UI system hosts panels.
	/// </summary>
	static RootPanel CreateRoot()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		return root;
	}

	/// <summary>
	/// StringValue defaults to null and round-trips through the property. Setting it via the
	/// special "value" property short-circuits before attribute storage, so no attribute is
	/// recorded for it.
	/// </summary>
	[TestMethod]
	public void StringValueRoundTrip()
	{
		var p = new Panel();

		Assert.IsNull( p.StringValue );

		p.StringValue = "hello";
		Assert.AreEqual( "hello", p.StringValue );

		p.SetProperty( "value", "world" );
		Assert.AreEqual( "world", p.StringValue );
		Assert.IsNull( p.GetAttribute( "value" ) );
	}

	/// <summary>
	/// CreateValueEvent( name, value ) queues a "{name}.changed" event carrying the value, which
	/// reaches event listeners registered for that derived event name on the next tick.
	/// </summary>
	[TestMethod]
	public void ValueEventTriggersChangedListener()
	{
		var root = CreateRoot();
		var p = new ValueProbePanel { Parent = root };

		PanelEvent received = null;
		p.AddEventListener( "myvalue.changed", e => received = e );

		p.RaiseValueEvent( "myvalue", 42 );
		root.TickInternal();

		Assert.IsNotNull( received );
		Assert.AreEqual( "myvalue.changed", received.Name );
		Assert.AreEqual( 42, received.Value );
		Assert.AreEqual( p, received.Target );
	}

	/// <summary>
	/// SetProperty matches C# properties case-insensitively through the TypeLibrary, assigns
	/// string properties directly, and also records the raw value as an attribute.
	/// </summary>
	[TestMethod]
	public void SetPropertyStringCaseInsensitive()
	{
		var p = new BindingProbePanel();

		p.SetProperty( "boundtitle", "Hello World" );

		Assert.AreEqual( "Hello World", p.BoundTitle );
		Assert.AreEqual( "Hello World", p.GetAttribute( "boundtitle" ) );
	}

	/// <summary>
	/// SetProperty converts string values to the target property's scalar type - int, float
	/// and bool all parse from their string forms, including "0" meaning false.
	/// </summary>
	[TestMethod]
	public void SetPropertyScalarConversions()
	{
		var p = new BindingProbePanel();

		p.SetProperty( "BoundCount", "42" );
		Assert.AreEqual( 42, p.BoundCount );

		p.SetProperty( "BoundSpeed", "3.5" );
		Assert.AreEqual( 3.5f, p.BoundSpeed );

		p.SetProperty( "BoundFlag", "true" );
		Assert.IsTrue( p.BoundFlag );

		p.SetProperty( "BoundFlag", "0" );
		Assert.IsFalse( p.BoundFlag );
	}

	/// <summary>
	/// SetProperty parses enum properties with Enum.Parse, which is case sensitive - an exact
	/// value name applies, while a wrong-cased name fails the conversion and leaves the
	/// property unchanged.
	/// </summary>
	[TestMethod]
	public void SetPropertyEnumConversion()
	{
		var p = new BindingProbePanel();

		p.SetProperty( "BoundMode", "Second" );
		Assert.AreEqual( BindingProbeMode.Second, p.BoundMode );

		p.SetProperty( "BoundMode", "third" );
		Assert.AreEqual( BindingProbeMode.Second, p.BoundMode );
	}

	/// <summary>
	/// The special property names are handled before C# property lookup: "id" assigns Id,
	/// "class" adds the class and replaces the previously set one on the next call, and
	/// "style" parses inline styles onto the panel.
	/// </summary>
	[TestMethod]
	public void SetPropertySpecialKeys()
	{
		var p = new BindingProbePanel();

		p.SetProperty( "id", "thing" );
		Assert.AreEqual( "thing", p.Id );

		p.SetProperty( "class", "alpha" );
		Assert.IsTrue( p.HasClass( "alpha" ) );

		p.SetProperty( "class", "beta" );
		Assert.IsFalse( p.HasClass( "alpha" ) );
		Assert.IsTrue( p.HasClass( "beta" ) );

		p.SetProperty( "style", "width: 100px;" );
		Assert.AreEqual( Length.Pixels( 100 ), p.Style.Width.Value );
	}

	/// <summary>
	/// A name with no matching C# property still gets stored as an attribute, and GetAttribute
	/// returns the provided default when the attribute was never set.
	/// </summary>
	[TestMethod]
	public void UnmatchedPropertyStoredAsAttribute()
	{
		var p = new BindingProbePanel();

		p.SetProperty( "data-foo", "bar" );

		Assert.AreEqual( "bar", p.GetAttribute( "data-foo" ) );
		Assert.AreEqual( "fallback", p.GetAttribute( "missing", "fallback" ) );
	}

	/// <summary>
	/// SetPropertyObject assigns directly via reflection when the value's type matches the
	/// property type - skipping attribute storage - and falls back to the string SetProperty
	/// path (which records an attribute) for mismatched types.
	/// </summary>
	[TestMethod]
	public void SetPropertyObjectAssignment()
	{
		var p = new BindingProbePanel();

		p.SetPropertyObject( "BoundCount", 7 );
		Assert.AreEqual( 7, p.BoundCount );
		Assert.IsNull( p.GetAttribute( "BoundCount" ) );

		p.SetPropertyObject( "BoundCount", "13" );
		Assert.AreEqual( 13, p.BoundCount );
		Assert.AreEqual( "13", p.GetAttribute( "BoundCount" ) );
	}
}

/// <summary>
/// Enum used to pin SetProperty's string-to-enum conversion behavior.
/// </summary>
public enum BindingProbeMode
{
	First,
	Second,
	Third
}

/// <summary>
/// Exposes the protected CreateValueEvent so tests can observe the "{name}.changed" event flow.
/// </summary>
public class ValueProbePanel : Panel
{
	/// <summary>
	/// Calls the protected CreateValueEvent with the given name and value.
	/// </summary>
	public void RaiseValueEvent( string name, object value )
	{
		CreateValueEvent( name, value );
	}
}

/// <summary>
/// Provides scalar properties of each common type for SetProperty conversion tests.
/// </summary>
public class BindingProbePanel : Panel
{
	public string BoundTitle { get; set; }
	public int BoundCount { get; set; }
	public float BoundSpeed { get; set; }
	public bool BoundFlag { get; set; }
	public BindingProbeMode BoundMode { get; set; }
}
