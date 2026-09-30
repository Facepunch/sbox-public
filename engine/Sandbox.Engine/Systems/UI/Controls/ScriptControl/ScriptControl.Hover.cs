using System;
using Sandbox.UI.Construct;

namespace Sandbox.UI;

public partial class ScriptControl
{
	const float HoverDelay = 0.45f;

	IntelliSensePopup _hoverPopup;
	int _hoverOffset = -1;
	Script.SourceSpan? _hoverSpan;
	RealTimeSince _sinceHover;
	// Remember an attempted lookup even when there is no documentation to display.
	bool _hoverQueried;

	internal void SetHoverOffset( int offset )
	{
		if ( offset == _hoverOffset )
		{
			return;
		}
		var span = HoverSpanAt( offset );
		if ( span is not null && span == _hoverSpan )
		{
			return;
		}

		_hoverOffset = offset;
		_hoverSpan = span;
		_sinceHover = 0;
		CloseHover( keepTarget: true );
	}

	internal void CloseHover( bool keepTarget = false )
	{
		if ( !keepTarget )
		{
			_hoverOffset = -1;
			_hoverSpan = null;
		}

		_hoverQueried = false;
		_hoverPopup?.Delete( true );
		_hoverPopup = null;
	}

	Script.SourceSpan? HoverSpanAt( int offset )
	{
		return offset < 0 ? null : Analysis.GetToken( offset )?.Span;
	}

	void UpdateHover()
	{
		if ( _hoverQueried || _hoverOffset < 0 || _hoverOffset > Source.Length )
		{
			return;
		}
		if ( _sinceHover < HoverDelay || CompletionVisible )
		{
			return;
		}

		ShowQuickInfo( _hoverOffset );
	}

	/// <summary>
	/// Display symbol documentation or a diagnostic at a UTF-16 source offset.
	/// </summary>
	public void ShowQuickInfo( int offset )
	{
		CloseHover( keepTarget: true );
		offset = Math.Clamp( offset, 0, Source.Length );
		_hoverOffset = offset;
		_hoverSpan = HoverSpanAt( offset );
		_hoverQueried = true;

		var errors = VisibleDiagnostics
			.Where( diagnostic => ContainsDiagnostic( diagnostic.Span, offset ) )
			.ToArray();
		var symbol = Analysis.GetSymbol( offset );
		if ( symbol is null && errors.Length == 0 )
		{
			return;
		}

		var detail = symbol?.Detail;
		var description = symbol?.Description;
		if ( symbol?.Kind == Script.SymbolKind.Type && symbol?.Type is { } symbolType )
		{
			var type = Game.TypeLibrary.GetType( symbolType );
			if ( type is not null )
			{
				var kind = type.IsInterface ? "interface"
					: type.IsEnum ? "enum"
					: type.IsValueType ? "struct"
					: "class";
				detail = $"{kind} {type.FullName}";

				if ( string.IsNullOrWhiteSpace( description ) )
				{
					description = type.Description;
				}
			}
		}

		_hoverPopup = CreatePopup( "hover-popup", ignoresInput: true );
		var title = _hoverPopup.Add.Label( "", "info-title" );
		var descriptionLabel = _hoverPopup.Add.Label( "", "info-description" );
		title.Text = detail ?? symbol?.Name
			?? (errors.Length > 0 ? errors[0].Severity.ToString() : "Symbol");
		descriptionLabel.Text = string.Join( "\n", new[] { ScriptDocumentation.PlainText( description ) }
			.Concat( errors.Select( diagnostic => $"{diagnostic.Code}: {diagnostic.Message}" ) )
			.Where( text => !string.IsNullOrWhiteSpace( text ) ) );
		descriptionLabel.Style.Display = string.IsNullOrWhiteSpace( descriptionLabel.Text )
			? DisplayMode.None
			: DisplayMode.Flex;
		_hoverPopup.Style.Display = DisplayMode.Flex;
	}
}
