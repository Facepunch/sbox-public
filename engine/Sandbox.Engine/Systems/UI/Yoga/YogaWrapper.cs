
using System.Runtime.InteropServices;

namespace Sandbox.UI
{
	[SkipHotload]
	internal sealed class YogaWrapper
	{
		YGNodeRef _native;


		YGNodeRef Node
		{
			get
			{
				if ( !_native.IsValid )
					throw new Exception( "Tried to access destroyed node" );

				return _native;
			}
		}


		static YGConfigRef _config;

		static YogaWrapper()
		{
			_config = Yoga.YGConfigNew();

			// web defaults, it's what people are expecting
			Yoga.YGConfigSetUseWebDefaults( _config, true );

			// we want to do our own snapping - don't do it internally
			Yoga.YGConfigSetPointScaleFactor( _config, 0.0f );
		}

		private Panel _panel;
		private YogaWrapper Parent => _panel?.Parent?.YogaNode;

		public YogaWrapper( Panel panel )
		{
			_native = Yoga.YGNodeNewWithConfig( _config );
			_panel = panel;
		}

		~YogaWrapper()
		{
			MainThread.Queue( Dispose );
		}

		public void Dispose()
		{
			GC.SuppressFinalize( this );

			if ( _native.IsValid )
			{
				Yoga.YGNodeFree( _native );
				_native = default;
			}

			_panel = default;
			_measureFunc = default;
		}

		Rect _yogaRect;
		internal Margin Margin;
		internal Margin Padding;
		internal Margin Border;

		public bool HasNewLayout => Yoga.YGNodeGetHasNewLayout( Node );

		internal float LayoutX => Yoga.YGNodeLayoutGetLeft( Node );
		internal float LayoutY => Yoga.YGNodeLayoutGetTop( Node );
		internal float LayoutWidth => Yoga.YGNodeLayoutGetWidth( Node );
		internal float LayoutHeight => Yoga.YGNodeLayoutGetHeight( Node );
		internal Margin LayoutMargin => new Margin( Yoga.YGNodeLayoutGetMargin( Node, YGEdge.YGEdgeLeft ), Yoga.YGNodeLayoutGetMargin( Node, YGEdge.YGEdgeTop ), Yoga.YGNodeLayoutGetMargin( Node, YGEdge.YGEdgeRight ), Yoga.YGNodeLayoutGetMargin( Node, YGEdge.YGEdgeBottom ) );
		internal Margin LayoutPadding => new Margin( Yoga.YGNodeLayoutGetPadding( Node, YGEdge.YGEdgeLeft ), Yoga.YGNodeLayoutGetPadding( Node, YGEdge.YGEdgeTop ), Yoga.YGNodeLayoutGetPadding( Node, YGEdge.YGEdgeRight ), Yoga.YGNodeLayoutGetPadding( Node, YGEdge.YGEdgeBottom ) );
		internal Margin LayoutBorder => new Margin( Yoga.YGNodeLayoutGetBorder( Node, YGEdge.YGEdgeLeft ), Yoga.YGNodeLayoutGetBorder( Node, YGEdge.YGEdgeTop ), Yoga.YGNodeLayoutGetBorder( Node, YGEdge.YGEdgeRight ), Yoga.YGNodeLayoutGetBorder( Node, YGEdge.YGEdgeBottom ) );

		public Rect YogaRect
		{
			get
			{
				if ( !HasNewLayout )
					return _yogaRect;

				Yoga.YGNodeSetHasNewLayout( Node, false );

				_yogaRect = new Rect( LayoutX, LayoutY, LayoutWidth, LayoutHeight );

				Margin = LayoutMargin;
				Padding = LayoutPadding;
				Border = LayoutBorder;

				return _yogaRect;
			}
		}

		YGMeasureFunc.Delegate _measureFunc;

		internal bool IsMeasureDefined => _measureFunc is not null;

		internal void SetMeasureFunction( YGMeasureFunc.Delegate target )
		{
			_measureFunc = target;
			var fp = Marshal.GetFunctionPointerForDelegate( _measureFunc );
			Yoga.YGNodeSetMeasureFunc( Node, new YGMeasureFunc { _ptr = fp } );
		}

		int ChildCount => (int)Yoga.YGNodeGetChildCount( Node );

		internal void RemoveChild( YogaWrapper yogaNode ) => Yoga.YGNodeRemoveChild( Node, yogaNode.Node );

