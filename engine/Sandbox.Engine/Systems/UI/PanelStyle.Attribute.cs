using System.Collections.Generic;

namespace Sandbox.UI;

public sealed partial class PanelStyle
{
	static readonly Styles EmptyAttributeSnapshot = new();
	Styles _attributeBefore;
	Styles _attributeAfter;
	HashSet<string> _attributeWrites;
	HashSet<string> _attributeRawWrites;
	Dictionary<(string Property, int Occurrence), int> _attributeTransitionWrites;
	Styles _transitionScratch;
	bool _hasAttribute;
	bool _applyingAttribute;
	bool _copyingStyles;
	bool TrackAttributeWrites => _hasAttribute && !_applyingAttribute;
	const int TransitionDuration = 1, TransitionDelay = 2, TransitionTiming = 4, TransitionProperty = 8, TransitionAll = 15;

	internal override void OnPropertySet( string property )
	{
		if ( !TrackAttributeWrites ) return;
		(_attributeWrites ??= new()).Add( property );
		if ( !_copyingStyles && CssWide?.Remove( property ) == true ) Dirty();
	}

	internal override void OnRawPropertySet( string property )
	{
		if ( TrackAttributeWrites ) (_attributeRawWrites ??= new()).Add( property );
	}

	bool Written( string property ) => _attributeWrites?.Contains( property ) == true;

	void DetachAttribute()
	{
		_hasAttribute = false;
		_attributeWrites?.Clear();
		_attributeRawWrites?.Clear();
		_attributeTransitionWrites?.Clear();
	}

	void ReleaseAttributeSnapshots()
	{
		ClearSnapshot( _attributeBefore );
		ClearSnapshot( _attributeAfter );
		ClearSnapshot( _transitionScratch );
	}

	static void ClearSnapshot( Styles snapshot )
	{
		if ( snapshot == null ) return;
		snapshot.From( EmptyAttributeSnapshot );
		snapshot.RawValues.Clear();
	}

	internal void SetAttribute( string value )
	{
		if ( !_hasAttribute && string.IsNullOrWhiteSpace( value ) ) return;
		_applyingAttribute = true;
		try
		{
			if ( _hasAttribute )
			{
				if ( !Written( "background-gradient" ) && BackgroundGradient.Equals( _attributeAfter.BackgroundGradient ) )
					BackgroundGradient = ReferenceEquals( _backgroundImage, _attributeAfter._backgroundImage ) ? _attributeBefore.BackgroundGradient : default;
				RestoreMatchingValues( _attributeBefore, _attributeAfter, _attributeWrites );
				if ( !Written( "text-gradient" ) ) RestoreMatchingValue( ref TextGradient, _attributeBefore.TextGradient, _attributeAfter.TextGradient );
				if ( !Written( "box-shadow" ) ) RestoreShadows( BoxShadow, _attributeBefore.BoxShadow, _attributeAfter.BoxShadow );
				if ( !Written( "text-shadow" ) ) RestoreShadows( TextShadow, _attributeBefore.TextShadow, _attributeAfter.TextShadow );
				if ( !Written( "filter-drop-shadow" ) ) RestoreShadows( FilterDropShadow, _attributeBefore.FilterDropShadow, _attributeAfter.FilterDropShadow );
				RestoreTransitions( _attributeBefore, _attributeAfter );
				RestoreRawValues( _attributeBefore, _attributeAfter );
			}

			DetachAttribute();
			if ( string.IsNullOrWhiteSpace( value ) ) { ReleaseAttributeSnapshots(); return; }
			_attributeBefore ??= new Styles();
			CopyAttributeSnapshot( _attributeBefore, this );
			_hasAttribute = true;
			try { Set( value ); }
			finally
			{
				_attributeAfter ??= new Styles();
				CopyAttributeSnapshot( _attributeAfter, this );
			}
		}
		finally { _applyingAttribute = false; Dirty(); }
	}

