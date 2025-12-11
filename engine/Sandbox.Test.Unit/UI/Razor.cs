using Sandbox.Engine;
using Sandbox.UI;

namespace UITest;

[TestClass]
[DoNotParallelize] // Modfiies UI System Global
public partial class RazorBasics
{
	[ClassInitialize]
	public static void ClassInitialize( TestContext context )
	{
	}

	void PanelShouldHaveOneGeneratedChild( Panel panel )
	{
		panel.TickInternal();

		Assert.AreEqual( 1, panel.Children.Count() );

		panel.TickInternal();

		Assert.AreEqual( 1, panel.Children.Count() );

		panel.ClearRenderTree();
		GlobalContext.Current.UISystem.RunDeferredDeletion();

		Assert.AreEqual( 0, panel.Children.Count() );
	}

	[TestMethod]
	public void SimpleWithContent()
	{
		var panel = new TestComponents.SimpleWithContent();
		Assert.AreEqual( 0, panel.Children.Count() );
		PanelShouldHaveOneGeneratedChild( panel );
	}

	[TestMethod]
	public void SimpleWithMarkup()
	{
		var panel = new TestComponents.SimpleWithMarkup();
		PanelShouldHaveOneGeneratedChild( panel );
	}

	[TestMethod]
	public void RefAttribute()
	{
		var panel = new TestComponents.RefAttribute();
		Assert.AreEqual( 0, panel.Children.Count() );
		Assert.IsNull( panel.MyReferencedElement );
		Assert.IsNull( panel.MyReferencedMarkup );

		// should detect and fill out refs
		panel.InternalRenderTree();

		Assert.IsNotNull( panel.MyReferencedElement );
		Assert.IsNotNull( panel.MyReferencedMarkup );
		Assert.AreEqual( 2, panel.Children.Count() );

		// hide the first element
		panel.ShowFirstElement = false;
		panel.InternalRenderTree();
		GlobalContext.Current.UISystem.RunDeferredDeletion();

		// reference should become null now - because it's no longer visible
		Assert.IsNull( panel.MyReferencedElement );
		Assert.IsNotNull( panel.MyReferencedMarkup );
		Assert.AreEqual( 1, panel.Children.Count() );
	}

	/// <summary>
	/// When switching elements on and off, they should all
	/// remain in the correct order.
	/// </summary>
	[TestMethod]
	public void SiblingOrder()
	{
		Panel two;
		var panel = new TestComponents.SiblingOrder();
		Assert.AreEqual( 0, panel.Children.Count() );

		two = panel.Children.FirstOrDefault( x => x.ElementName == "two" );
		Assert.IsNull( panel.One );
		Assert.IsNull( panel.Three );

		panel.TickInternal();

		two = panel.Children.FirstOrDefault( x => x.ElementName == "two" );
		Assert.AreEqual( 1, panel.Children.Count() );
		Assert.IsNull( panel.One );
		Assert.IsNotNull( panel.Three );
		Assert.AreEqual( 0, panel.Three.SiblingIndex );

		panel.ShowFirstItem = true;
		panel.InternalRenderTree();

		two = panel.Children.FirstOrDefault( x => x.ElementName == "two" );
		Assert.AreEqual( 2, panel.Children.Count() );
		Assert.IsNotNull( panel.One );
		Assert.IsNotNull( panel.Three );
		Assert.AreEqual( 0, panel.One.SiblingIndex );
		Assert.AreEqual( 1, panel.Three.SiblingIndex );

		panel.ShowMiddleItem = true;
		panel.InternalRenderTree();

		two = panel.Children.FirstOrDefault( x => x.ElementName == "two" );
		Assert.AreEqual( 3, panel.Children.Count() );
		Assert.IsNotNull( panel.One );
		Assert.IsNotNull( panel.Three );
		Assert.AreEqual( 0, panel.One.SiblingIndex );
		Assert.AreEqual( 1, two.SiblingIndex );
		Assert.AreEqual( 2, panel.Three.SiblingIndex );

		panel.ShowFirstItem = false;
		panel.InternalRenderTree();
		GlobalContext.Current.UISystem.RunDeferredDeletion();

		two = panel.Children.FirstOrDefault( x => x.ElementName == "two" );
		Assert.AreEqual( 2, panel.Children.Count() );
		Assert.IsNull( panel.One );
		Assert.IsNotNull( panel.Three );
		Assert.AreEqual( 0, two.SiblingIndex );
		Assert.AreEqual( 1, panel.Three.SiblingIndex );





		// todo check their order 
	}

