// RichTextKit
// Copyright © 2019-2020 Topten Software. All Rights Reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may 
// not use this product except in compliance with the License. You may obtain 
// a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software 
// distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
// WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
// License for the specific language governing permissions and limitations 
// under the License.

using Sandbox;
using SkiaSharp;

namespace Topten.RichTextKit
{
	/// <summary>
	/// Describes a text effect to apply to the rendering of text
	/// </summary>
	public struct TextEffect
	{
		public SKColor Color { get; set; }
		public SKPoint Offset { get; set; }
		public float Width { get; set; }
		public SKPaintStyle PaintStyle { get; set; }
		public SKBlurStyle BlurStyle { get; set; }
		public float BlurSize { get; set; }
		public SKStrokeJoin StrokeJoin { get; set; }
		public float StrokeMiter { get; set; }

		public static TextEffect DropShadow( SKColor sKColor, float x, float y, float blurSize )
		{
			blurSize = blurSize.Clamp( 0, 512 );

			return new TextEffect
			{
				Color = sKColor,
				Offset = new SKPoint( x, y ),
				BlurStyle = SKBlurStyle.Normal,
				BlurSize = blurSize,
				Width = 0.0f,
				PaintStyle = SKPaintStyle.StrokeAndFill
			};
		}

		public static TextEffect Outline( SKColor sKColor, float size )
		{
			size = size.Clamp( 0, 512 );

			return new TextEffect
			{
				Color = sKColor,
				Offset = new SKPoint( 0, 0 ),
				Width = size,
				PaintStyle = SKPaintStyle.StrokeAndFill,
				StrokeMiter = 0.5f,
				StrokeJoin = SKStrokeJoin.Round
			};
		}
	}
}
