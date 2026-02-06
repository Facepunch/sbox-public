using Sandbox;
using System;

[AttributeUsage( AttributeTargets.Property )]
[CodeGenerator( CodeGeneratorFlags.WrapPropertyGet | CodeGeneratorFlags.Instance, "OnWrapGet" )]
[CodeGenerator( CodeGeneratorFlags.WrapPropertyGet | CodeGeneratorFlags.Static, "WrapGet.OnWrapGetStatic" )]
public class WrapGet : Attribute
{
	public static T OnWrapGetStatic<T>( WrappedPropertyGet<T> p )
	{
		return null;
	}
}

public class MyTestClass
{
	
}

public partial class TestWrapGet
{
	[WrapGet]
	public static bool StaticProperty { get; set; }
	
	[WrapGet]
	public bool InstanceProperty { get; set; } = true;

	[WrapGet]
	public bool Test
	{
		get
		{
			return true;
		}
		set
		{
			InstanceProperty = true;
		}
	}
	
	[WrapGet]
	public bool ComplexGetter
	{
		get
		{
			if ( true )
			{
				field = value;
			}
			else
			{
				SomeFunction();
			}
		}
		set;
	}
	
	void SomeFunction();
	
	private bool _hasNoGetterToWrap;
	
	[WrapGet]
	public bool HasNoGetterToWrap
	{
		set
		{
			_hasNoGetterToWrap = true;
		}
	}
	
	[WrapGet]
	public MyTestClass InstanceProperty2 { get; }

	internal T OnWrapGet<T>( WrappedPropertyGet<T> p )
	{
		return p.Value;
	}
	
	[WrapGet]
	public bool FieldKeywordProperty
	{
		set
		{
			field = value;
		}
		get
		{
			return field;
		}
	}
	
	[WrapGet]
	public bool FieldKeywordPropertyAuto
	{
		set
		{
			field = value;
		}
		get;
	}
}