	public override void From( BaseStyles source )
	{
		if ( !_applyingAttribute ) { DetachAttribute(); ReleaseAttributeSnapshots(); }
		if ( !ReferenceEquals( source, this ) )
		{
			base.From( source );
			RawValues.Clear();
			if ( source is Styles styles ) foreach ( var pair in styles.RawValues ) RawValues.Add( pair.Key, pair.Value );
		}
		Dirty();
	}

	public override void Add( BaseStyles source )
	{
		if ( TrackAttributeWrites )
		{
			_copyingStyles = true;
			try { TrackAddedProperties( source ); }
			finally { _copyingStyles = false; }
			if ( source is Styles styles )
			{
				if ( styles.BoxShadow.IsNone || styles.BoxShadow.Count > 0 ) OnPropertySet( "box-shadow" );
				if ( styles.TextShadow.IsNone || styles.TextShadow.Count > 0 ) OnPropertySet( "text-shadow" );
				if ( styles.FilterDropShadow.IsNone || styles.FilterDropShadow.Count > 0 ) OnPropertySet( "filter-drop-shadow" );
				if ( !styles.TextGradient.ColorOffsets.IsDefaultOrEmpty ) OnPropertySet( "text-gradient" );
				if ( styles._backgroundImage != null || !styles.BackgroundGradient.ColorOffsets.IsDefaultOrEmpty ) OnPropertySet( "background-gradient" );
				if ( styles.Transitions != null ) foreach ( var entry in styles.Transitions.List ) TrackTransition( entry.Property, 0, TransitionFields( entry ) );
			}
		}
		if ( !ReferenceEquals( source, this ) )
		{
			base.Add( source );
			if ( source is Styles styles ) foreach ( var pair in styles.RawValues ) { RawValues[pair.Key] = pair.Value; OnRawPropertySet( pair.Key ); }
		}
		Dirty();
	}

	public override void FromLerp( BaseStyles from, BaseStyles to, float delta )
	{
		TrackLerpedShadows( null, from, to );
		base.FromLerp( from, to, delta );
		Dirty();
	}

	public override void LerpProperty( string name, BaseStyles from, BaseStyles to, float delta )
	{
		if ( TrackAttributeWrites ) TrackLerpedPropertyGenerated( name, from, to );
		TrackLerpedShadows( name, from, to );
		base.LerpProperty( name, from, to, delta );
		Dirty();
	}

	void TrackLerpedShadows( string name, BaseStyles from, BaseStyles to )
	{
		if ( !TrackAttributeWrites || from is not Styles a || to is not Styles b ) return;
		if ( (name == null || name == "box-shadow") && a.BoxShadow != b.BoxShadow ) OnPropertySet( "box-shadow" );
		if ( (name == null || name == "text-shadow") && a.TextShadow != b.TextShadow ) OnPropertySet( "text-shadow" );
		if ( (name == null || name == "filter-drop-shadow") && a.FilterDropShadow != b.FilterDropShadow ) OnPropertySet( "filter-drop-shadow" );
	}

	internal override void OnStyleCloned( BaseStyles source )
	{
		var original = (PanelStyle)source;
		RawValues = new( original.RawValues, System.StringComparer.OrdinalIgnoreCase );
		BoxShadow = new();
		TextShadow = new();
		FilterDropShadow = new();
		CopyShadows( original );
		Transitions = original.Transitions == null ? null : new();
		if ( Transitions != null ) Transitions.List.AddRange( original.Transitions.List );
		_attributeWrites = original._attributeWrites == null ? null : new( original._attributeWrites );
		_attributeRawWrites = original._attributeRawWrites == null ? null : new( original._attributeRawWrites );
		_attributeTransitionWrites = original._attributeTransitionWrites == null ? null : new( original._attributeTransitionWrites );
		_transitionScratch = null;
		if ( original._attributeBefore != null ) { _attributeBefore = new(); CopyAttributeSnapshot( _attributeBefore, original._attributeBefore ); }
		if ( original._attributeAfter != null ) { _attributeAfter = new(); CopyAttributeSnapshot( _attributeAfter, original._attributeAfter ); }
	}

