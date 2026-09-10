using Sandbox;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MenuProject.Settings;

public enum SettingKind
{
	Options,

	Dropdown,

	Slider,

	/// <summary>A button that acts immediately, not a stored value.</summary>
	Action
}

/// <summary>
/// One line in the settings list: everything needed to draw, search, explain and write it back.
/// This is what makes the pages data rather than markup.
/// </summary>
public class SettingItem
{
	public string Id { get; init; }
	public string Category { get; init; }
	public string Section { get; init; }
	public string Title { get; init; }

	/// <summary>Shown in the pane on the right. Say what it does and what it costs.</summary>
	public string Description { get; init; }

	public SettingKind Kind { get; init; } = SettingKind.Options;

	public List<Option> Options { get; init; }

	/// <summary>For lists only known when shown. Wins over <see cref="Options"/>.</summary>
	public Func<List<Option>> OptionsBuilder { get; init; }

	public float Min { get; init; }
	public float Max { get; init; } = 100;
	public float Step { get; init; } = 1;
	public string NumberFormat { get; init; } = "0.###";

	public Func<bool> IsVisible { get; init; }

	/// <summary>Greyed out and unwritable, with this as the reason.</summary>
	public Func<string> DisabledReason { get; init; }

	/// <summary>An amber note under the row: a caveat the current value carries.</summary>
	public Func<string> Warning { get; init; }

	public Func<object> Read { get; init; }
	public Action<object> Write { get; init; }

	public Action Invoke { get; init; }

	/// <summary>Action button label. The row already carries the name.</summary>
	public string ActionLabel { get; init; } = "Run";

	public bool AppliesRenderSettings { get; init; }

	public bool IsAction => Kind == SettingKind.Action;

	object staged;
	object loaded;

	/// <summary>The edited value, not in use until saved.</summary>
	public object Value
	{
		get => staged;
		set => staged = value;
	}

	public float FloatValue
	{
		get => ToFloat( staged );
		set => staged = value;
	}

	public bool IsDirty => !IsAction && !ValuesEqual( staged, loaded );

	public bool Visible => IsVisible?.Invoke() ?? true;

	public string Disabled => DisabledReason?.Invoke();

	public List<Option> CurrentOptions => OptionsBuilder is not null ? OptionsBuilder() : Options;

	// Backed by convars that may not be registered and devices that may not exist. A throwing
	// row is logged and left blank rather than taking the whole screen down.

	/// <summary>Throw the edit away and re-read what's in use.</summary>
	public void Revert()
	{
		if ( IsAction ) return;

		try
		{
			loaded = Read?.Invoke();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't read setting '{Id}': {e.Message}" );
			loaded = null;
		}

		staged = loaded;
	}

	/// <summary>Write the edit through.</summary>
	public void Commit()
	{
		if ( IsAction || !IsDirty ) return;

		try
		{
			Write?.Invoke( staged );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't write setting '{Id}': {e.Message}" );
			return;
		}

		loaded = staged;
	}

	/// <summary>
	/// ButtonGroup and DropDown hand values back as strings, so reads convert rather than cast.
	/// </summary>
	public T As<T>()
	{
		var value = staged;

		if ( value is null ) return default;
		if ( value is T typed ) return typed;

		var target = typeof( T );

		if ( target.IsEnum )
			return (T)Enum.Parse( target, value.ToString(), true );

		return (T)Convert.ChangeType( value, target, CultureInfo.InvariantCulture );
	}

	static float ToFloat( object value ) => value switch
	{
		null => 0f,
		float f => f,
		int i => i,
		_ => float.TryParse( value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed ) ? parsed : 0f
	};

	// "High" the string and High the enum are the same value here.
	static bool ValuesEqual( object a, object b )
	{
		if ( a is null || b is null ) return a is null && b is null;
		if ( a.Equals( b ) ) return true;

		return string.Equals( a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase );
	}

	string searchText;

	/// <summary>Everything a search looks at, lowercased once and kept.</summary>
	public string SearchText => searchText ??= $"{Title} {Description} {Section} {Category}".ToLowerInvariant();

	public bool Matches( string query ) => SearchText.Contains( query );
}
