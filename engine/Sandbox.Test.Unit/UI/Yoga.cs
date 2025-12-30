using Sandbox.UI;

namespace UITest;

[TestClass]
public partial class Yoga
{
	[TestMethod]
	public void YogaMinimal()
	{
		var root = new YogaWrapper( null );
		root.FlexDirection = FlexDirection.Column;
		root.AlignItems = Align.Stretch;
		root.Width = 100.0f;
		root.Height = 100.0f;

		var child = new YogaWrapper( null );
		child.FlexGrow = 1;
		root.AddChild( child );

		root.CalculateLayout();

		Assert.AreEqual( 100.0f, child.LayoutWidth );
		Assert.AreEqual( 100.0f, child.LayoutHeight );
		Assert.AreEqual( 0f, child.LayoutX );
		Assert.AreEqual( 0f, child.LayoutY );
	}

	[TestMethod]
	public void YogaSanity()
	{
		var root = new YogaWrapper( null );
		root.Width = 1920;
		root.Height = 1080;

		var child = new YogaWrapper( null );
		child.PositionType = PositionMode.Absolute;
		child.Top = 64;
		child.Left = 32;
		child.Width = 100;
		child.Height = 100;

		root.AddChild( child );

		var child2 = new YogaWrapper( null );
		child2.PositionType = PositionMode.Absolute;
		child2.Right = 64;
		child2.Bottom = 32;
		child2.Width = 100;
		child2.Height = 100;

		root.AddChild( child2 );

		root.CalculateLayout();

		Assert.AreEqual( 1920f, root.LayoutWidth );
		Assert.AreEqual( 1080f, root.LayoutHeight );
		Assert.AreEqual( 0f, root.LayoutX );
		Assert.AreEqual( 0f, root.LayoutY );

		Assert.AreEqual( 32f, child.LayoutX );
		Assert.AreEqual( 64f, child.LayoutY );

		Assert.AreEqual( 1756f, child2.LayoutX );
		Assert.AreEqual( 948f, child2.LayoutY );
	}
}
