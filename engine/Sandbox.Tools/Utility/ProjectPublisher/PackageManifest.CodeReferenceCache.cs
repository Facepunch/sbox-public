using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Project = Sandbox.Project;

namespace Editor;

public partial class ProjectPublisher
{
	public partial class PackageManifest
	{
		// A project's lifetime bounds the cache. Compare contents, not just timestamps, so edits with
		// unchanged length/time and changed resource registrations cannot reuse stale diagnostics.
		static readonly ConditionalWeakTable<Project, CodeReferenceCache> codeReferenceCaches = new();

		internal sealed class CodeReferenceCache
		{
			sealed record Entry( string Code, string Types, CodeAssetPath[] Paths );
			readonly Dictionary<string, Entry> entries = new( StringComparer.OrdinalIgnoreCase );

			public CodeAssetPath[] Analyze( string file, string code, string typeSignature, IReadOnlyDictionary<string, string[]> types, CancellationToken cancel = default )
			{
				cancel.ThrowIfCancellationRequested();
				lock ( entries )
				{
					if ( entries.TryGetValue( file, out var entry ) && entry.Code == code && entry.Types == typeSignature ) return entry.Paths;
					var paths = FindCodeAssetPaths( code, file, cancel, types ).ToArray();
					entries[file] = new( code, typeSignature, paths );
					return paths;
				}
			}
		}
	}
}
