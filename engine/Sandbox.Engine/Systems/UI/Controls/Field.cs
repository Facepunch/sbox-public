using Microsoft.AspNetCore.Components;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Sandbox.Internal.GlobalGameNamespace;


namespace Sandbox.UI
{
	/// <summary>
	/// A field in a form, usually contains a label and a control
	/// </summary>
	[Library( "field" )]
	public class Field : Panel
	{
		public Field()
		{
			AddClass( "field" );
		}
	}

	/// <summary>
	/// A field in a form, usually contains a label and a control
	/// </summary>
	[Library( "control" )]
	public class FieldControl : Panel
	{
		public FieldControl()
		{
			AddClass( "field-control control" );
		}
	}
}
