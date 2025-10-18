using System.Runtime.InteropServices;

// This static class holds the external method declarations for the C++ DLL.
// We use a constant for the DLL name for easy reference.
public static class NativeCTranslate2
{
    // The name of the DLL we will create in the next step.
    private const string DllName = "NidarosRTT.NativeTranslator";

    // --- 1. Lifecycle and Memory Management ---

    // C#: public static extern IntPtr CreateTranslator(string modelPath, int numThreads);
    // C++: void* CreateTranslator(const char* model_path, int num_threads);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr CreateTranslator(string modelPath, int numThreads);

    // C#: public static extern void DestroyTranslator(IntPtr handle);
    // C++: void DestroyTranslator(void* handle);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void DestroyTranslator(IntPtr handle);

    // C#: public static extern void FreeString(IntPtr strPtr);
    // C++: void FreeString(char* str_to_free);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FreeString(IntPtr strPtr);


    // --- 2. Translation Function ---

    // C#: public static extern IntPtr Translate(IntPtr handle, string sourceText, string targetLangCode);
    // C++: char* Translate(void* handle, const char* source_text, const char* target_lang_code);
    // We expect the C++ side to return a UTF-8 encoded char* pointer (IntPtr).
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Translate(IntPtr translatorHandle, string sourceText, string targetLangCode);
}