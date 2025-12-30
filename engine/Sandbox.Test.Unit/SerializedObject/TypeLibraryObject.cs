using Sandbox.Internal;
using System.Collections.Generic;

namespace SerializedObject;

[TestClass]
public partial class From_TypeLibrary
{
	TypeLibrary typeLibrary;

	public From_TypeLibrary()
	{
		typeLibrary = new Sandbox.Internal.TypeLibrary();
		typeLibrary.AddAssembly( GetType().Assembly, true );
		typeLibrary.AddAssembly( typeof( Vector3 ).Assembly, true );
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

		public void SetColorRed()
		{
			Color = Color.Red;
		}
	}

	public struct MyDeepStruct
	{
		public string String { get; set; }
		public Transform Transform { get; set; }
		public Color Color { get; set; }
		public Vector3 Vector { get; set; }
	}

	public class MyDeepClass
	{
		public string String { get; set; }
		public Transform Transform { get; set; }
		public Color Color { get; set; }
	}
}
