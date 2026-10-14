using System;
using System.Collections.Generic;
using Sandbox;
using Sandbox.Internal;
using Sandbox.Utility;
using static SerializedObjectTests.SerializedObjectTest;

namespace SerializedObjectTests;

[TestClass]
public partial class SerializedDictionaryTest
{
	// Scanning the test assembly is slow, so build it once. Tests only read through it, which is safe in parallel.
	static readonly Lazy<TypeLibrary> SharedLibrary = new( () =>
	{
		var library = new TypeLibrary();
		library.AddAssembly( typeof( SerializedDictionaryTest ).Assembly, true );
		return library;
	} );

	TypeLibrary typeLibrary => SharedLibrary.Value;

	class MyClass
	{
		public string String { get; set; }
		public Vector3 Vector3 { get; set; }
		public Transform Transform { get; set; }
		public float Float { get; set; }
		public Color Color { get; set; }
		public MyDeepStruct DeepStruct { get; set; }
		public MyDeepClass DeepClass { get; set; }
		public List<string> StringList { get; set; }
	}

	public struct MyDeepStruct
	{
		public string String { get; set; }
		public Transform Transform { get; set; }
		public Color Color { get; set; }
	}

	public class MyDeepClass
	{
		public string String { get; set; }
		public Transform Transform { get; set; }
		public Color Color { get; set; }
	}
}
