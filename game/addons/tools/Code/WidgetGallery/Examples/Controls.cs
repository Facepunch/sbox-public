using Sandbox.UI;
namespace Editor.Widgets.Gallery;

internal class ControlsTest : Widget
{
	TestClass instance;

	public ControlsTest( Widget parent ) : base( parent )
	{
		instance = new TestClass();
		var so = instance.GetSerialized();

		var ps = new ControlSheet();
		ps.AddObject( so );

		Layout = Layout.Column();
		Layout.Add( ps );

		Layout.AddStretchCell();
	}


	public class TestClass
	{
		public string StringValue { get; set; } = "Hello There";
		public uint UIntValue { get; set; } = 477;
		public int IntValue { get; set; } = -765;
		public ulong UlongValue { get; set; } = 546234623;
		public long LongValue { get; set; } = -46223512;
		public float FloatValue { get; set; } = 109.9f;
		[Range( 0, 10 )] public float FloatValueWithRange { get; set; } = 8.0f;
		public RangedFloat RangedFloat { get; set; } = 16.0f;
		public double DoubleValue { get; set; } = 78.9f;
		public Transform TransformValue { get; set; } = new Transform { Position = new Vector3( 600, 400, 20 ), Scale = 1.0f, Rotation = Rotation.From( 90, 0, 0 ) };
		public Vector2 Vector2Value { get; set; }
		public Material MaterialValue { get; set; }
		public Vector3 Vector3Value { get; set; }
		public Color ColorValue { get; set; } = "#f00";
		public Vector4 Vector4Value { get; set; }
		public Rotation RotationValue { get; set; }
		public Angles AnglesValue { get; set; }
		public Margin MarginValue { get; set; } = new Margin( 10, 10, 10, 20 );
		public Rect RectValue { get; set; } = new Rect( 20, 20, 200, 100 );
		public bool BoolValue { get; set; } = true;
		public TestEnum EnumValue { get; set; } = TestEnum.Ireland;
		public DecoratedEnum DecoratedEnumValue { get; set; } = DecoratedEnum.Pose;
		public TestFlags FlagsValue { get; set; } = TestFlags.Bottom | TestFlags.Top;
		public Model ModelValue { get; set; }
		public Curve CurveValue { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );
		public Gradient GradientValue { get; set; } = new Gradient( new Gradient.ColorFrame( 0.0f, Color.Cyan ), new Gradient.ColorFrame( 0.2f, Color.Red ), new Gradient.ColorFrame( 1.0f, Color.Yellow ) );
		public List<Vector3> ValueList { get; set; } = new List<Vector3>() { Vector3.Up, Vector3.Down };
		public Dictionary<string, float> DictionaryValues { get; set; } = new() { { "garry", 6.3f }, { "helk", 4.6f } };
		public Guid Guid { get; set; } = Guid.NewGuid();

		[ReadOnly] public float ReadOnlyFloatValue { get; set; } = 12.5f;
		[ReadOnly] public Vector3 ReadOnlyVectorValue { get; set; } = new Vector3( 0, 1, 2 );
		[ReadOnly] public Vector4 ReadOnlyVector4Value { get; set; } = new Vector4( 3, 2, 1, 0 );

		//	[TextArea]
		public string DictionaryJson
		{
			get => Json.Serialize( DictionaryValues );
			set { }
		}

		public Dictionary<TestEnum, Vector3> OtherDictionaryValues { get; set; } = new() { { TestEnum.Scotland, Vector3.Up }, { TestEnum.Wales, Vector3.Down } };
	}

	public enum DecoratedEnum
	{
		/// <summary>
		/// A car you can drive
		/// </summary>
		[Icon( "🚗" )]
		Vehicle,

		/// <summary>
		/// A robot that eats you
		/// </summary>
		[Icon( "🤖" )]
		Robot,

		/// <summary>
		/// A car that flies
		/// </summary>
		[Icon( "✈️" )]
		Plane,

		/// <summary>
		/// A standing person doing a weird pose or something
		/// </summary>
		[Icon( "🕺🏼" )]
		Pose
	}


	public enum TestEnum
	{
		NotApplicable,
		England,
		Ireland,
		Wales,
		Scotland,
		USA,
		Canada,
		Mexico,
		France,
		Germany,
		Italy,
		Spain,
		Portugal,
		Netherlands,
		Belgium,
		Switzerland,
		Austria,
		Poland,
		Sweden,
		Norway,
		Denmark,
		Finland,
		Greece,
		Turkey,
		Russia,
		Japan,
		China,
		India,
		Australia,
		NewZealand,
		Brazil,
		Argentina,
		Chile,
		SouthAfrica,
		Egypt
	}

	[Flags]
	public enum TestFlags
	{
		None = 0,
		Top = 1 << 0,
		Bottom = 1 << 1,
		Left = 1 << 2,
		Right = 1 << 3
	}

	/// <summary>
	/// Control Widgets are used to edit single values. With Controls we're editing via
	/// SerializedObject and SerializedProperty. In this way things are abstracted enough
	/// that we can provide things such as editing without an object, editing multiple objects
	/// at once, editing and applying the values in a deferred way.. and the controls don't
	/// have to worry about any of that.
	/// </summary>
	[WidgetGallery]
	[Title( "Control Widgets" )]
	[Icon( "portrait" )]
	[Order( -100 )]
	internal static Widget WidgetGallery() => new ControlsTest( null );
}
