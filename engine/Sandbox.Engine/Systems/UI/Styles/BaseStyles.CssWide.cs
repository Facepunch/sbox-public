using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Sandbox.UI;

/// <summary>
/// The CSS-wide keywords (https://www.w3.org/TR/css-cascade/#defaulting-keywords).
/// These are valid on any property and are resolved during the cascade rather than at parse time.
/// </summary>
internal enum CssWideKeyword
{
	/// <summary>
	/// Take the computed value of the property from the parent element.
	/// </summary>
	Inherit,

	/// <summary>
	/// Reset the property to its initial (specification default) value.
	/// </summary>
	Initial,

	/// <summary>
	/// Acts like <see cref="Inherit"/> for inherited properties and <see cref="Initial"/> otherwise.
	/// </summary>
	Unset,

	/// <summary>
	/// Rolls back to a previous cascade origin. We have no separate user-agent layer, so this
	/// is treated the same as <see cref="Unset"/>.
	/// </summary>
	Revert
}

public abstract partial class BaseStyles
{
	/// <summary>
	/// Sparse record of CSS-wide keywords (inherit/initial/unset/revert) applied per property.
	/// These can't be resolved at parse time because they depend on the parent's computed value
	/// and the property's initial value, so we stash them here and resolve in <see cref="ResolveCssWide"/>
	/// during the cascade. Stays null unless one of those keywords is actually used.
	/// </summary>
	internal Dictionary<string, CssWideKeyword> CssWide;

	/// <summary>
	/// Maps a CSS property name to the backing field that stores it. Cached because reflection
	/// lookups aren't free, and built lazily since CSS-wide keywords are rare.
	/// </summary>
	static readonly ConcurrentDictionary<string, FieldInfo> StyleFields = new();

	/// <summary>
	/// CSS shorthands that fan out to several single-field longhands. A CSS-wide keyword on a shorthand
	/// is applied to each of these longhands. Shorthands NOT listed here either handle the keyword
	/// natively (flex - whose 'initial'/'none'/'auto' are its own keywords) or store their value
	/// somewhere other than a single field (border-image, the image shorthands, transition, the shadow
	/// lists), so a keyword on them falls through to their own parser or is left unsupported. A few
	/// listed shorthands are partial: 'background'/'mask' don't cover the image, 'filter' the drop-shadow.
	/// </summary>
	internal static readonly Dictionary<string, string[]> ShorthandExpansions = new()
	{
		["margin"] = new[] { "margin-left", "margin-top", "margin-right", "margin-bottom" },
		["padding"] = new[] { "padding-left", "padding-top", "padding-right", "padding-bottom" },
		["inset"] = new[] { "top", "right", "bottom", "left" },
		["gap"] = new[] { "row-gap", "column-gap" },
		["overflow"] = new[] { "overflow-x", "overflow-y" },
		["border-radius"] = new[] { "border-top-left-radius", "border-top-right-radius", "border-bottom-right-radius", "border-bottom-left-radius" },
		["border-width"] = new[] { "border-top-width", "border-right-width", "border-bottom-width", "border-left-width" },
		["border-color"] = new[] { "border-left-color", "border-top-color", "border-right-color", "border-bottom-color" },
		["border"] = new[] { "border-left-width", "border-top-width", "border-right-width", "border-bottom-width", "border-left-color", "border-top-color", "border-right-color", "border-bottom-color" },
		["border-left"] = new[] { "border-left-width", "border-left-color" },
		["border-right"] = new[] { "border-right-width", "border-right-color" },
		["border-top"] = new[] { "border-top-width", "border-top-color" },
		["border-bottom"] = new[] { "border-bottom-width", "border-bottom-color" },
		["outline"] = new[] { "outline-width", "outline-color" },
		["text-stroke"] = new[] { "text-stroke-width", "text-stroke-color" },
		["text-decoration"] = new[] { "text-decoration-line", "text-decoration-color", "text-decoration-thickness", "text-decoration-style" },
		["transform-origin"] = new[] { "transform-origin-x", "transform-origin-y" },
		["perspective-origin"] = new[] { "perspective-origin-x", "perspective-origin-y" },
		["background-size"] = new[] { "background-size-x", "background-size-y" },
		["background-position"] = new[] { "background-position-x", "background-position-y" },
		["mask-size"] = new[] { "mask-size-x", "mask-size-y" },
		["mask-position"] = new[] { "mask-position-x", "mask-position-y" },
		["background"] = new[] { "background-color", "background-position-x", "background-position-y", "background-size-x", "background-size-y", "background-repeat" },
		["mask"] = new[] { "mask-position-x", "mask-position-y", "mask-size-x", "mask-size-y", "mask-repeat", "mask-mode" },
		["animation"] = new[] { "animation-duration", "animation-delay", "animation-timing-function", "animation-iteration-count", "animation-direction", "animation-fill-mode", "animation-play-state", "animation-name" },
		["filter"] = new[] { "filter-blur", "filter-saturate", "filter-sepia", "filter-brightness", "filter-contrast", "filter-hue-rotate", "filter-invert", "filter-tint", "filter-border-width", "filter-border-color" },
		["backdrop-filter"] = new[] { "backdrop-filter-blur", "backdrop-filter-invert", "backdrop-filter-contrast", "backdrop-filter-brightness", "backdrop-filter-saturate", "backdrop-filter-sepia", "backdrop-filter-hue-rotate" },
	};

