using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Sandbox;

namespace Sandbox.Engine.Emulation;

/// <summary>
/// P/Invoke declarations for YogaLayout functions.
/// These functions are loaded from libyogacore.so shared library
/// </summary>
internal static unsafe class YogaInterop
{
    private static IntPtr? _libraryHandle = null;

    static YogaInterop()
    {
        // Use SetDllImportResolver to intercept DllImport calls and load the library manually
        NativeLibrary.SetDllImportResolver(typeof(YogaInterop).Assembly, DllImportResolver);
    }

    private static IntPtr DllImportResolver(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        Console.WriteLine($"[YogaInterop] DllImportResolver called for: {libraryName}");
        
        // Handle both "libyogacore" and "yogacore" (in case DllImport strips the "lib" prefix)
        if (libraryName == "libyogacore" || libraryName == "yogacore")
        {
            // If already loaded, return the handle
            if (_libraryHandle.HasValue)
            {
                Console.WriteLine($"[YogaInterop] Returning cached handle for {libraryName}");
                return _libraryHandle.Value;
            }

            // Try to find and load libyogacore.so
            string currentDir = Directory.GetCurrentDirectory();
            string? exeDir = null;
            var args = Environment.GetCommandLineArgs();
            if (args != null && args.Length > 0 && !string.IsNullOrEmpty(args[0]))
            {
                exeDir = Path.GetDirectoryName(args[0]);
            }
            if (string.IsNullOrEmpty(exeDir))
                exeDir = currentDir;

            List<string> possiblePaths = new()
            {
                Path.Combine(currentDir, "bin", "linuxsteamrt64", "libyogacore.so"),
                Path.Combine(currentDir, "libyogacore.so"),
                Path.Combine(exeDir, "bin", "linuxsteamrt64", "libyogacore.so"),
                Path.Combine(exeDir, "libyogacore.so"),
                Path.Combine(exeDir, "..", "bin", "linuxsteamrt64", "libyogacore.so"),
                "/usr/lib/libyogacore.so",
                "/usr/local/lib/libyogacore.so"
            };

            Console.WriteLine($"[YogaInterop] Searching for libyogacore.so in {possiblePaths.Count} locations...");
            foreach (var libPath in possiblePaths)
            {
                string fullPath = Path.GetFullPath(libPath);
                Console.WriteLine($"[YogaInterop] Checking: {fullPath} (exists: {File.Exists(fullPath)})");
                if (File.Exists(fullPath))
                {
                    try
                    {
                        IntPtr handle = NativeLibrary.Load(fullPath);
                        _libraryHandle = handle;
                        Console.WriteLine($"[YogaInterop] Successfully loaded libyogacore.so from: {fullPath}");
                        return handle;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[YogaInterop] Failed to load libyogacore.so from {fullPath}: {ex.Message}");
                    }
                }
            }

            string errorMsg = $"libyogacore.so not found. Searched in: {string.Join(", ", possiblePaths)}";
            Console.WriteLine($"[YogaInterop] ERROR: {errorMsg}");
            throw new DllNotFoundException(errorMsg);
        }

        // For other libraries, use default resolution
        Console.WriteLine($"[YogaInterop] Using default resolution for: {libraryName}");
        return IntPtr.Zero;
    }

