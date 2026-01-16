using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.ComponentModel.DataAnnotations;

namespace TestBind;

[TestClass]
public class FindAttributes
{
	[TestMethod]
	public void Simple()
	{
		var target = new BindingTarget
		{
			Primary = "Dog",
			Secondary = "Cat"
		};
		var bind = new Sandbox.Bind.BindSystem( "test" );
		bind.Build.Set( target, nameof( BindingTarget.Primary ) ).From( target, nameof( BindingTarget.Secondary ) );

		var attributes = bind.FindAttributes( target, nameof( BindingTarget.Primary ) );
		Assert.IsNotNull( attributes );
		Assert.AreEqual( 1, attributes.Length );
	}

	private sealed class BindingTarget
	{
		public string Primary { get; set; }

		[Display( Name = "Secondary" )]
		public string Secondary { get; set; }
	}

}
