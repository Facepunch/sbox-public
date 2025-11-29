using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GenerateStubs
{
    class Program
    {
        static void Main(string[] args)
        {
            var repoRoot = "/home/hermann/Repositories/sbox-public";
            var definitionsDir = Path.Combine(repoRoot, "engine/Definitions");
            var outputDir = Path.Combine(repoRoot, "src/Sbox.Engine.Emulation/Generated");

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var manifestFile = Path.Combine(definitionsDir, "manifest.def");
            if (!File.Exists(manifestFile))
            {
                Console.WriteLine($"Manifest not found at {manifestFile}");
                return;
            }

            var defFiles = File.ReadAllLines(manifestFile)
                .Where(l => !string.IsNullOrWhiteSpace(l) && l.Trim().EndsWith(".def"))
                .Select(l => Path.Combine(definitionsDir, l.Trim()))
                .ToList();

            // Also include engine.def directly if not in manifest (it usually is)
            var engineDef = Path.Combine(definitionsDir, "engine.def");
            if (!defFiles.Contains(engineDef) && File.Exists(engineDef))
            {
                defFiles.Add(engineDef);
            }

            foreach (var file in defFiles)
            {
                if (File.Exists(file))
                {
                    ProcessDefFile(file, outputDir);
                }
                else
                {
                    Console.WriteLine($"Warning: Definition file not found: {file}");
                }
            }
        }

        static void ProcessDefFile(string file, string outputDir)
        {
            Console.WriteLine($"Processing {Path.GetFileName(file)}...");
            var content = File.ReadAllText(file);
            var parser = new DefParser(content);
            var classes = parser.Parse();

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Runtime.InteropServices;");
            sb.AppendLine("using Silk.NET.Core.Native;");
            sb.AppendLine();
            sb.AppendLine("namespace Sbox.Engine.Emulation.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    public static unsafe class Exports");
            sb.AppendLine("    {");

            foreach (var cls in classes)
            {
                foreach (var func in cls.Functions)
                {
                    // Generate stub
                    // We need to mangle the name to match what InteropGen expects.
                    // InteropGen usually expects: Exports::MangledName
                    // The mangled name is usually ClassName_FunctionName or similar.
                    // Let's try to reconstruct the mangling logic or just use a simple one and hope InteropGen uses predictable names.
                    // Looking at NativeWriter.cs, it uses f.MangledName.
                    // In Definition.cs/Mangler.cs, it seems to be ClassName_FunctionName.
                    
                    var mangledName = $"{cls.Name}_{func.Name}";
                    // Handle overloads if necessary, but for now simple concatenation.
                    // Actually, let's look at interop.engine.cpp exports again to be sure.
                    // Example: Sandbox_AsyncGPUReadback_DispatchManagedReadTextureCallback
                    // It seems to be Namespace_ClassName_FunctionName.
                    
                    // We need to handle namespaces from the class.
                    // But for the "Null Engine", we are implementing the NATIVE side exports.
                    // Wait, interop.engine.cpp has:
                    // Imports::Sandbox_AsyncGPUReadback_... (Calls FROM managed TO native? No, these are calls FROM Native TO Managed)
                    // Exports::... (Calls FROM Managed TO Native)
                    
                    // We need to implement the EXPORTS.
                    // Example from interop.engine.cpp:
                    // void CnmtnGrpBldr_DeleteThis( void* self )
                    // const CAnimationGroupBuilder* CnmtnGrpBldr_Create()
                    // int CnmtnGrpBldr_AddAnimation( void* self )
                    
                    // The mangling seems specific. 
                    // "CnmtnGrpBldr" seems to be a shortened version of CAnimationGroupBuilder?
                    // Or maybe it is explicitly defined in the .def file?
                    
                    // Let's check the .def file content for "CAnimationGroupBuilder".
                    
                    WriteStub(sb, cls, func);
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            var outputPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(file) + ".Generated.cs");
            File.WriteAllText(outputPath, sb.ToString());
        }

        static void WriteStub(StringBuilder sb, ClassDef cls, FuncDef func)
        {
            // This is a simplification. Real mangling is complex.
            // We might need to read the mangled name from the .def file if it's there, 
            // or we might need to replicate the hashing/mangling logic of InteropGen.
            // However, for a "Null Engine" that just compiles, we might get away with generic stubs 
            // IF we can match the EntryPoint string.
            
            // CRITICAL: The EntryPoint string in [UnmanagedCallersOnly(EntryPoint = "...")] MUST match 
            // what the managed side expects.
            // The managed side (Interop.Engine.cs) gets function pointers via `igen_engine`.
            // It passes a list of function pointers.
            // `igen_engine` fills `nativeFunctions` array.
            // The ORDER in that array is determined by `InteropGen`.
            
            // So we don't export individual functions by name!
            // We export `igen_engine` (which we did in EngineEmulation.cs).
            // And `igen_engine` must fill the `void** nativeFunctions` array with pointers to our stubs.
            
            // So we need to:
            // 1. Generate static methods for each native function.
            // 2. Generate the `igen_engine` implementation that populates the array in the CORRECT ORDER.
            
            // To get the correct order, we MUST process the .def files exactly like InteropGen does.
            // Or... we can use the `InteropGen` tool itself to generate the C# stubs?
            // The user asked to "Modify InteropGen to generate NativeAOT stubs".
            // That might be safer than rewriting the logic.
            
            // Let's pause writing this script and consider modifying InteropGen.
            // InteropGen already parses everything and knows the order.
            // It has `NativeWriter.cs`. We can create `NativeAotWriter.cs`.
        }
    }

    class DefParser
    {
        private string _content;
        public DefParser(string content) { _content = content; }
        public List<ClassDef> Parse() { return new List<ClassDef>(); } // Placeholder
    }

    class ClassDef { public string Name; public List<FuncDef> Functions = new List<FuncDef>(); }
    class FuncDef { public string Name; }
}