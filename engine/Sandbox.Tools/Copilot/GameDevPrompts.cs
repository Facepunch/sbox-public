using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Editor.Copilot;

/// <summary>
/// Pre-built game-dev prompts (slash commands) and context-reference resolvers
/// (`#file:`, `#selection`, `#scene`).
///
/// The chat widget calls <see cref="BuildPrompt"/> with the raw user input and
/// receives a fully expanded prompt + the cleaned-up display text.
/// </summary>
public static class GameDevPrompts
{
	// ── system prompt ─────────────────────────────────────────────────────────

	/// <summary>
	/// The master system prompt — shapes Copilot into a game-dev specialist
	/// who knows the s&box engine.
	/// </summary>
	public const string SystemPrompt = """
		You are an expert game-development AI assistant integrated into the s&box editor.
		s&box is a modern game engine built on .NET 10 / C# 12 with these characteristics:

		• Component-based scene system (Sandbox.Scene, Sandbox.GameObject, Sandbox.Component)
		• Renderer: Vulkan 1.3 with dynamic rendering, supports raytracing
		• UI: Razor + HTML/SCSS panels (Sandbox.UI)
		• Networking: built-in client-server with [Sync] / [Rpc] attributes
		• Physics: PhysX 5
		• Editor namespace contains all tooling APIs (Qt-based widgets)
		• Scripts hot-reload — write systems that survive Hotload events
		• Use `Log.Info/Warning/Error` (NOT Console.WriteLine)
		• Common types: Vector3, Rotation, Transform, Color, BBox, RealTimeSince, TimeSince
		• Async work uses `GameTask.Delay`, NOT `Task.Delay`, so it survives scene transitions

		You specialise in:
		  • Gameplay code (Components, Scenes, prefabs)
		  • Shaders (HLSL with Sandbox material system, .shader files)
		  • Procedural generation, AI, physics, animation
		  • Networking and replication
		  • UI / HUD with Razor panels
		  • Performance profiling and optimisation
		  • Asset pipelines (models, textures, sounds, particles)

		Style:
		  • Be concise — game devs want answers, not essays
		  • Always prefer code examples
		  • Use the Sandbox API correctly; if unsure, say so
		  • Wrap code in fenced blocks with language tags (```csharp, ```hlsl, ```razor)
		  • If the user asks something not related to game development, gently redirect

		If the user provides #file: or #selection context, treat it as authoritative
		ground-truth from their actual project — refer to it specifically in your answer.
		""";

	// ── slash commands ────────────────────────────────────────────────────────

	public class SlashCommand
	{
		public string Name        { get; init; }
		public string Description { get; init; }
		public string PromptTemplate { get; init; } // {0} = arguments / context
	}

