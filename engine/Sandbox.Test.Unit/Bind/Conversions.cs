using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Text.Json;

namespace TestBind;


[TestClass]
public class Conversions
{
	public string StringValue { get; set; } = "Hello";
	public float FloatValue { get; set; } = 66.43f;
	public Season EnumValue { get; set; } = Season.Spring;
	public int IntValue { get; set; } = 3;
	public bool BoolValue { get; set; }


	[TestMethod]
	public void StringToBool()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		BoolValue = false;
		StringValue = "true";
		bind.Build.Set( this, "BoolValue" ).From( this, "StringValue" );
		bind.Tick();
		Assert.AreEqual( true, BoolValue );

		StringValue = "false";
		bind.Tick();
		Assert.AreEqual( false, BoolValue );

		StringValue = "1";
		bind.Tick();
		Assert.AreEqual( true, BoolValue );

		StringValue = "0";
		bind.Tick();
		Assert.AreEqual( false, BoolValue );

		StringValue = "yes";
		bind.Tick();
		Assert.AreEqual( true, BoolValue );

		StringValue = "no";
		bind.Tick();
		Assert.AreEqual( false, BoolValue );
	}

	[TestMethod]
	public void StringToFloat()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		StringValue = "17.85";
		bind.Build.Set( this, "FloatValue" ).From( this, "StringValue" );
		bind.Tick();
		Assert.AreEqual( 17.85f, FloatValue );
	}

	[TestMethod]
	public void FloatToString()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		StringValue = "Bullshit";
		FloatValue = 17.85f;
		bind.Build.Set( this, "StringValue" ).From( this, "FloatValue" );
		bind.Tick();
		Assert.AreEqual( "17.85", StringValue );
	}

	[TestMethod]
	public void StringToFloat_Invalid()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		StringValue = "poopy";
		FloatValue = 44.0f;
		bind.Build.Set( this, "FloatValue" ).From( this, "StringValue" );
		bind.Tick();
		Assert.AreEqual( 44.0f, FloatValue );
	}

	[TestMethod]
	public void StringToEnum()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		StringValue = "Winter";
		EnumValue = Season.Spring;
		bind.Build.Set( this, "EnumValue" ).From( this, "StringValue" );
		bind.Tick();
		Assert.AreEqual( Season.Winter, EnumValue );
	}

	[TestMethod]
	public void EnumToString()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		StringValue = "Bullshit";
		EnumValue = Season.Spring;
		bind.Build.Set( this, "StringValue" ).From( this, "EnumValue" );
		bind.Tick();
		Assert.AreEqual( "Spring", StringValue );
	}

	[TestMethod]
	public void EnumToInt()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		IntValue = 5435324;
		EnumValue = Season.Autumn;
		bind.Build.Set( this, "IntValue" ).From( this, "EnumValue" );
		bind.Tick();
		Assert.AreEqual( (int)(Season.Autumn), IntValue );
	}

	[TestMethod]
	public void IntToEnum()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		IntValue = 2;
		EnumValue = Season.Spring;
		bind.Build.Set( this, "EnumValue" ).From( this, "IntValue" );
		bind.Tick();
		Assert.AreEqual( Season.Autumn, EnumValue );
	}

	[TestMethod]
	public void StringToEnum_Invalid()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		StringValue = "8gf8324g";
		bind.Build.Set( this, "StringValue" ).From( this, "EnumValue" );
		bind.Tick();
		Assert.AreEqual( Season.Spring, EnumValue );
	}

	public JsonElement JsonElement { get; set; }

	[TestMethod]
	public void JsonElementToString()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		StringValue = "17.85";
		JsonElement = JsonDocument.Parse( "\"Hello There\"" ).RootElement;
		bind.Build.Set( this, "StringValue" ).From( this, "JsonElement" );
		bind.Tick();
		Assert.AreEqual( "Hello There", StringValue );
	}

	[TestMethod]
	public void JsonElementToFloat()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		FloatValue = 17.85f;
		JsonElement = JsonDocument.Parse( "2134.54" ).RootElement;
		bind.Build.Set( this, "FloatValue" ).From( this, "JsonElement" );
		bind.Tick();
		Assert.AreEqual( 2134.54f, FloatValue );
	}

	public string[] ArrayValue { get; set; }
	public List<string> ListValue { get; set; }

	[TestMethod]
	public void ArrayToList()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		ArrayValue = new string[] { "One", "Two", "Three" };
		ListValue = null;
		bind.Build.Set( this, "ListValue" ).From( this, "ArrayValue" );
		bind.Tick();
		Assert.AreEqual( "One", ListValue[0] );
		Assert.AreEqual( "Two", ListValue[1] );
		Assert.AreEqual( "Three", ListValue[2] );
	}

	[TestMethod]
	public void ListToArray()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		ArrayValue = new string[] { "One", "Two", "Three" };
		ListValue = new List<string> { "Banana", "Apple" };
		bind.Build.Set( this, "ArrayValue" ).From( this, "ListValue" );
		bind.Tick();
		Assert.AreEqual( "Banana", ArrayValue[0] );
		Assert.AreEqual( "Apple", ArrayValue[1] );

		ListValue[0] = "BEAR";
		bind.Tick();

		Assert.AreEqual( "BEAR", ArrayValue[0] );
		Assert.AreEqual( "Apple", ArrayValue[1] );
	}

	[TestMethod]
	public void DetectListChanges()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		ArrayValue = new string[] { "One", "Two", "Three" };
		ListValue = new List<string> { "Banana", "Apple" };
		bind.Build.Set( this, "ArrayValue" ).From( this, "ListValue" );
		bind.Tick();
		Assert.AreEqual( "Banana", ArrayValue[0] );
		Assert.AreEqual( "Apple", ArrayValue[1] );

		ListValue[0] = "CuntFace";

		bind.Tick();

		Assert.AreEqual( "CuntFace", ArrayValue[0] );
	}


	public enum Season
	{
		Spring,
		Summer,
		Autumn,
		Winter
	}
}
