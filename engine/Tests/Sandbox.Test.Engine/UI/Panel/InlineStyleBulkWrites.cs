using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Engine;
using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize]
public class InlineStyleBulkReliabilityTests
{
	[TestCleanup]
	public void CleanupBulkStylePanels()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	static Panel CreateBulkStylePanel()
	{
		var panel = new Panel();
		panel.Style.Width = 100;
		panel.SetProperty( "style", "width: 200px;" );
		Assert.AreEqual( 200f, panel.Style.Width.Value.Value );
		return panel;
	}

	[TestMethod]
	public void BulkCloneWriteDoesNotClaimOriginalAttributeProperty()
	{
		var panel = CreateBulkStylePanel();
		// Exercise independence even when the source already tracks another C# write.
		panel.Style.Height = 25;
		var copy = (BaseStyles)panel.Style.Clone();
		Assert.AreNotSame( panel.Style, copy );

		copy.Width = 200;
		Assert.AreEqual( 200f, copy.Width.Value.Value );
		panel.SetProperty( "style", "" );

		Assert.AreEqual( 100f, panel.Style.Width.Value.Value, "A write on the clone must not claim the original panel's attribute value." );
		Assert.AreEqual( 25f, panel.Style.Height.Value.Value );
		Assert.AreEqual( 200f, copy.Width.Value.Value, "Restoring the original panel must not change its style copy." );
	}

	[TestMethod]
	public void BulkLerpPropertyReturningToAttributeValueKeepsProgrammaticWidth()
	{
		var panel = CreateBulkStylePanel();
		var from = new Styles { Width = 100 };
		var to = new Styles { Width = 300 };
		panel.Style.LerpProperty( "width", from, to, 0.25f );
		Assert.AreEqual( 150f, panel.Style.Width.Value.Value, 0.001f );
		panel.Style.LerpProperty( "width", from, to, 0.5f );
		Assert.AreEqual( 200f, panel.Style.Width.Value.Value, 0.001f );

		panel.SetProperty( "style", "" );

		Assert.AreEqual( 200f, panel.Style.Width.Value.Value, 0.001f, "The latest interpolated width belongs to C# even when it equals the earlier attribute value." );
	}

	[TestMethod]
	public void BulkFromLerpReturningToAttributeValueKeepsProgrammaticWidth()
	{
		var panel = CreateBulkStylePanel();
		var from = new Styles { Width = 100 };
		var to = new Styles { Width = 300 };
		panel.Style.FromLerp( from, to, 0.25f );
		Assert.AreEqual( 150f, panel.Style.Width.Value.Value, 0.001f );
		panel.Style.FromLerp( from, to, 0.5f );
		Assert.AreEqual( 200f, panel.Style.Width.Value.Value, 0.001f );

		panel.SetProperty( "style", "" );

		Assert.AreEqual( 200f, panel.Style.Width.Value.Value, 0.001f, "Bulk interpolation must preserve its later C# write through attribute removal." );
	}

	[TestMethod]
	public void BulkApplyScaleRoundTripKeepsProgrammaticWidth()
	{
		var panel = CreateBulkStylePanel();
		panel.Style.ApplyScale( 2f );
		Assert.AreEqual( 400f, panel.Style.Width.Value.Value, 0.001f );
		panel.Style.ApplyScale( 0.5f );
		Assert.AreEqual( 200f, panel.Style.Width.Value.Value, 0.001f );

		panel.SetProperty( "style", "" );

		Assert.AreEqual( 200f, panel.Style.Width.Value.Value, 0.001f, "Scaling is an explicit C# mutation even when a round trip restores the attribute's numeric value." );
	}

	[TestMethod]
	public void BulkApplyCascadingDoesNotClaimNonInheritedAttributeWidth()
	{
		var panel = CreateBulkStylePanel();
		var parent = new Styles { Width = 900, FontColor = Color.Red };
		panel.Style.ApplyCascading( parent );
		Assert.AreEqual( Color.Red, panel.Style.FontColor.Value, "The inherited style proves the cascade operation ran." );
		Assert.AreEqual( 200f, panel.Style.Width.Value.Value );

		panel.SetProperty( "style", "" );

		Assert.AreEqual( 100f, panel.Style.Width.Value.Value, "Width is not inherited, so this operation must not claim the attribute's existing width." );
		Assert.AreEqual( Color.Red, panel.Style.FontColor.Value );
	}

	[TestMethod]
	public void BulkFillDefaultsDoesNotClaimAlreadyDefinedAttributeWidth()
	{
		var panel = CreateBulkStylePanel();
		panel.Style.FillDefaults();
		Assert.IsTrue( panel.Style.Height.HasValue, "An initially unset property should receive its default." );
		Assert.AreEqual( 200f, panel.Style.Width.Value.Value );

		panel.SetProperty( "style", "" );

		Assert.AreEqual( 100f, panel.Style.Width.Value.Value, "Filling absent defaults must not claim an already defined width supplied by the attribute." );
	}

	[TestMethod]
	public void BulkFromLerpDoesNotRetainUnrelatedAttributeHeight()
	{
		var panel = new Panel();
		panel.Style.Width = 100;
		panel.SetProperty( "style", "width: 200px; height: 50px;" );
		var from = new Styles { Width = 100 };
		var to = new Styles { Width = 300 };

		panel.Style.FromLerp( from, to, 0.5f );
		Assert.AreEqual( 200f, panel.Style.Width.Value.Value, 0.001f );
		Assert.AreEqual( 50f, panel.Style.Height.Value.Value );
		panel.SetProperty( "style", "" );

		Assert.AreEqual( 200f, panel.Style.Width.Value.Value, 0.001f );
		Assert.IsNull( panel.Style.Height, "Neither interpolation endpoint supplied Height, so its old attribute declaration must still be removed." );
	}

	[TestMethod]
	public void BulkLerpWithNoEndpointValueDoesNotClaimAttributeWidth()
	{
		var panel = CreateBulkStylePanel();

		panel.Style.LerpProperty( "width", new Styles(), new Styles(), 0.5f );
		Assert.AreEqual( 200f, panel.Style.Width.Value.Value );
		panel.SetProperty( "style", "" );

		Assert.AreEqual( 100f, panel.Style.Width.Value.Value, "Interpolation with two absent endpoints performs no width write and must not claim the attribute's width." );
	}
}
