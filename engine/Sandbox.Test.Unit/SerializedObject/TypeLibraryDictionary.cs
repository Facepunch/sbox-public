using System;
using System.Collections.Generic;
using Sandbox;
using Sandbox.Internal;
using Sandbox.Utility;
using static SerializedObject.From_TypeLibrary;

namespace SerializedObject;

[TestClass]
public partial class From_TypeLibraryDictionary
{
	TypeLibrary typeLibrary;

	public From_TypeLibraryDictionary()
	{
		typeLibrary = new Sandbox.Internal.TypeLibrary();
		typeLibrary.AddAssembly( GetType().Assembly, true );
	}

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
