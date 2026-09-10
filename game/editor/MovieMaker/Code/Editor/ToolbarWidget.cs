using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Sandbox.UI;
using Sandbox.Utility;

namespace Editor.MovieMaker;

#nullable enable

public sealed class ToolBarWidget : Widget
{
	private readonly List<ToolBarGroup> _groups = new();

	public ToolBarWidget( MovieEditorPanel parent ) : base( parent )
	{
		Parent = parent;

		Layout = Layout.Row();
		Layout.Spacing = 4f;
		Layout.Margin = new Margin( 0f, 4f );

		VerticalSizeMode = SizeMode.CanShrink;
	}

	public ToolBarGroup AddGroup( bool permanent = false, bool alignRight = false )
	{
		var group = new ToolBarGroup( this ) { IsPermanent = permanent, AlignRight = alignRight };

		_groups.Add( group );

		UpdateLayout();

		return group;
	}

	internal void RemoveGroup( ToolBarGroup group )
	{
		_groups.Remove( group );

		UpdateLayout();
	}

	public void Reset()
	{
		var toRemove = _groups.Where( x => !x.IsPermanent ).ToArray();

		foreach ( var group in toRemove )
		{
			group.Destroy();
		}
	}

	private void UpdateLayout()
	{
		Layout.Clear( false );

		_groups.RemoveAll( x => !x.IsValid );

		foreach ( var group in _groups.Where( x => !x.AlignRight ) )
		{
			Layout.Add( group );
		}

		if ( !_groups.Any( x => x.AlignRight ) ) return;

		Layout.AddStretchCell();

		foreach ( var group in _groups.Where( x => x.AlignRight ) )
		{
			Layout.Add( group );
		}
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( Theme.TabBackground );
		Paint.DrawRect( LocalRect );
	}
}

public sealed record ToolBarItemDisplay(
	string Title,
	string Icon,
	string? Description = null,
	bool Background = true,
	string? ShortcutIdentifier = null,
	string? ShortcutKeyBind = null )
{
	public string? ShortcutDisplayKeys => !string.IsNullOrEmpty( ShortcutIdentifier ) && EditorShortcuts.GetDisplayKeys( ShortcutIdentifier ) is { } keys
		? keys : ShortcutKeyBind?.ToTitleCase();

	public string FormattedTitle => ShortcutDisplayKeys is { } keys
		? $"<h3>{Title} <small>({keys})</small></h3>"
		: $"<h3>{Title}</h3>";
}

public sealed class ToolBarGroup : Widget
{
	public bool IsPermanent { get; init; }
	public bool AlignRight { get; init; }

	public ToolBarGroup( ToolBarWidget parent )
		: base( parent )
	{
		HorizontalSizeMode = SizeMode.CanGrow;

		Layout = Layout.Row();
		Layout.Spacing = 2f;
		Layout.Margin = 4f;
	}