	/// <summary>
	/// Backing-field names (e.g. "_fontcolor") of the properties that inherit from their parent by
	/// default. Derived once from the generated <see cref="ApplyCascading"/> so it can never drift
	/// from the property table in BaseStyles.Generated.tt, which is the single source of truth.
	/// </summary>
	static readonly HashSet<string> InheritedFields = ComputeInheritedFields();

	/// <summary>
	/// A style set with every property populated with its initial (default) value, used as the
	/// copy source when resolving 'initial' (and 'unset' on non-inherited properties).
	/// </summary>
	static readonly Styles InitialValues = CreateInitialValues();

	/// <summary>
	/// Discovers which properties inherit by running the generated ApplyCascading against a fully
	/// populated parent and an empty child - the fields the child picks up are exactly the inherited
	/// ones. Avoids hand-maintaining a list that would silently drift from the property table.
	/// </summary>
	static HashSet<string> ComputeInheritedFields()
	{
		var parent = new Styles();
		var child = new Styles();

		var fields = typeof( BaseStyles ).GetFields( BindingFlags.Instance | BindingFlags.NonPublic );

		foreach ( var field in fields )
		{
			if ( !field.Name.StartsWith( "_" ) )
				continue;

			try
			{
				var underlying = Nullable.GetUnderlyingType( field.FieldType ) ?? field.FieldType;
				object value = underlying == typeof( string ) ? "_"
					: underlying.IsValueType ? Activator.CreateInstance( underlying )
					: null;

				if ( value != null )
					field.SetValue( parent, value );
			}
			catch
			{
				// Reference-typed fields with no default value aren't inherited anyway - ignore them.
			}
		}

		child.ApplyCascading( parent );

		var inherited = new HashSet<string>();
		foreach ( var field in fields )
		{
			if ( field.Name.StartsWith( "_" ) && field.GetValue( child ) != null )
				inherited.Add( field.Name );
		}

		return inherited;
	}

	/// <summary>
	/// Builds the <see cref="InitialValues"/> style set.
	/// </summary>
	static Styles CreateInitialValues()
	{
		var s = new Styles();
		s.FillDefaults();

		// FillDefaults only populates nullable members - it skips every string property. Supply the
		// string defaults explicitly so 'initial'/'unset' on them resolve to the real default rather
		// than null. (Mirrors the default column in BaseStyles.Generated.tt.)
		s.FontFamily = "Arial";
		s.Cursor = "auto";
		s.MixBlendMode = "default";
		s.BackgroundBlendMode = "normal";
		s.AnimationName = "none";
		s.AnimationDirection = "normal";
		s.AnimationFillMode = "none";
		s.AnimationPlayState = "running";
		s.AnimationTimingFunction = "ease";
		s.Content = "";
		s.SoundIn = "";
		s.SoundOut = "";

		return s;
	}

	/// <summary>
	/// Looks up the backing field for a CSS property name, or null if there isn't a single field
	/// for it (shorthands like 'margin' or unknown properties).
	/// </summary>
	internal static FieldInfo GetStyleField( string property )
	{
		return StyleFields.GetOrAdd( property, static p =>
			typeof( BaseStyles ).GetField( "_" + p.Replace( "-", "" ), BindingFlags.Instance | BindingFlags.NonPublic ) );
	}

