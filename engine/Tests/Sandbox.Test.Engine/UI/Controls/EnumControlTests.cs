using Sandbox.UI;
using System;
using static UITests.UiTesting;

namespace UITests.Controls;

/// <summary>
/// Exercises enum selection through the controls created for a bound property.
/// </summary>
[TestClass]
public class EnumControlTests
{
	/// <summary>
	/// Changing the layout or rebinding replaces the controls and keeps the selected value in sync.
	/// </summary>
	[TestMethod]
	public void SwitchLayoutAndRebind()
	{
		ThreadSafe.MarkMainThread();
		var root = CreateRoot();
		try
		{
			var kind = DateTimeKind.Utc;
			var property = Game.TypeLibrary.CreateProperty<DateTimeKind>( "Kind", () => kind, value => kind = value );
			var control = root.AddChild( new EnumControl { Property = property } );
			Assert.IsInstanceOfType<ButtonGroup>( control.Children.Single() );

			control.ButtonGroupOptionCount = 2;
			var dropdown = (DropDown)control.Children.Single();
			Assert.AreEqual( kind, dropdown.Selected.Value );
			dropdown.Open();
			root.TickInternal();
			var popup = root.Descendants.OfType<Popup>().Single();
			Assert.AreEqual( dropdown.Text, popup.ChildrenOfType<Button>().Single( button => button.Active ).Text );
			var local = dropdown.Options.Single( option => Equals( option.Value, DateTimeKind.Local ) );
			Click( root, popup.ChildrenOfType<Button>().Single( button => button.Text == local.Title ) );
			Assert.AreEqual( DateTimeKind.Local, kind );

			kind = DateTimeKind.Unspecified;
			root.TickInternal();
			Assert.AreEqual( kind, dropdown.Selected.Value );

			var search = System.IO.SearchOption.TopDirectoryOnly;
			control.Property = Game.TypeLibrary.CreateProperty<System.IO.SearchOption>( "Search", () => search, value => search = value );
			var buttons = (ButtonGroup)control.Children.Single();
			var first = buttons.ChildrenOfType<Button>().Single( button => Equals( button.Value, search ) );
			search = System.IO.SearchOption.AllDirectories;
			root.TickInternal();
			Assert.AreEqual( search, buttons.Value );
			Click( root, first );
			Assert.AreEqual( System.IO.SearchOption.TopDirectoryOnly, search );
			Assert.IsTrue( first.Active );
		}
		finally
		{
			root.Delete( true );
		}
	}
}