	public override void OnDestroyed()
	{
		if ( !IsValid ) return;

		if ( Parent is ToolBarWidget { IsValid: true } toolbar )
		{
			toolbar.RemoveGroup( this );
		}
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.SidebarBackground );
		Paint.DrawRect( LocalRect, 3f );
	}

	public Label AddLabel( string text )
	{
		var label = new Label( null )
		{
			Color = Color.White.Darken( 0.5f ),
			Margin = 4f,
			Alignment = TextFlag.Left,
			Text = text
		};

		Layout.Add( label );

		return label;
	}

	public IconButton AddAction( ToolBarItemDisplay display, Action action, Func<bool>? enabled = null )
	{
		var btn = new IconButton( display.Icon )
		{
			ToolTip = $"{display.FormattedTitle}{display.Description}",
			IconSize = 16
		};

		if ( !display.Background )
		{
			btn.Background = Color.Transparent;
			btn.BackgroundActive = Color.Transparent;
			btn.ForegroundActive = Theme.Primary;
		}

		btn.OnClick += action;

		if ( enabled != null )
		{
			btn.Bind( nameof( IconButton.Enabled ) )
				.ReadOnly()
				.From( enabled, (Action<bool>?)null );
		}

		Layout.Add( btn );

		return btn;
	}

	public IconButton AddToggle( ToolBarItemDisplay display, Func<bool> getState, Action<bool> setState )
	{
		var btn = new IconButton( display.Icon )
		{
			ToolTip = $"{display.FormattedTitle}{display.Description}",
			IconSize = 16,
			IsToggle = true
		};

		if ( !display.Background )
		{
			btn.Background = Color.Transparent;
			btn.BackgroundActive = Color.Transparent;
			btn.ForegroundActive = Theme.Primary;
		}

		btn.Bind( "IsActive" ).From( getState, setState );

		Layout.Add( btn );

		return btn;
	}

	public FunctionSelector<InterpolationMode> AddInterpolationSelector( Func<InterpolationMode> getValue, Action<InterpolationMode> setValue )
	{
		var selector = new FunctionSelector<InterpolationMode>( "Interpolation Mode", mode => t => mode.Apply( t ) );

		selector.Bind( "Value" ).From( getValue, setValue );

		Layout.Add( selector );

		return selector;
	}

	private static ImmutableArray<float> ExampleValues { get; } =
	[
		-0.5f,
		0f,
		0.67f,
		0.33f,
		1f,
		1.5f
	];

	private static float CubicInterpolationExample( float t )
	{
		const int margin = 1;

		var segments = ExampleValues.Length - 1;

		t *= segments - margin * 2;

		var index = (int)MathF.Floor( t ) + margin;

		var i1 = Math.Clamp( index, 0, segments );
		var i2 = Math.Clamp( index + 1, 0, segments );

		t -= i1 - margin;

		t = Math.Clamp( t, 0f, 1f );

		var v1 = ExampleValues[i1];
		var v2 = ExampleValues[i2];

		if ( t <= 0f ) return v1;
		if ( t >= 1f ) return v2;

		var i0 = Math.Clamp( index - 1, 0, segments );
		var i3 = Math.Clamp( index + 2, 0, segments );

		var v0 = ExampleValues[i0];
		var v3 = ExampleValues[i3];

		var t0 = (v2 - v0) * 0.5f;
		var t1 = (v3 - v1) * 0.5f;

		var c0 = v1 + t0 / 3f;
		var c1 = v2 - t1 / 3f;

		var a0 = MathX.Lerp( v1, c0, t );
		var a1 = MathX.Lerp( c0, c1, t );
		var a2 = MathX.Lerp( c1, v2, t );

		var b0 = MathX.Lerp( a0, a1, t );
		var b1 = MathX.Lerp( a1, a2, t );

		return MathX.Lerp( b0, b1, t );
	}

	public static Func<float, float>? GetInterpolationFunc( KeyframeInterpolation interpolation )
	{
		return interpolation switch
		{
			KeyframeInterpolation.Step => MathF.Floor,
			KeyframeInterpolation.Linear => Easing.Linear,
			KeyframeInterpolation.Quadratic => Easing.QuadraticInOut,
			KeyframeInterpolation.Cubic => CubicInterpolationExample,
			_ => null
		};
	}

	public FunctionSelector<KeyframeInterpolation> AddInterpolationSelector( Func<KeyframeInterpolation> getValue, Action<KeyframeInterpolation> setValue )
	{
		var selector = new FunctionSelector<KeyframeInterpolation>( "Keyframe Interpolation", GetInterpolationFunc );

		selector.Bind( "Value" ).From( getValue, setValue );

		Layout.Add( selector );

		return selector;
	}

	public (FloatSlider Slider, Label Label) AddSlider( string title, Func<float> getValue, Action<float> setValue, float minimum = 0f,
		float maximum = 1f, float step = 0.01f, Func<string>? getLabel = null )
	{
		var slider = new FloatSlider( null )
		{
			ToolTip = title,
			FixedWidth = 80f,
			Minimum = minimum,
			Maximum = maximum,
			Step = step
		};

		slider.Bind( nameof( FloatSlider.Value ) )
			.From( getValue, setValue );

		Layout.Add( slider );

		var label = new Label( null )
		{
			Color = Color.White.Darken( 0.5f ),
			Margin = 4f,
			Alignment = TextFlag.Left
		};

		label.Bind( nameof( Label.Text ) )
			.ReadOnly()
			.From( getLabel ?? (() => slider.Value.ToString( CultureInfo.InvariantCulture )), (Action<string>?)null );

		Layout.Add( label );

		return (slider, label);
	}

	public void AddSpacingCell() => Layout.AddSpacingCell( 8f );
}