	internal override void OnStyleScaled()
	{
		if ( !TrackAttributeWrites ) return;
		// These are the fields scaled by Styles.ApplyScale, not every Length property.
		if ( _fontsize.HasValue ) OnPropertySet( "font-size" );
		if ( _textstrokewidth.HasValue ) OnPropertySet( "text-stroke-width" );
		if ( _lineheight.HasValue ) OnPropertySet( "line-height" );
		if ( _letterspacing.HasValue ) OnPropertySet( "letter-spacing" );
		if ( _width.HasValue ) OnPropertySet( "width" );
		if ( _minwidth.HasValue ) OnPropertySet( "min-width" );
		if ( _maxwidth.HasValue ) OnPropertySet( "max-width" );
		if ( _left.HasValue ) OnPropertySet( "left" );
		if ( _right.HasValue ) OnPropertySet( "right" );
		if ( _top.HasValue ) OnPropertySet( "top" );
		if ( _bottom.HasValue ) OnPropertySet( "bottom" );
		if ( _height.HasValue ) OnPropertySet( "height" );
		if ( _minheight.HasValue ) OnPropertySet( "min-height" );
		if ( _maxheight.HasValue ) OnPropertySet( "max-height" );
		if ( _flexbasis.HasValue ) OnPropertySet( "flex-basis" );
		if ( _rowgap.HasValue ) OnPropertySet( "row-gap" );
		if ( _columngap.HasValue ) OnPropertySet( "column-gap" );
		if ( _marginleft.HasValue ) OnPropertySet( "margin-left" );
		if ( _margintop.HasValue ) OnPropertySet( "margin-top" );
		if ( _marginright.HasValue ) OnPropertySet( "margin-right" );
		if ( _marginbottom.HasValue ) OnPropertySet( "margin-bottom" );
		if ( _paddingleft.HasValue ) OnPropertySet( "padding-left" );
		if ( _paddingtop.HasValue ) OnPropertySet( "padding-top" );
		if ( _paddingright.HasValue ) OnPropertySet( "padding-right" );
		if ( _paddingbottom.HasValue ) OnPropertySet( "padding-bottom" );
		if ( _borderleftwidth.HasValue ) OnPropertySet( "border-left-width" );
		if ( _bordertopwidth.HasValue ) OnPropertySet( "border-top-width" );
		if ( _borderrightwidth.HasValue ) OnPropertySet( "border-right-width" );
		if ( _borderbottomwidth.HasValue ) OnPropertySet( "border-bottom-width" );
		if ( _bordertopleftradius.HasValue ) OnPropertySet( "border-top-left-radius" );
		if ( _bordertoprightradius.HasValue ) OnPropertySet( "border-top-right-radius" );
		if ( _borderbottomrightradius.HasValue ) OnPropertySet( "border-bottom-right-radius" );
		if ( _borderbottomleftradius.HasValue ) OnPropertySet( "border-bottom-left-radius" );
		if ( _bordertopleftradiusv.HasValue ) OnPropertySet( "border-top-left-radius-v" );
		if ( _bordertoprightradiusv.HasValue ) OnPropertySet( "border-top-right-radius-v" );
		if ( _borderbottomrightradiusv.HasValue ) OnPropertySet( "border-bottom-right-radius-v" );
		if ( _borderbottomleftradiusv.HasValue ) OnPropertySet( "border-bottom-left-radius-v" );
		if ( _outlinewidth.HasValue ) OnPropertySet( "outline-width" );
		if ( _outlineoffset.HasValue ) OnPropertySet( "outline-offset" );
		if ( _scrollbarwidth.HasValue ) OnPropertySet( "scrollbar-width" );
		if ( _transformoriginx.HasValue ) OnPropertySet( "transform-origin-x" );
		if ( _transformoriginy.HasValue ) OnPropertySet( "transform-origin-y" );
		if ( _transform.HasValue ) OnPropertySet( "transform" );
		if ( _bordershape != null ) OnPropertySet( "border-shape" );
		if ( BoxShadow.Count > 0 ) OnPropertySet( "box-shadow" );
		if ( TextShadow.Count > 0 ) OnPropertySet( "text-shadow" );
		if ( FilterDropShadow.Count > 0 ) OnPropertySet( "filter-drop-shadow" );
		Dirty();
	}