	public static readonly List<SlashCommand> Commands = new()
	{
		new()
		{
			Name        = "/explain",
			Description = "Explain the selected code or referenced file",
			PromptTemplate =
				"Explain what the following s&box code does, step by step. " +
				"Highlight any sbox-specific patterns (components, scenes, networking, hotload). " +
				"If you spot bugs or anti-patterns, mention them.\n\n{0}"
		},
		new()
		{
			Name        = "/fix",
			Description = "Find and fix bugs in the referenced code",
			PromptTemplate =
				"Identify bugs in the following s&box code and provide a corrected version. " +
				"Explain each fix briefly.\n\n{0}"
		},
		new()
		{
			Name        = "/optimize",
			Description = "Suggest performance optimisations",
			PromptTemplate =
				"Review the following s&box code for performance. " +
				"Focus on per-frame allocations, GetComponent loops, async patterns, and " +
				"network-traffic reduction. Provide an optimised version.\n\n{0}"
		},
		new()
		{
			Name        = "/tests",
			Description = "Generate Sandbox.Test unit tests",
			PromptTemplate =
				"Write Sandbox.Test unit tests for the following code. " +
				"Use [TestMethod] / [TestClass] attributes and the Sandbox.Test framework.\n\n{0}"
		},
		new()
		{
			Name        = "/component",
			Description = "Generate a new Sandbox.Component",
			PromptTemplate =
				"Generate a new Sandbox.Component class that does the following: {0}.\n" +
				"Use [Property] for inspector-visible fields, override OnUpdate / OnStart / " +
				"OnEnabled as appropriate, and follow s&box conventions (PascalCase, partial " +
				"class if it's likely to be hot-reloaded)."
		},
		new()
		{
			Name        = "/shader",
			Description = "Generate or fix an HLSL .shader file",
			PromptTemplate =
				"Generate an s&box HLSL .shader file that does the following: {0}.\n" +
				"Use the Sandbox shader template (HEADER, MODES, COMMON, VS, PS), declare " +
				"texture/material parameters with `CreateTexture2D` and `CreateInputTexture2D` " +
				"helpers, and respect Vulkan 1.3 capabilities."
		},
		new()
		{
			Name        = "/network",
			Description = "Help with networking / replication",
			PromptTemplate =
				"Help me with this s&box networking task: {0}.\n" +
				"Use [Sync], [Rpc.Broadcast], [Rpc.Owner], and Network.OwnerConnection where " +
				"appropriate. Note client vs host authority."
		},
		new()
		{
			Name        = "/ui",
			Description = "Generate a Razor UI panel",
			PromptTemplate =
				"Generate an s&box Razor UI panel for: {0}.\n" +
				"Provide both the .razor file (with @using Sandbox; @using Sandbox.UI; " +
				"@inherits PanelComponent) and the matching .razor.scss styles. Use " +
				"flexbox-style layout."
		},
	};

	// ── parsing ───────────────────────────────────────────────────────────────

	private static readonly Regex FileRefRegex      = new( @"#file:([^\s]+)",            RegexOptions.Compiled );
	private static readonly Regex SelectionRefRegex = new( @"#selection\b",               RegexOptions.Compiled );
	private static readonly Regex SceneRefRegex     = new( @"#scene\b",                   RegexOptions.Compiled );
	private static readonly Regex SlashRegex        = new( @"^\s*(/[a-z]+)(?:\s+(.*))?$", RegexOptions.Singleline | RegexOptions.Compiled );

	public class ParsedPrompt
	{
		/// <summary>The fully-expanded prompt sent to the model.</summary>
		public string ExpandedPrompt { get; init; }

		/// <summary>The original user input (shown in the chat history).</summary>
		public string DisplayText    { get; init; }

		/// <summary>List of context items (for showing chips in the UI).</summary>
		public List<string> ContextRefs { get; init; } = new();
	}

	/// <summary>
	/// Expand slash commands and #context references in the user input.
	/// </summary>
	public static ParsedPrompt BuildPrompt( string rawInput )
	{
		if ( string.IsNullOrWhiteSpace( rawInput ) )
			return new ParsedPrompt { ExpandedPrompt = "", DisplayText = "" };

		var contextChips = new List<string>();
		var contextBlock = new StringBuilder();

		// 1. Resolve #file:path references
		var afterFiles = FileRefRegex.Replace( rawInput, m =>
		{
			var path = m.Groups[1].Value;
			contextChips.Add( $"📄 {path}" );

			var content = TryReadProjectFile( path );
			if ( content != null )
			{
				contextBlock.AppendLine();
				contextBlock.AppendLine( $"--- File: {path} ---" );
				contextBlock.AppendLine( "```" );
				contextBlock.AppendLine( content );
				contextBlock.AppendLine( "```" );
			}
			else
			{
				contextBlock.AppendLine();
				contextBlock.AppendLine( $"--- File: {path} (not found) ---" );
			}

			return $"`{path}`";
		} );

		// 2. Resolve #selection
		afterFiles = SelectionRefRegex.Replace( afterFiles, _ =>
		{
			var sel = TryGetSelectionContext();
			if ( !string.IsNullOrEmpty( sel ) )
			{
				contextChips.Add( "🎯 #selection" );
				contextBlock.AppendLine();
				contextBlock.AppendLine( "--- Current selection ---" );
				contextBlock.AppendLine( sel );
			}
			return "the current selection";
		} );

		// 3. Resolve #scene
		afterFiles = SceneRefRegex.Replace( afterFiles, _ =>
		{
			var scene = TryGetSceneContext();
			if ( !string.IsNullOrEmpty( scene ) )
			{
				contextChips.Add( "🎬 #scene" );
				contextBlock.AppendLine();
				contextBlock.AppendLine( "--- Current scene ---" );
				contextBlock.AppendLine( scene );
			}
			return "the current scene";
		} );

		// 4. Slash commands
		var slashMatch = SlashRegex.Match( afterFiles.Trim() );
		string expanded;

		if ( slashMatch.Success )
		{
			var commandName = slashMatch.Groups[1].Value.ToLowerInvariant();
			var args        = slashMatch.Groups[2].Success ? slashMatch.Groups[2].Value : "";
			var command     = Commands.Find( c => c.Name == commandName );

			if ( command != null )
			{
				var combined = string.IsNullOrEmpty( args ) ? contextBlock.ToString() : args + contextBlock;
				expanded     = string.Format( command.PromptTemplate, combined );
				contextChips.Insert( 0, $"⚡ {commandName}" );
			}
			else
			{
				expanded = afterFiles + contextBlock;
			}
		}
		else
		{
			expanded = afterFiles + contextBlock;
		}

		return new ParsedPrompt
		{
			ExpandedPrompt = expanded.Trim(),
			DisplayText    = rawInput.Trim(),
			ContextRefs    = contextChips
		};
	}

