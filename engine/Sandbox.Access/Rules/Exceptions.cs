namespace Sandbox;

internal static partial class Rules
{
	internal static string[] Exceptions = new[]
	{
		"System.Private.CoreLib/System.Exception*",
		"System.Private.CoreLib/System.AggregateException*",
		"System.Private.CoreLib/System.AccessViolationException*",
		"System.Private.CoreLib/System.ArgumentException*",
		"System.Private.CoreLib/System.ArgumentNullException*",
		"System.Private.CoreLib/System.ArgumentOutOfRangeException*",
		"System.Private.CoreLib/System.ArithmeticException*",
		"System.Private.CoreLib/System.ArrayTypeMismatchException*",
		"System.Private.CoreLib/System.DivideByZeroException*",
		"System.Private.CoreLib/System.FormatException*",
		"System.Private.CoreLib/System.IndexOutOfRangeException*",
		"System.Private.CoreLib/System.InvalidCastException*",
		"System.Private.CoreLib/System.InvalidOperationException*",
		"System.Private.CoreLib/System.NotImplementedException*",
		"System.Private.CoreLib/System.NotSupportedException*",
		"System.Private.CoreLib/System.NullReferenceException*",
		"System.Private.CoreLib/System.OperationCanceledException*",
		"System.Private.CoreLib/System.PlatformNotSupportedException*",
		"System.Private.CoreLib/System.TimeoutException*",
		"System.Private.CoreLib/System.UnauthorizedAccessException*",
		"System.Private.CoreLib/System.IO.IOException*",
		"System.Private.CoreLib/System.IO.FileNotFoundException*",
		"System.Private.CoreLib/System.IO.DirectoryNotFoundException*",
		"System.Private.CoreLib/System.IO.FileLoadException*",
		"System.Private.CoreLib/System.IO.PathTooLongException*",
		"System.Private.CoreLib/System.IO.EndOfStreamException*",

		// InvalidDataException - explicit entries (no wildcard). Only adds its 3 ctors; inherited
		// members are already covered by System.Exception*.
		"System.Private.CoreLib/System.IO.InvalidDataException",
		"System.Private.CoreLib/System.IO.InvalidDataException..ctor()",
		"System.Private.CoreLib/System.IO.InvalidDataException..ctor( System.String )",
		"System.Private.CoreLib/System.IO.InvalidDataException..ctor( System.String, System.Exception )",

		"System.Private.CoreLib/System.Runtime.CompilerServices.SwitchExpressionException*",
	};
}
