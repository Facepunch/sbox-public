namespace Sandbox;

internal static partial class Rules
{
	internal static string[] Reflection = new[]
	{
		"System.Private.CoreLib/System.Reflection.CustomAttributeExtensions*",
		"System.Private.CoreLib/System.Reflection.ICustomAttributeProvider*",
		"System.Private.CoreLib/System.Reflection.BindingFlags*",

		//
		// Very basic interaction
		//
		"System.Private.CoreLib/System.Reflection.MemberInfo",
		"System.Private.CoreLib/System.Reflection.MemberInfo.get_Name()",
		"System.Private.CoreLib/System.Reflection.MemberInfo.IsDefined( System.Type, System.Boolean )",
		"System.Private.CoreLib/System.Reflection.MemberInfo.get_DeclaringType()",

		//
		// Very basic interaction
		//
		"System.Private.CoreLib/System.Reflection.PropertyInfo",
		"System.Private.CoreLib/System.Reflection.PropertyInfo.GetSetMethod()",
		"System.Private.CoreLib/System.Reflection.PropertyInfo.GetGetMethod()",
		"System.Private.CoreLib/System.Reflection.PropertyInfo.get_CanWrite()",
		"System.Private.CoreLib/System.Reflection.PropertyInfo.get_CanRead()",
		"System.Private.CoreLib/System.Reflection.PropertyInfo.get_PropertyType()",
		"System.Private.CoreLib/System.Reflection.PropertyInfo.op_Inequality( System.Reflection.PropertyInfo, System.Reflection.PropertyInfo )",
		"System.Private.CoreLib/System.Reflection.PropertyInfo.op_Equality( System.Reflection.PropertyInfo, System.Reflection.PropertyInfo )",

		//
		// Very basic interaction
		//
		"System.Private.CoreLib/System.Reflection.MethodInfo",
		"System.Private.CoreLib/System.Reflection.MethodInfo.op_Equality*",

		//
		// Very basic interaction
		//
		"System.Private.CoreLib/System.Reflection.ParameterInfo",
		"System.Private.CoreLib/System.Reflection.ParameterInfo.get_Name*",
		"System.Private.CoreLib/System.Reflection.ParameterInfo.get_DefaultValue()",
		"System.Private.CoreLib/System.Reflection.ParameterInfo.get_IsOptional()",
		"System.Private.CoreLib/System.Reflection.ParameterInfo.get_ParameterType()",
		"System.Private.CoreLib/System.Reflection.ParameterInfo.get_IsIn()",
		"System.Private.CoreLib/System.Reflection.ParameterInfo.get_IsOut()",
		"System.Private.CoreLib/System.Reflection.ParameterInfo.GetCustomAttributesData()",

		//
		// Pretty safe, but still cautious
		//
		"System.Private.CoreLib/System.Reflection.CustomAttributeData",
		"System.Private.CoreLib/System.Reflection.CustomAttributeData.get_AttributeType()",
		"System.Private.CoreLib/System.Reflection.CustomAttributeData.get_NamedArguments()",
		"System.Private.CoreLib/System.Reflection.CustomAttributeData.get_ConstructorArguments()",
		"System.Private.CoreLib/System.Reflection.CustomAttributeTypedArgument*",
		"System.Private.CoreLib/System.Reflection.CustomAttributeNamedArgument*",
	};
}