	// ── context resolvers ─────────────────────────────────────────────────────

	private static string TryReadProjectFile( string path )
	{
		try
		{
			// Try project-relative first via the editor FileSystem
			if ( Sandbox.FileSystem.Mounted != null )
			{
				if ( Sandbox.FileSystem.Mounted.FileExists( path ) )
					return Sandbox.FileSystem.Mounted.ReadAllText( path );
			}

			if ( Sandbox.FileSystem.Root.FileExists( path ) )
				return Sandbox.FileSystem.Root.ReadAllText( path );

			// Try absolute path
			if ( File.Exists( path ) )
				return File.ReadAllText( path );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Copilot: could not read #file:{path} — {ex.Message}" );
		}

		return null;
	}

	private static string TryGetSelectionContext()
	{
		try
		{
			var session = SceneEditorSession.Active;
			if ( session == null ) return null;

			var sb = new StringBuilder();
			foreach ( var obj in session.Selection )
			{
				if ( obj is Sandbox.GameObject go )
				{
					sb.AppendLine( $"GameObject: {go.Name}" );
					sb.AppendLine( $"  Position: {go.WorldPosition}" );
					sb.AppendLine( $"  Components:" );
					foreach ( var c in go.Components.GetAll() )
						sb.AppendLine( $"    - {c.GetType().Name}" );
				}
				else if ( obj is Sandbox.Component comp )
				{
					sb.AppendLine( $"Component: {comp.GetType().Name} on {comp.GameObject?.Name}" );
				}
			}

			return sb.Length > 0 ? sb.ToString() : null;
		}
		catch
		{
			return null;
		}
	}

	private static string TryGetSceneContext()
	{
		try
		{
			var session = SceneEditorSession.Active;
			var scene   = session?.Scene;
			if ( scene == null ) return null;

			var sb    = new StringBuilder();
			sb.AppendLine( $"Scene: {scene.Name}" );

			int count = 0;
			foreach ( var go in scene.GetAllObjects( false ) )
			{
				if ( count++ >= 50 ) { sb.AppendLine( "  …(truncated)" ); break; }
				sb.Append( "  " ).Append( go.Name );

				var components = go.Components.GetAll();
				if ( components != null )
				{
					var names = new List<string>();
					foreach ( var c in components ) names.Add( c.GetType().Name );
					if ( names.Count > 0 )
						sb.Append( "  [" ).Append( string.Join( ", ", names ) ).Append( ']' );
				}
				sb.AppendLine();
			}

			return sb.ToString();
		}
		catch
		{
			return null;
		}
	}
}