		internal void AddChild( YogaWrapper yogaNode ) => AddChild( ChildCount, yogaNode );

		internal void AddChild( int index, YogaWrapper yogaNode )
		{
			index = Math.Clamp( index, 0, ChildCount );
			Yoga.YGNodeInsertChild( Node, yogaNode.Node, index );
		}

		internal void CalculateLayout( float width = float.NaN, float height = float.NaN )
		{
			Yoga.YGNodeCalculateLayout( Node, width, height, YGDirection.YGDirectionLTR );
		}

		internal void MarkDirty()
		{
			if ( !IsMeasureDefined )
				return;

			Yoga.YGNodeMarkDirty( Node );
		}

		internal bool Initialized;

		Length? _width;
		public Length? Width
		{
			set
			{
				if ( Initialized && _width == value && !_width.Value.Unit.IsDynamic() ) return;
				_width = value;

				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, Yoga.YGNodeStyleSetWidthAuto, Yoga.YGNodeStyleSetWidth, Yoga.YGNodeStyleSetWidthPercent );
			}
		}

		Length? _height;
		public Length? Height
		{
			set
			{
				if ( Initialized && _height == value && !value.Value.Unit.IsDynamic() ) return;
				_height = value;

				value.SetYoga( Node, () => Parent?.LayoutHeight ?? 0, Yoga.YGNodeStyleSetHeightAuto, Yoga.YGNodeStyleSetHeight, Yoga.YGNodeStyleSetHeightPercent );
			}
		}