	static int TransitionFields( TransitionDesc entry ) => TransitionProperty
		| (entry.Duration.HasValue ? TransitionDuration : 0) | (entry.Delay.HasValue ? TransitionDelay : 0)
		| (entry.TimingFunction != null ? TransitionTiming : 0);

	void TrackTransition( string property, int occurrence, int fields )
	{
		if ( string.IsNullOrEmpty( property ) ) return;
		_attributeTransitionWrites ??= new();
		var key = (property, occurrence);
		_attributeTransitionWrites.TryGetValue( key, out var previous );
		_attributeTransitionWrites[key] = previous | fields;
	}

	void TrackTransitionSet( string property, string value )
	{
		if ( !TrackAttributeWrites || !property.StartsWith( "transition", System.StringComparison.Ordinal ) ) return;
		_transitionScratch ??= new Styles();
		_transitionScratch.Transitions?.List.Clear();
		_transitionScratch.Set( property, value );
		if ( _transitionScratch.Transitions == null ) return;
		if ( property == "transition" )
		{
			foreach ( var entry in _transitionScratch.Transitions.List ) TrackTransition( entry.Property, 0, TransitionFields( entry ) );
		}
		else
		{
			var field = property switch
			{
				"transition-duration" => TransitionDuration,
				"transition-delay" => TransitionDelay,
				"transition-timing-function" => TransitionTiming,
				_ => TransitionProperty
			};
			for ( var i = 0; i < _transitionScratch.Transitions.List.Count && i < Transitions.List.Count; ++i )
				TrackTransition( Transitions.List[i].Property, TransitionOccurrence( Transitions, i ), field );
		}
	}

	static void CopyAttributeSnapshot( Styles destination, Styles source )
	{
		destination.From( source );
		// From merges entries by property; a snapshot must also preserve repeated "all" entries.
		if ( source.Transitions != null )
		{
			destination.Transitions ??= new();
			destination.Transitions.List.Clear();
			destination.Transitions.List.AddRange( source.Transitions.List );
		}
		destination.RawValues.Clear();
		foreach ( var pair in source.RawValues ) destination.RawValues.Add( pair.Key, pair.Value );
	}

	static void RestoreShadows( ShadowList current, ShadowList before, ShadowList after )
	{
		if ( current.IsNone != after.IsNone || current.Count != after.Count ) return;
		for ( var i = 0; i < current.Count; ++i )
		{
			var a = current[i];
			var b = after[i];
			if ( a.OffsetX != b.OffsetX || a.OffsetY != b.OffsetY || a.Blur != b.Blur
				|| a.Spread != b.Spread || a.Inset != b.Inset || !a.Color.Equals( b.Color ) ) return;
		}

		current.Clear();
		current.IsNone = before.IsNone;
		current.AddRange( before );
	}

