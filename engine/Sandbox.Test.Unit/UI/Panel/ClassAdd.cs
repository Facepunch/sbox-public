using Sandbox.UI;
namespace UITest.Panels;

[TestClass]
[DoNotParallelize] // Modfiies UI System Global
public partial class ClassAdd
{
	[TestMethod]
	public void SetSingle()
	{
		var r = new RootPanel();
		r.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new Panel { Parent = r };

		Assert.AreEqual( 0, p.Class.Count() );

		p.AddClass( "one" );

		Assert.AreEqual( 1, p.Class.Count() );
		Assert.AreEqual( "one", p.Class.First() );
	}

	[TestMethod]
	public void SetDouble()
	{
		var r = new RootPanel();
		r.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new Panel { Parent = r };

		Assert.AreEqual( 0, p.Class.Count() );

		p.AddClass( "one" );
		p.AddClass( "two" );

		Assert.AreEqual( 2, p.Class.Count() );
		Assert.IsTrue( p.Class.Contains( "one" ) );
		Assert.IsTrue( p.Class.Contains( "two" ) );
	}

	[TestMethod]
	public void SetDuplicate()
	{
		var r = new RootPanel();
		r.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new Panel { Parent = r };

		Assert.AreEqual( 0, p.Class.Count() );

		p.AddClass( "one" );
		p.AddClass( "two" );
		p.AddClass( "one" );

		Assert.AreEqual( 2, p.Class.Count() );
		Assert.IsTrue( p.Class.Contains( "one" ) );
		Assert.IsTrue( p.Class.Contains( "two" ) );
	}

	[TestMethod]
	public void Remove()
	{
		var r = new RootPanel();
		r.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new Panel { Parent = r };

		Assert.AreEqual( 0, p.Class.Count() );

		p.AddClass( "one" );
		p.AddClass( "two" );
		p.RemoveClass( "one" );

		Assert.AreEqual( 1, p.Class.Count() );
		Assert.IsTrue( p.Class.Contains( "two" ) );
	}

	[TestMethod]
	public void RemoveAll()
	{
		var r = new RootPanel();
		r.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new Panel { Parent = r };

		Assert.AreEqual( 0, p.Class.Count() );

		p.AddClass( "one" );
		p.AddClass( "two" );
		p.RemoveClass( "one" );
		p.RemoveClass( "two" );

		Assert.AreEqual( 0, p.Class.Count() );
	}

	[TestMethod]
	public void SetMultiple()
	{
		var r = new RootPanel();
		r.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new Panel { Parent = r };

		Assert.AreEqual( 0, p.Class.Count() );

		p.AddClass( "one two" );

		Assert.AreEqual( 2, p.Class.Count() );
		Assert.IsTrue( p.Class.Contains( "one" ) );
		Assert.IsTrue( p.Class.Contains( "two" ) );

		p.AddClass( "one two three four" );

		Assert.AreEqual( 4, p.Class.Count() );
		Assert.IsTrue( p.Class.Contains( "one" ) );
		Assert.IsTrue( p.Class.Contains( "two" ) );
		Assert.IsTrue( p.Class.Contains( "three" ) );
		Assert.IsTrue( p.Class.Contains( "four" ) );
	}
}