	/// <summary>
	/// Returns true and the matching keyword if the value is one of the CSS-wide keywords.
	/// </summary>
	internal static bool IsCssWideKeyword( string value, out CssWideKeyword keyword )
	{
		keyword = default;
		if ( value == null )
			return false;

		var span = value.AsSpan().Trim();

		// All four keywords are 5-7 chars; reject anything else before doing any comparison. This keeps
		// the common (non-keyword) Set path allocation-free.
		if ( span.Length < 5 || span.Length > 7 )
			return false;

		if ( span.Equals( "inherit", StringComparison.OrdinalIgnoreCase ) ) { keyword = CssWideKeyword.Inherit; return true; }
		if ( span.Equals( "initial", StringComparison.OrdinalIgnoreCase ) ) { keyword = CssWideKeyword.Initial; return true; }
		if ( span.Equals( "unset", StringComparison.OrdinalIgnoreCase ) ) { keyword = CssWideKeyword.Unset; return true; }
		if ( span.Equals( "revert", StringComparison.OrdinalIgnoreCase ) ) { keyword = CssWideKeyword.Revert; return true; }

		return false;
	}

	/// <summary>
	/// Records that a property was set to a CSS-wide keyword. Shorthands are expanded to their
	/// longhands. Returns false (declaration ignored) for properties that don't map to a single
	/// backing field and aren't an expandable shorthand.
	/// </summary>
	internal bool MarkCssWide( string property, CssWideKeyword keyword )
	{
		// Shorthands have no single backing field; apply the keyword to each of their longhands.
		if ( ShorthandExpansions.TryGetValue( property, out var longhands ) )
		{
			foreach ( var longhand in longhands )
				MarkCssWide( longhand, keyword );

			return true;
		}

		var field = GetStyleField( property );
		if ( field == null )
			return false;

		// Drop any value already parsed for this declaration - the keyword wins and is resolved later.
		field.SetValue( this, null );

		CssWide ??= new Dictionary<string, CssWideKeyword>();
		CssWide[property] = keyword;

		Dirty();
		return true;
	}

	/// <summary>
	/// Removes any CSS-wide keyword recorded for a property (and, if it's a shorthand, for each of
	/// its longhands) because a normal value has now been set and supersedes the keyword.
	/// </summary>
	internal void ClearCssWide( string property )
	{
		if ( CssWide == null )
			return;

		CssWide.Remove( property );

		if ( ShorthandExpansions.TryGetValue( property, out var longhands ) )
		{
			foreach ( var longhand in longhands )
				CssWide.Remove( longhand );
		}
	}

	/// <summary>
	/// Resolves any recorded CSS-wide keywords against the parent's computed style and the initial
	/// values. Must run after the rules have been applied but before inheritance and FillDefaults.
	/// </summary>
	internal void ResolveCssWide( BaseStyles parent )
	{
		if ( CssWide == null || CssWide.Count == 0 )
			return;

		foreach ( var pair in CssWide )
		{
			var field = GetStyleField( pair.Key );
			if ( field == null )
				continue;

			BaseStyles source = pair.Value switch
			{
				CssWideKeyword.Initial => InitialValues,
				CssWideKeyword.Inherit => parent,
				// unset / revert: behave as inherit for inherited properties, as initial otherwise.
				_ => InheritedFields.Contains( field.Name ) ? parent : InitialValues,
			};

			// inherit/unset on the root (no parent) leaves the field null so FillDefaults supplies
			// the initial value - which is what CSS does for inherit with no parent.
			if ( source == null )
				continue;

			field.SetValue( this, field.GetValue( source ) );
		}
	}

	/// <summary>
	/// Merges another set's CSS-wide keywords into this one when cascading rules together, keeping
	/// last-declaration-wins ordering between keywords and normal values.
	/// </summary>
	void MergeCssWide( BaseStyles bs )
	{
		// A normal value declared by bs supersedes a keyword we recorded earlier for that property.
		if ( CssWide != null && CssWide.Count > 0 )
		{
			List<string> overridden = null;
			foreach ( var property in CssWide.Keys )
			{
				// bs re-declares it as a keyword too - handled below, keep ours for now.
				if ( bs.CssWide != null && bs.CssWide.ContainsKey( property ) )
					continue;

				var field = GetStyleField( property );
				if ( field != null && field.GetValue( bs ) != null )
					(overridden ??= new List<string>()).Add( property );
			}

			if ( overridden != null )
			{
				foreach ( var property in overridden )
					CssWide.Remove( property );
			}
		}

		// A keyword declared by bs supersedes anything we had for that property.
		if ( bs.CssWide != null )
		{
			foreach ( var pair in bs.CssWide )
			{
				CssWide ??= new Dictionary<string, CssWideKeyword>();
				CssWide[pair.Key] = pair.Value;
				GetStyleField( pair.Key )?.SetValue( this, null );
			}
		}
	}
}
