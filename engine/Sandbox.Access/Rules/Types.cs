using Microsoft.CodeAnalysis;
using System;
using System.Text.RegularExpressions;

namespace Sandbox;

internal static partial class Rules
{
	internal static string[] Types = new[]
	{
		"System.Private.CoreLib/System.Object*",
		"!System.Private.CoreLib/System.Object.MemberwiseClone*",
		"System.Private.CoreLib/System.Void*",
		"System.Private.CoreLib/System.Boolean*",
		"System.Private.CoreLib/System.Double*",
		"System.Private.CoreLib/System.Decimal*",
		"System.Private.CoreLib/System.Int16*",
		"System.Private.CoreLib/System.UInt16*",
		"System.Private.CoreLib/System.Int32*",
		"System.Private.CoreLib/System.UInt32*",
		"System.Private.CoreLib/System.Int64*",
		"System.Private.CoreLib/System.UInt64*",
		"System.Private.CoreLib/System.Int128*",
		"System.Private.CoreLib/System.UInt128*",
		"System.Private.CoreLib/System.IntPtr*",
		"System.Private.CoreLib/System.Single*",
		"System.Private.CoreLib/System.Char*",
		"System.Private.CoreLib/System.Byte*",
		"System.Private.CoreLib/System.SByte*",
		"System.Private.CoreLib/System.String*",
		"System.Private.CoreLib/System.Array*",
		"!System.Private.CoreLib/System.Array.Clone*",
		"System.Private.CoreLib/System.Half*",
		"System.Private.CoreLib/System.TypeCode*",
		"System.Private.CoreLib/System.Action*",
		"System.Private.CoreLib/System.Func*",
		"System.Private.CoreLib/System.Nullable*",
		"System.Private.CoreLib/System.Predicate*",

		//
		// Not too much - don't let them get methods or anything
		//
		"System.Private.CoreLib/System.Type",
		"System.Private.CoreLib/System.Type.Equals( System.Type )",
		"System.Private.CoreLib/System.Type.get_BaseType()",
		"System.Private.CoreLib/System.Type.get_ContainsGenericParameters()",
		"System.Private.CoreLib/System.Type.get_FullName()",
		"System.Private.CoreLib/System.Type.get_IsAbstract()",
		"System.Private.CoreLib/System.Type.get_IsClass()",
		"System.Private.CoreLib/System.Type.get_IsEnum()",
		"System.Private.CoreLib/System.Type.get_IsGenericType()",
		"System.Private.CoreLib/System.Type.get_IsPrimitive()", // Boolean classification only; no reflection accessors or handles.
		"System.Private.CoreLib/System.Type.get_IsValueType()",
		"System.Private.CoreLib/System.Type.GetEnumNames()",
		"System.Private.CoreLib/System.Type.GetEnumValues()",
		"System.Private.CoreLib/System.Type.GetInterfaces()",
		"System.Private.CoreLib/System.Type.GetTypeCode( System.Type )",
		"System.Private.CoreLib/System.Type.GetTypeFromHandle( System.RuntimeTypeHandle )",
		"System.Private.CoreLib/System.Type.IsAssignableFrom( System.Type )",
		"System.Private.CoreLib/System.Type.IsAssignableTo( System.Type )",
		"System.Private.CoreLib/System.Type.IsSubclassOf( System.Type )",
		"System.Private.CoreLib/System.Type.op_Equality( System.Type, System.Type )",
		"System.Private.CoreLib/System.Type.op_Inequality( System.Type, System.Type )"
	};
}