	[TestMethod]
	public void TreeCreate()
	{
		var panel = new TestComponents.StopwatchPanel();
		Assert.AreEqual( 0, panel.Children.Count() );

		panel.InternalRenderTree();

		Assert.AreEqual( 11, panel.Children.Count() );

		panel.InternalRenderTree();

		Assert.AreEqual( 11, panel.Children.Count() );

		panel.ClearRenderTree();
		GlobalContext.Current.UISystem.RunDeferredDeletion();

		Assert.AreEqual( 0, panel.Children.Count() );
	}


	[TestMethod]
	public void PanelInstancing()
	{
		var panel = new TestComponents.ComponentUser();
		Assert.AreEqual( 0, panel.Children.Count() );

		Assert.AreEqual( 0, panel.CallbackCalls );
		panel.InternalRenderTree();

		Assert.AreEqual( 0, panel.CallbackCalls );
		Assert.AreEqual( 6, panel.Children.Count() );

		{
			var p = panel.Children.First();
			Assert.IsInstanceOfType( p, typeof( TestComponents.Icon ) );
			Assert.AreEqual( "One", (p as TestComponents.Icon).Title );
		}

		foreach ( var icon in panel.Children.OfType<TestComponents.Icon>() )
		{
			icon.TriggerCallback();
		}

		Assert.AreEqual( 2, panel.CallbackCalls );
		panel.InternalRenderTree();
		Assert.AreEqual( 2, panel.CallbackCalls );
		panel.InternalRenderTree();
		Assert.AreEqual( 2, panel.CallbackCalls );

		foreach ( var icon in panel.Children.OfType<TestComponents.Icon>() )
		{
			icon.TriggerCallback();
		}

		Assert.AreEqual( 4, panel.CallbackCalls );

		panel.ClearRenderTree();
		GlobalContext.Current.UISystem.RunDeferredDeletion();
		Assert.AreEqual( 0, panel.Children.Count() );

		Assert.AreEqual( 4, panel.CallbackCalls );
	}

	[TestMethod]
	public void RootPanel()
	{
		var panel = new TestComponents.RootPanel();
		Assert.AreEqual( 0, panel.Children.Count() );

		panel.InternalRenderTree();

		// no children because we just applied shit to the root panel
		Assert.AreEqual( 0, panel.Children.Count() );

		panel.ClearRenderTree();

		Assert.AreEqual( 0, panel.Children.Count() );
		Assert.IsTrue( panel.IsValid );
	}

	[TestMethod]
	public void Bind()
	{
		var panel = new TestComponents.Bind();
		Assert.AreEqual( 0, panel.Children.Count() );

		panel.InternalRenderTree();

		Assert.IsNotNull( panel.TargetPanel );
		Assert.AreEqual( "Poops", panel.TargetPanel.ElementName );
		Assert.AreEqual( "Poops", panel.PanelName );

		panel.InternalTreeBinds();

		Assert.IsNotNull( panel.TargetPanel );
		Assert.AreEqual( "Poops", panel.TargetPanel.ElementName );
		Assert.AreEqual( "Poops", panel.PanelName );

		panel.PanelName = "One";

		panel.InternalTreeBinds();

		Assert.IsNotNull( panel.TargetPanel );
		Assert.AreEqual( "One", panel.PanelName );
		Assert.AreEqual( "One", panel.TargetPanel.ElementName );

		panel.InternalTreeBinds();

		Assert.IsNotNull( panel.TargetPanel );
		Assert.AreEqual( "One", panel.PanelName );
		Assert.AreEqual( "One", panel.TargetPanel.ElementName );

		panel.TargetPanel.ElementName = "Two";

		panel.InternalTreeBinds();

		Assert.IsNotNull( panel.TargetPanel );
		Assert.AreEqual( "Two", panel.PanelName );
		Assert.AreEqual( "Two", panel.TargetPanel.ElementName );

	}
}