	void RestoreTransitions( Styles before, Styles after )
	{
		if ( Transitions == null ) return;
		if ( (_attributeTransitionWrites?.Count ?? 0) == 0 && SameTransitions( Transitions, after.Transitions ) )
		{
			Transitions.List.Clear();
			if ( before.Transitions != null ) Transitions.List.AddRange( before.Transitions.List );
			return;
		}

		if ( after.Transitions != null )
		{
			// Work backwards so removing a duplicate doesn't change earlier occurrence numbers.
			for ( var appliedIndex = after.Transitions.List.Count - 1; appliedIndex >= 0; --appliedIndex )
			{
				var applied = after.Transitions.List[appliedIndex];
				var occurrence = TransitionOccurrence( after.Transitions, appliedIndex );
				var written = 0;
				_attributeTransitionWrites?.TryGetValue( (applied.Property, occurrence), out written );
				if ( written == TransitionAll ) continue;
				var index = FindTransition( Transitions, applied.Property, occurrence );
				if ( index < 0 ) continue;
				var current = Transitions.List[index];
				var beforeIndex = before.Transitions != null && appliedIndex < before.Transitions.List.Count
					&& before.Transitions.List[appliedIndex].Property == applied.Property ? appliedIndex : FindTransition( before.Transitions, applied.Property, occurrence );
				// A transition-property longhand can rename an entry rather than add one.
				if ( beforeIndex < 0 && before.Transitions != null && appliedIndex < before.Transitions.List.Count )
				{
					var previousProperty = before.Transitions.List[appliedIndex].Property;
					var previousOccurrence = TransitionOccurrence( before.Transitions, appliedIndex );
					if ( FindTransition( after.Transitions, previousProperty, previousOccurrence ) < 0
						&& FindTransition( Transitions, previousProperty, previousOccurrence ) < 0
						&& _attributeTransitionWrites?.ContainsKey( (previousProperty, previousOccurrence) ) != true ) beforeIndex = appliedIndex;
				}
				var previous = beforeIndex < 0 ? default : before.Transitions.List[beforeIndex];
				if ( written == 0 && beforeIndex < 0 && current.Duration == applied.Duration && current.Delay == applied.Delay
					&& current.TimingFunction == applied.TimingFunction )
				{
					Transitions.List.RemoveAt( index );
					continue;
				}

				if ( (written & TransitionDuration) == 0 ) RestoreMatchingValue( ref current.Duration, previous.Duration, applied.Duration );
				if ( (written & TransitionDelay) == 0 ) RestoreMatchingValue( ref current.Delay, previous.Delay, applied.Delay );
				if ( (written & TransitionTiming) == 0 ) RestoreMatchingValue( ref current.TimingFunction, previous.TimingFunction, applied.TimingFunction );
				if ( (written & TransitionProperty) == 0 ) current.Property = previous.Property ?? "all";
				Transitions.List[index] = current;
			}
		}
	}

	static bool SameTransitions( TransitionList a, TransitionList b )
	{
		if ( a.List.Count != (b?.List.Count ?? 0) ) return false;
		for ( var i = 0; i < a.List.Count; ++i )
		{
			var x = a.List[i];
			var y = b.List[i];
			if ( x.Property != y.Property || x.Duration != y.Duration || x.Delay != y.Delay || x.TimingFunction != y.TimingFunction ) return false;
		}
		return true;
	}

	static int TransitionOccurrence( TransitionList transitions, int index )
	{
		var count = 0;
		for ( var i = 0; i < index; ++i )
			if ( transitions.List[i].Property == transitions.List[index].Property ) ++count;
		return count;
	}

	static int FindTransition( TransitionList transitions, string property, int occurrence )
	{
		if ( transitions != null )
		{
			for ( var i = 0; i < transitions.List.Count; ++i )
				if ( transitions.List[i].Property == property && occurrence-- == 0 ) return i;
		}
		return -1;
	}

	void RestoreRawValues( Styles before, Styles after )
	{
		foreach ( var pair in after.RawValues )
		{
			if ( _attributeRawWrites?.Contains( pair.Key ) == true ) continue;
			if ( !RawValues.TryGetValue( pair.Key, out var current ) || !SameRawValue( current, pair.Value ) ) continue;
			if ( before.RawValues.TryGetValue( pair.Key, out var previous ) ) RawValues[pair.Key] = previous;
			else RawValues.Remove( pair.Key );
		}
	}

	static bool SameRawValue( IStyleBlock.StyleProperty a, IStyleBlock.StyleProperty b )
	{
		return a.Name == b.Name && a.Value == b.Value && a.OriginalValue == b.OriginalValue
			&& a.Line == b.Line && a.IsValid == b.IsValid;
	}
}