    // Node creation and management
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeNew", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeNew();
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeNewWithConfig", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeNewWithConfig(IntPtr config);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeFree", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeFree(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeReset", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeReset(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeCalculateLayout", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeCalculateLayout(IntPtr node, float availableWidth, float availableHeight, long direction);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeGetHasNewLayout", CallingConvention = CallingConvention.Cdecl)]
    public static extern int YGNodeGetHasNewLayout(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeSetHasNewLayout", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeSetHasNewLayout(IntPtr node, int hasNewLayout);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeIsDirty", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeIsDirty(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeMarkDirty", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeMarkDirty(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeInsertChild", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeInsertChild(IntPtr owner, IntPtr child, int index);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeRemoveChild", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeRemoveChild(IntPtr owner, IntPtr child);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeRemoveAllChildren", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeRemoveAllChildren(IntPtr owner);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeGetChildCount", CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong YGNodeGetChildCount(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeGetParent", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeGetParent(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeSetConfig", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeSetConfig(IntPtr node, IntPtr config);
    
    // Layout getters
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeLayoutGetLeft", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeLayoutGetLeft(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeLayoutGetTop", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeLayoutGetTop(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeLayoutGetRight", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeLayoutGetRight(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeLayoutGetBottom", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeLayoutGetBottom(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeLayoutGetWidth", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeLayoutGetWidth(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeLayoutGetHeight", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeLayoutGetHeight(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeLayoutGetDirection", CallingConvention = CallingConvention.Cdecl)]
    public static extern long YGNodeLayoutGetDirection(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeLayoutGetHadOverflow", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeLayoutGetHadOverflow(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeLayoutGetMargin", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeLayoutGetMargin(IntPtr node, long edge);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeLayoutGetBorder", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeLayoutGetBorder(IntPtr node, long edge);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeLayoutGetPadding", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeLayoutGetPadding(IntPtr node, long edge);
    
    // Config functions
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGConfigNew", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGConfigNew();
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGConfigFree", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGConfigFree(IntPtr config);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGConfigSetUseWebDefaults", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGConfigSetUseWebDefaults(IntPtr config, int enabled);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGConfigSetPointScaleFactor", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGConfigSetPointScaleFactor(IntPtr config, float pixelsInPoint);

    [DllImport("libyogacore", EntryPoint = "globalYoga_YGConfigSetLogger", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGConfigSetLogger(IntPtr config, IntPtr logger);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeCopyStyle", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeCopyStyle(IntPtr dstNode, IntPtr srcNode);
    
    // Style setters/getters - Direction
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetDirection", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetDirection(IntPtr node, long direction);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetDirection", CallingConvention = CallingConvention.Cdecl)]
    public static extern long YGNodeStyleGetDirection(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetFlexDirection", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetFlexDirection(IntPtr node, long flexDirection);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetFlexDirection", CallingConvention = CallingConvention.Cdecl)]
    public static extern long YGNodeStyleGetFlexDirection(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetJustifyContent", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetJustifyContent(IntPtr node, long justifyContent);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetJustifyContent", CallingConvention = CallingConvention.Cdecl)]
    public static extern long YGNodeStyleGetJustifyContent(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetAlignContent", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetAlignContent(IntPtr node, long alignContent);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetAlignContent", CallingConvention = CallingConvention.Cdecl)]
    public static extern long YGNodeStyleGetAlignContent(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetAlignItems", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetAlignItems(IntPtr node, long alignItems);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetAlignItems", CallingConvention = CallingConvention.Cdecl)]
    public static extern long YGNodeStyleGetAlignItems(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetAlignSelf", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetAlignSelf(IntPtr node, long alignSelf);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetAlignSelf", CallingConvention = CallingConvention.Cdecl)]
    public static extern long YGNodeStyleGetAlignSelf(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetPositionType", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetPositionType(IntPtr node, long positionType);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetPositionType", CallingConvention = CallingConvention.Cdecl)]
    public static extern long YGNodeStyleGetPositionType(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetFlexWrap", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetFlexWrap(IntPtr node, long flexWrap);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetFlexWrap", CallingConvention = CallingConvention.Cdecl)]
    public static extern long YGNodeStyleGetFlexWrap(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetOverflow", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetOverflow(IntPtr node, long overflow);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetOverflow", CallingConvention = CallingConvention.Cdecl)]
    public static extern long YGNodeStyleGetOverflow(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetDisplay", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetDisplay(IntPtr node, long display);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetDisplay", CallingConvention = CallingConvention.Cdecl)]
    public static extern long YGNodeStyleGetDisplay(IntPtr node);
    
    // Style setters/getters - Flex
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetFlex", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetFlex(IntPtr node, float flex);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetFlex", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeStyleGetFlex(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetFlexGrow", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetFlexGrow(IntPtr node, float flexGrow);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetFlexGrow", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeStyleGetFlexGrow(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetFlexShrink", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetFlexShrink(IntPtr node, float flexShrink);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetFlexShrink", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeStyleGetFlexShrink(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetFlexBasis", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetFlexBasis(IntPtr node, float flexBasis);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetFlexBasisPercent", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetFlexBasisPercent(IntPtr node, float flexBasis);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetFlexBasisAuto", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetFlexBasisAuto(IntPtr node);
    
    // Note: YGNodeStyleGetFlexBasis returns YGValue struct - simplified for now
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetFlexBasis", CallingConvention = CallingConvention.Cdecl)]
    public static extern YGValue YGNodeStyleGetFlexBasis(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetPosition", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetPosition(IntPtr node, long edge, float position);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetPositionPercent", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetPositionPercent(IntPtr node, long edge, float position);
    
    // Note: YGNodeStyleGetPosition returns YGValue - simplified
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetPosition", CallingConvention = CallingConvention.Cdecl)]
    public static extern YGValue YGNodeStyleGetPosition(IntPtr node, long edge);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetMargin", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetMargin(IntPtr node, long edge, float margin);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetMarginPercent", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetMarginPercent(IntPtr node, long edge, float margin);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetMarginAuto", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetMarginAuto(IntPtr node, long edge);
    
    // Note: YGNodeStyleGetMargin returns YGValue - simplified
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetMargin", CallingConvention = CallingConvention.Cdecl)]
    public static extern YGValue YGNodeStyleGetMargin(IntPtr node, long edge);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetPadding", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetPadding(IntPtr node, long edge, float padding);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetPaddingPercent", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetPaddingPercent(IntPtr node, long edge, float padding);
    
    // Note: YGNodeStyleGetPadding returns YGValue - simplified
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetPadding", CallingConvention = CallingConvention.Cdecl)]
    public static extern YGValue YGNodeStyleGetPadding(IntPtr node, long edge);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetBorder", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetBorder(IntPtr node, long edge, float border);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetBorder", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeStyleGetBorder(IntPtr node, long edge);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetGap", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetGap(IntPtr node, long gutter, float gapLength);
    
    // Note: YGNodeStyleGetGap returns YGValue - simplified
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetGap", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeStyleGetGap(IntPtr node, long gutter);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetWidth", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetWidth(IntPtr node, float width);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetWidthPercent", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetWidthPercent(IntPtr node, float width);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetWidthAuto", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetWidthAuto(IntPtr node);
    
    // Note: YGNodeStyleGetWidth returns YGValue
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetWidth", CallingConvention = CallingConvention.Cdecl)]
    public static extern YGValue YGNodeStyleGetWidth(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetHeight", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetHeight(IntPtr node, float height);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetHeightPercent", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetHeightPercent(IntPtr node, float height);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetHeightAuto", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetHeightAuto(IntPtr node);
    
    // Note: YGNodeStyleGetHeight returns YGValue
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetHeight", CallingConvention = CallingConvention.Cdecl)]
    public static extern YGValue YGNodeStyleGetHeight(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetMinWidth", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetMinWidth(IntPtr node, float minWidth);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetMinWidthPercent", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetMinWidthPercent(IntPtr node, float minWidth);
    
    // Note: YGNodeStyleGetMinWidth returns YGValue
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetMinWidth", CallingConvention = CallingConvention.Cdecl)]
    public static extern YGValue YGNodeStyleGetMinWidth(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetMinHeight", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetMinHeight(IntPtr node, float minHeight);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetMinHeightPercent", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetMinHeightPercent(IntPtr node, float minHeight);
    
    // Note: YGNodeStyleGetMinHeight returns YGValue
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetMinHeight", CallingConvention = CallingConvention.Cdecl)]
    public static extern YGValue YGNodeStyleGetMinHeight(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetMaxWidth", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetMaxWidth(IntPtr node, float maxWidth);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetMaxWidthPercent", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetMaxWidthPercent(IntPtr node, float maxWidth);
    
    // Note: YGNodeStyleGetMaxWidth returns YGValue
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetMaxWidth", CallingConvention = CallingConvention.Cdecl)]
    public static extern YGValue YGNodeStyleGetMaxWidth(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetMaxHeight", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetMaxHeight(IntPtr node, float maxHeight);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetMaxHeightPercent", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetMaxHeightPercent(IntPtr node, float maxHeight);
    
    // Note: YGNodeStyleGetMaxHeight returns YGValue
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetMaxHeight", CallingConvention = CallingConvention.Cdecl)]
    public static extern YGValue YGNodeStyleGetMaxHeight(IntPtr node);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleSetAspectRatio", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeStyleSetAspectRatio(IntPtr node, float aspectRatio);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeStyleGetAspectRatio", CallingConvention = CallingConvention.Cdecl)]
    public static extern float YGNodeStyleGetAspectRatio(IntPtr node);
    
    // Note: YGNodeSetMeasureFunc takes a function pointer - requires special handling
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeSetMeasureFunc", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr YGNodeSetMeasureFunc(IntPtr node, IntPtr measureFunc);
    
    [DllImport("libyogacore", EntryPoint = "globalYoga_YGNodeHasMeasureFunc", CallingConvention = CallingConvention.Cdecl)]
    public static extern int YGNodeHasMeasureFunc(IntPtr node);
}

