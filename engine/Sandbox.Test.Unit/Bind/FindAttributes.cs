using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.ComponentModel.DataAnnotations;

namespace TestBind;

[TestClass]
public class FindAttributes
{
	public string Primary { get; set; }

	[Display( Name = "Fuck" )]
	public string Secondary { get; set; }

	[TestMethod]
	public void Simple()
	{
		Primary = "Dog";
		Secondary = "Cat";

		var bind = new Sandbox.Bind.BindSystem( "test" );
		bind.Build.Set( this, "Primary" ).From( this, "Secondary" );

		var attributes = bind.FindAttributes( this, nameof( Primary ) );
		Assert.IsNotNull( attributes );
		Assert.AreEqual( 1, attributes.Length );
	}

}