		Length? _maxwidth;
		public Length? MaxWidth
		{
			set
			{
				if ( Initialized && _maxwidth == value && !value.Value.Unit.IsDynamic() ) return;
				_maxwidth = value;

				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetMaxWidth, Yoga.YGNodeStyleSetMaxWidthPercent );
			}
		}

		Length? _maxheight;
		public Length? MaxHeight
		{
			set
			{
				if ( Initialized && _maxheight == value && !value.Value.Unit.IsDynamic() ) return;
				_maxheight = value;
				value.SetYoga( Node, () => Parent?.LayoutHeight ?? 0, default, Yoga.YGNodeStyleSetMaxHeight, Yoga.YGNodeStyleSetMaxHeightPercent );
			}
		}

		Length? _minwidth;
		public Length? MinWidth
		{
			set
			{
				if ( Initialized && _minwidth == value && !value.Value.Unit.IsDynamic() ) return;
				_minwidth = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetMinWidth, Yoga.YGNodeStyleSetMinWidthPercent );
			}
		}



		Length? _minheight;
		public Length? MinHeight
		{
			set
			{
				if ( Initialized && _minheight == value && !value.Value.Unit.IsDynamic() ) return;
				_minheight = value;
				value.SetYoga( Node, () => Parent?.LayoutHeight ?? 0, default, Yoga.YGNodeStyleSetMinHeight, Yoga.YGNodeStyleSetMinHeightPercent );
			}
		}

		DisplayMode? _display;
		public DisplayMode? Display
		{
			set
			{
				if ( Initialized && _display == value ) return;
				_display = value;
				Yoga.YGNodeStyleSetDisplay( Node, value ?? DisplayMode.Flex );
			}
		}

		Length? _left;
		public Length? Left
		{
			set
			{
				if ( Initialized && _left == value && !value.Value.Unit.IsDynamic() ) return;
				_left = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetPosition, Yoga.YGNodeStyleSetPositionPercent, YGEdge.YGEdgeLeft );
			}
		}

		Length? _right;
		public Length? Right
		{
			set
			{
				if ( Initialized && _right == value && !value.Value.Unit.IsDynamic() ) return;
				_right = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetPosition, Yoga.YGNodeStyleSetPositionPercent, YGEdge.YGEdgeRight );
			}
		}

		Length? _top;
		public Length? Top
		{
			set
			{
				if ( Initialized && _top == value && !value.Value.Unit.IsDynamic() ) return;
				_top = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetPosition, Yoga.YGNodeStyleSetPositionPercent, YGEdge.YGEdgeTop );
			}
		}

		Length? _bottom;
		public Length? Bottom
		{
			set
			{
				if ( Initialized && _bottom == value && !value.Value.Unit.IsDynamic() ) return;
				_bottom = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetPosition, Yoga.YGNodeStyleSetPositionPercent, YGEdge.YGEdgeBottom );
			}
		}

		Length? _marginleft;
		public Length? MarginLeft
		{
			set
			{
				if ( Initialized && _marginleft == value && !value.Value.Unit.IsDynamic() ) return;
				_marginleft = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, Yoga.YGNodeStyleSetMarginAuto, Yoga.YGNodeStyleSetMargin, Yoga.YGNodeStyleSetPositionPercent, YGEdge.YGEdgeLeft );
			}
		}

		Length? _marginright;
		public Length? MarginRight
		{
			set
			{
				if ( Initialized && _marginright == value && !value.Value.Unit.IsDynamic() ) return;
				_marginright = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, Yoga.YGNodeStyleSetMarginAuto, Yoga.YGNodeStyleSetMargin, Yoga.YGNodeStyleSetPositionPercent, YGEdge.YGEdgeRight );
			}
		}

		Length? _margintop;
		public Length? MarginTop
		{
			set
			{
				if ( Initialized && _margintop == value && !value.Value.Unit.IsDynamic() ) return;
				_margintop = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, Yoga.YGNodeStyleSetMarginAuto, Yoga.YGNodeStyleSetMargin, Yoga.YGNodeStyleSetPositionPercent, YGEdge.YGEdgeTop );
			}
		}

		Length? _marginbottom;
		public Length? MarginBottom
		{
			set
			{
				if ( Initialized && _marginbottom == value && !value.Value.Unit.IsDynamic() ) return;
				_marginbottom = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, Yoga.YGNodeStyleSetMarginAuto, Yoga.YGNodeStyleSetMargin, Yoga.YGNodeStyleSetPositionPercent, YGEdge.YGEdgeBottom );
			}
		}

		Length? _paddingleft;
		public Length? PaddingLeft
		{
			set
			{
				if ( Initialized && _paddingleft == value && !value.Value.Unit.IsDynamic() ) return;
				_paddingleft = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetPadding, Yoga.YGNodeStyleSetPaddingPercent, YGEdge.YGEdgeLeft );
			}
		}

		Length? _paddingright;
		public Length? PaddingRight
		{
			set
			{
				if ( Initialized && _paddingright == value && !value.Value.Unit.IsDynamic() ) return;
				_paddingright = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetPadding, Yoga.YGNodeStyleSetPaddingPercent, YGEdge.YGEdgeRight );
			}
		}

		Length? _paddingtop;
		public Length? PaddingTop
		{
			set
			{
				if ( Initialized && _paddingtop == value && !value.Value.Unit.IsDynamic() ) return;
				_paddingtop = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetPadding, Yoga.YGNodeStyleSetPaddingPercent, YGEdge.YGEdgeTop );
			}
		}

		Length? _paddingbottom;
		public Length? PaddingBottom
		{
			set
			{
				if ( Initialized && _paddingbottom == value && !value.Value.Unit.IsDynamic() ) return;
				_paddingbottom = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetPadding, Yoga.YGNodeStyleSetPaddingPercent, YGEdge.YGEdgeBottom );
			}
		}

		Length? _borderleft;
		public Length? BorderLeftWidth
		{
			set
			{
				if ( Initialized && _borderleft == value && !value.Value.Unit.IsDynamic() ) return;
				_borderleft = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetBorder, default, YGEdge.YGEdgeLeft );
			}
		}

		Length? _borderright;
		public Length? BorderRightWidth
		{
			set
			{
				if ( Initialized && _borderright == value && !value.Value.Unit.IsDynamic() ) return;
				_borderright = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetBorder, default, YGEdge.YGEdgeRight );
			}
		}

		Length? _bordertop;
		public Length? BorderTopWidth
		{
			set
			{
				if ( Initialized && _bordertop == value && !value.Value.Unit.IsDynamic() ) return;
				_bordertop = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetBorder, default, YGEdge.YGEdgeTop );
			}
		}

		Length? _borderbottom;
		public Length? BorderBottomWidth
		{
			set
			{
				if ( Initialized && _borderbottom == value && !value.Value.Unit.IsDynamic() ) return;
				_borderbottom = value;
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, default, Yoga.YGNodeStyleSetBorder, default, YGEdge.YGEdgeBottom );
			}
		}

		PositionMode? _positionType;
		public PositionMode? PositionType
		{
			set
			{
				if ( Initialized && _positionType == value ) return;
				_positionType = value;
				Yoga.YGNodeStyleSetPositionType( Node, _positionType ?? PositionMode.Static );
			}
		}

		float? _aspectRatio;
		public float? AspectRatio
		{
			set
			{
				if ( Initialized && _aspectRatio == value ) return;
				_aspectRatio = value;
				Yoga.YGNodeStyleSetAspectRatio( Node, value ?? float.NaN );
			}
		}

		float? _flexgrow;
		public float? FlexGrow
		{
			set
			{
				if ( Initialized && _flexgrow == value ) return;
				_flexgrow = value;
				Yoga.YGNodeStyleSetFlexGrow( Node, value ?? 0 );
			}
		}

		float? _flexshrink;
		public float? FlexShrink
		{
			set
			{
				if ( Initialized && _flexshrink == value ) return;
				_flexshrink = value;
				Yoga.YGNodeStyleSetFlexShrink( Node, value ?? 1 );
			}
		}

		Length? _flexbasis;
		public Length? FlexBasis
		{
			set
			{
				if ( Initialized && _flexbasis == value && !value.Value.Unit.IsDynamic() ) return;
				_flexbasis = value;
				// width/height should be dependant on direction!
				value.SetYoga( Node, () => Parent?.LayoutWidth ?? 0, Yoga.YGNodeStyleSetFlexBasisAuto, Yoga.YGNodeStyleSetFlexBasis, Yoga.YGNodeStyleSetFlexBasisPercent );
			}
		}

		Wrap? _flexWrap;
		public Wrap? Wrap
		{
			set
			{
				if ( Initialized && _flexWrap == value ) return;
				_flexWrap = value;
				Yoga.YGNodeStyleSetFlexWrap( Node, value ?? UI.Wrap.NoWrap );
			}
		}

		Align? _aligncontent;
		public Align? AlignContent
		{
			set
			{
				if ( Initialized && _aligncontent == value ) return;
				_aligncontent = value;
				Yoga.YGNodeStyleSetAlignContent( Node, value ?? Align.FlexStart );
			}
		}

		Align? _alignitems;
		public Align? AlignItems
		{
			set
			{
				if ( Initialized && _alignitems == value ) return;
				_alignitems = value;
				Yoga.YGNodeStyleSetAlignItems( Node, value ?? Align.Stretch );
			}
		}

		Align? _alignself;
		public Align? AlignSelf
		{
			set
			{
				if ( Initialized && _alignself == value ) return;
				_alignself = value;
				Yoga.YGNodeStyleSetAlignSelf( Node, value ?? Align.Auto );
			}
		}

		FlexDirection? _flexdirection;
		public FlexDirection? FlexDirection
		{
			set
			{
				if ( Initialized && _flexdirection == value ) return;
				_flexdirection = value;
				Yoga.YGNodeStyleSetFlexDirection( Node, value ?? UI.FlexDirection.Row );
			}
		}

		Justify? _justifycontent;
		public Justify? JustifyContent
		{
			set
			{
				if ( Initialized && _justifycontent == value ) return;
				_justifycontent = value;
				Yoga.YGNodeStyleSetJustifyContent( Node, value ?? Justify.FlexStart );
			}
		}

		OverflowMode? _overflow;
		public OverflowMode? Overflow
		{
			set
			{
				if ( Initialized && _overflow == value ) return;
				_overflow = value;
				// Clip and ClipWhole behave like Visible for layout purposes — they only affect rendering, not layout.
				var yogaOverflow = (value == OverflowMode.Clip || value == OverflowMode.ClipWhole) ? OverflowMode.Visible : (value ?? OverflowMode.Visible);
				Yoga.YGNodeStyleSetOverflow( Node, yogaOverflow );
			}
		}

		Length? _rowGap;
		public Length? RowGap
		{
			set
			{
				if ( Initialized && _rowGap == value && !value.Value.Unit.IsDynamic() ) return;
				_rowGap = value;

				Yoga.YGNodeStyleSetGap( Node, YGGutter.YGGutterRow, value?.GetPixels( 0 ) ?? float.NaN );
			}
		}

		Length? _columnGap;
		public Length? ColumnGap
		{
			set
			{
				if ( Initialized && _columnGap == value && !value.Value.Unit.IsDynamic() ) return;
				_columnGap = value;
				Yoga.YGNodeStyleSetGap( Node, YGGutter.YGGutterColumn, value?.GetPixels( 0 ) ?? float.NaN );
			}
		}
	}
}
