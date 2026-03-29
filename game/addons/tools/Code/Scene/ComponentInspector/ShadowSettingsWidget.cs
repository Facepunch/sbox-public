namespace Editor;

[CustomEditor( typeof( DirectionalLight.CascadeVisualizer ) )]
public class ShadowCascadeVisualizerControlWidget : ControlWidget
{
	public override bool IncludeLabel => false;

	private static readonly Color[] CascadeColors =
	{
		new Color(0.6f, 0.5f, 0.5f, 1.0f),
		new Color(0.5f, 0.6f, 0.5f, 1.0f),
		new Color(0.5f, 0.5f, 0.6f, 1.0f),
		new Color(0.6f, 0.6f, 0.5f, 1.0f),
	};

	public ShadowCascadeVisualizerControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Column();

		Rebuild();

		var a = property.GetValue<DirectionalLight.CascadeVisualizer>();
		a.Update += Update;
	}

	static float[] CalculateSplitDistances( int numCascades, float near, float far, float lambda = 0.91f )
	{
		float[] splits = new float[numCascades];

		float subNear = 1.0f;
		float subRange = far - subNear;
		float subRatio = far / MathF.Max( subNear, 1.0f );

		for ( int i = 0; i < numCascades; i++ )
		{
			float p = (i + 1f) / numCascades;
			float logSplit = subNear * MathF.Pow( subRatio, p );
			float uniformSplit = subNear + subRange * p;
			float d = lambda * (logSplit - uniformSplit) + uniformSplit;
			splits[i] = Math.Clamp( d / far, 0.0f, 1.0f );
		}

		return splits;
	}

	protected override void OnPaint()
	{
		var cascadeCount = SerializedProperty.Parent.GetProperty( "ShadowCascadeCount" ).GetValue<int>();
		var splitRatio = SerializedProperty.Parent.GetProperty( "ShadowCascadeSplitRatio" ).GetValue<float>();
		var far = 15000.0f;

		var splits = CalculateSplitDistances( cascadeCount, 1.0f, far, splitRatio );

		var width = LocalRect.Width / cascadeCount;
		var height = LocalRect.Height;

		float x = 0;
		float prevSplit = 0;
		for ( int i = 0; i < splits.Length; i++ )
		{
			var split = splits[i];

			var w = (split - prevSplit) * LocalRect.Width;
			var rect = new Rect( x, 0, w, height );

			Paint.SetPen( Theme.Border );
			Paint.SetBrush( CascadeColors[i] );
			Paint.DrawRect( rect );

			Paint.ClearBrush();
			Paint.SetPen( Theme.Text );
			var cascadeNear = prevSplit * far;
			var cascadeFar = split * far;
			Paint.DrawText( rect, $"{i}\n{cascadeNear:F0}-{cascadeFar:F0}" );

			x += w;
			prevSplit = split;
		}
	}

	public void Rebuild()
	{
		Layout.Clear( true );
		Layout.Spacing = 2;

		Layout.Margin = 8;
		FixedHeight = 48;
	}

	protected override void OnValueChanged()
	{
		Rebuild();
	}
}
