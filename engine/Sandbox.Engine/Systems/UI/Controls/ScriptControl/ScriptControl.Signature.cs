using System;
using Sandbox.UI.Construct;

namespace Sandbox.UI;

public partial class ScriptControl
{
	IntelliSensePopup _signaturePopup;
	Label _signatureText;
	Label _signatureCounter;
	Script.SignatureHelp? _signature;
	int _selectedOverload;

	void CreateSignaturePopup()
	{
		_signaturePopup = CreatePopup( "signature-popup", ignoresInput: true );
		_signatureCounter = _signaturePopup.Add.Label( "", "signature-counter" );
		_signatureText = _signaturePopup.Add.Label( "", "signature-text" );
		_signatureText.IsRich = true;
	}

	internal bool SignatureVisible => _signature is { } help && help.Overloads.Count > 0;

	internal void CloseSignature()
	{
		_signature = null;
		var popup = _signaturePopup;
		_signaturePopup = null;
		popup?.Delete( true );
	}

	// Frame updates may refresh open help; typing or an explicit request opens it.
	internal void UpdateSignature( bool explicitRequest = false )
	{
		if ( !explicitRequest && !SignatureVisible )
		{
			return;
		}

		var next = Analysis.GetSignatureHelp( CaretOffset );
		if ( next?.Span != _signature?.Span )
		{
			_selectedOverload = 0;
		}
		_signature = next;
		RenderSignature();
	}

	internal void MoveOverload( int delta )
	{
		if ( !SignatureVisible )
		{
			return;
		}

		_selectedOverload += delta;
		RenderSignature();
	}

	void RenderSignature()
	{
		if ( !SignatureVisible )
		{
			CloseSignature();
			return;
		}

		if ( _signaturePopup is null ) CreateSignaturePopup();
		var help = _signature.Value;

		_selectedOverload = Math.Clamp( _selectedOverload, 0, help.Overloads.Count - 1 );
		var overload = help.Overloads[_selectedOverload];
		var text = EscapeHtml( overload.Text );
		var argument = overload.IsVariadic
			? Math.Min( help.Argument, overload.Parameters.Count - 1 )
			: help.Argument;

		if ( argument >= 0 && argument < overload.Parameters.Count )
		{
			var parameter = overload.Parameters[argument];
			var before = overload.Text[..parameter.Start];
			var current = overload.Text.Substring( parameter.Start, parameter.Length );
			var after = overload.Text[(parameter.Start + parameter.Length)..];
			text = $"{EscapeHtml( before )}<b style=\"color:{Theme.ActiveParameter.Rgba}\">{EscapeHtml( current )}</b>{EscapeHtml( after )}";
		}

		_signatureText.Text = text;
		_signatureCounter.Text = help.Overloads.Count > 1
			? $"{_selectedOverload + 1} of {help.Overloads.Count}  ·  Alt+↑↓"
			: "Parameter info";
		_signaturePopup.Style.Display = DisplayMode.Flex;
	}
}
