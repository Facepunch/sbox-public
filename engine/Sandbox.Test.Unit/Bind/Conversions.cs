using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Text.Json;

namespace TestBind;


[TestClass]
public class Conversions
{
	[TestMethod]
	public void StringToBool()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			BoolValue = false,
			StringValue = "true"
		};
		bind.Build.Set( testObject, nameof( BindingTarget.BoolValue ) ).From( testObject, nameof( BindingTarget.StringValue ) );
		bind.Tick();
		Assert.AreEqual( true, testObject.BoolValue );

		testObject.StringValue = "false";
		bind.Tick();
		Assert.AreEqual( false, testObject.BoolValue );

		testObject.StringValue = "1";
		bind.Tick();
		Assert.AreEqual( true, testObject.BoolValue );

		testObject.StringValue = "0";
		bind.Tick();
		Assert.AreEqual( false, testObject.BoolValue );

		testObject.StringValue = "yes";
		bind.Tick();
		Assert.AreEqual( true, testObject.BoolValue );

		testObject.StringValue = "no";
		bind.Tick();
		Assert.AreEqual( false, testObject.BoolValue );
	}

	[TestMethod]
	public void StringToFloat()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			StringValue = "17.85"
		};
		bind.Build.Set( testObject, nameof( BindingTarget.FloatValue ) ).From( testObject, nameof( BindingTarget.StringValue ) );
		bind.Tick();
		Assert.AreEqual( 17.85f, testObject.FloatValue );
	}

	[TestMethod]
	public void FloatToString()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			StringValue = "Bullshit",
			FloatValue = 17.85f
		};
		bind.Build.Set( testObject, nameof( BindingTarget.StringValue ) ).From( testObject, nameof( BindingTarget.FloatValue ) );
		bind.Tick();
		Assert.AreEqual( "17.85", testObject.StringValue );
	}

	[TestMethod]
	public void StringToFloat_Invalid()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			StringValue = "poopy",
			FloatValue = 44.0f
		};
		bind.Build.Set( testObject, nameof( BindingTarget.FloatValue ) ).From( testObject, nameof( BindingTarget.StringValue ) );
		bind.Tick();
		Assert.AreEqual( 44.0f, testObject.FloatValue );
	}

	[TestMethod]
	public void StringToEnum()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			StringValue = "Winter",
			EnumValue = Season.Spring
		};
		bind.Build.Set( testObject, nameof( BindingTarget.EnumValue ) ).From( testObject, nameof( BindingTarget.StringValue ) );
		bind.Tick();
		Assert.AreEqual( Season.Winter, testObject.EnumValue );
	}

	[TestMethod]
	public void EnumToString()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			StringValue = "Bullshit",
			EnumValue = Season.Spring
		};
		bind.Build.Set( testObject, nameof( BindingTarget.StringValue ) ).From( testObject, nameof( BindingTarget.EnumValue ) );
		bind.Tick();
		Assert.AreEqual( "Spring", testObject.StringValue );
	}

	[TestMethod]
	public void EnumToInt()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			IntValue = 5435324,
			EnumValue = Season.Autumn
		};
		bind.Build.Set( testObject, nameof( BindingTarget.IntValue ) ).From( testObject, nameof( BindingTarget.EnumValue ) );
		bind.Tick();
		Assert.AreEqual( (int)Season.Autumn, testObject.IntValue );
	}

	[TestMethod]
	public void IntToEnum()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			IntValue = 2,
			EnumValue = Season.Spring
		};
		bind.Build.Set( testObject, nameof( BindingTarget.EnumValue ) ).From( testObject, nameof( BindingTarget.IntValue ) );
		bind.Tick();
		Assert.AreEqual( Season.Autumn, testObject.EnumValue );
	}

	[TestMethod]
	public void StringToEnum_Invalid()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			StringValue = "8gf8324g"
		};
		bind.Build.Set( testObject, nameof( BindingTarget.StringValue ) ).From( testObject, nameof( BindingTarget.EnumValue ) );
		bind.Tick();
		Assert.AreEqual( Season.Spring, testObject.EnumValue );
	}

	[TestMethod]
	public void JsonElementToString()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			StringValue = "17.85",
			JsonElement = JsonDocument.Parse( "\"Hello There\"" ).RootElement
		};
		bind.Build.Set( testObject, nameof( BindingTarget.StringValue ) ).From( testObject, nameof( BindingTarget.JsonElement ) );
		bind.Tick();
		Assert.AreEqual( "Hello There", testObject.StringValue );
	}

	[TestMethod]
	public void JsonElementToFloat()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			FloatValue = 17.85f,
			JsonElement = JsonDocument.Parse( "2134.54" ).RootElement
		};
		bind.Build.Set( testObject, nameof( BindingTarget.FloatValue ) ).From( testObject, nameof( BindingTarget.JsonElement ) );
		bind.Tick();
		Assert.AreEqual( 2134.54f, testObject.FloatValue );
	}

	[TestMethod]
	public void ArrayToList()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			ArrayValue = new[] { "One", "Two", "Three" },
			ListValue = null
		};
		bind.Build.Set( testObject, nameof( BindingTarget.ListValue ) ).From( testObject, nameof( BindingTarget.ArrayValue ) );
		bind.Tick();
		Assert.AreEqual( "One", testObject.ListValue[0] );
		Assert.AreEqual( "Two", testObject.ListValue[1] );
		Assert.AreEqual( "Three", testObject.ListValue[2] );
	}

	[TestMethod]
	public void ListToArray()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			ArrayValue = new[] { "One", "Two", "Three" },
			ListValue = new List<string> { "Banana", "Apple" }
		};
		bind.Build.Set( testObject, nameof( BindingTarget.ArrayValue ) ).From( testObject, nameof( BindingTarget.ListValue ) );
		bind.Tick();
		Assert.AreEqual( "Banana", testObject.ArrayValue[0] );
		Assert.AreEqual( "Apple", testObject.ArrayValue[1] );

		testObject.ListValue[0] = "BEAR";
		bind.Tick();

		Assert.AreEqual( "BEAR", testObject.ArrayValue[0] );
		Assert.AreEqual( "Apple", testObject.ArrayValue[1] );
	}

	[TestMethod]
	public void DetectListChanges()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var testObject = new BindingTarget
		{
			ArrayValue = new[] { "One", "Two", "Three" },
			ListValue = new List<string> { "Banana", "Apple" }
		};
		bind.Build.Set( testObject, nameof( BindingTarget.ArrayValue ) ).From( testObject, nameof( BindingTarget.ListValue ) );
		bind.Tick();
		Assert.AreEqual( "Banana", testObject.ArrayValue[0] );
		Assert.AreEqual( "Apple", testObject.ArrayValue[1] );

		testObject.ListValue[0] = "BearFace";

		bind.Tick();

		Assert.AreEqual( "BearFace", testObject.ArrayValue[0] );
	}

	public enum Season
	{
		Spring,
		Summer,
		Autumn,
		Winter
	}

	private sealed class BindingTarget
	{
		public string StringValue { get; set; } = "Hello";
		public float FloatValue { get; set; } = 66.43f;
		public Season EnumValue { get; set; } = Season.Spring;
		public int IntValue { get; set; } = 3;
		public bool BoolValue { get; set; }
		public JsonElement JsonElement { get; set; }
		public string[] ArrayValue { get; set; }
		public List<string> ListValue { get; set; }
	}
}
