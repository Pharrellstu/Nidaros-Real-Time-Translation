using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
// Assume NativeCTranslate2 and IMachineTranslationService are accessible here.

public class CTranslate2Service : IDisposable, IMachineTranslationService
{
    private IntPtr _translatorHandle;

    public CTranslate2Service(string modelPath, int numWorkers)
    {
        // Calls the C++ DLL function defined in the Core project via P/Invoke.
        _translatorHandle = NativeCTranslate2.CreateTranslator(modelPath, numWorkers);
        if (_translatorHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to initialize CTranslate2 translator. Check C++ DLL and model path.");
        }
    }

    public Task<string> TranslateAsync(string englishText, string targetLangCode, CancellationToken cancellationToken)
    {
        if (_translatorHandle == IntPtr.Zero)
        {
            return Task.FromResult(string.Empty);
        }

        // 1. Call C++ and get the raw memory pointer (char*)
        IntPtr resultPtr = NativeCTranslate2.Translate(
            _translatorHandle,
            englishText,
            targetLangCode);

        if (resultPtr == IntPtr.Zero) return Task.FromResult(string.Empty);

        string persianText = string.Empty;
        try
        {
            // 2. Marshal (convert) the C-string (IntPtr) to a safe C# string (UTF8 assumed)
            persianText = Marshal.PtrToStringUTF8(resultPtr);
        }
        finally
        {
            // 3. CRUCIAL: Call the C++ DLL function to free the allocated memory.
            NativeCTranslate2.FreeString(resultPtr);
        }

        return Task.FromResult(persianText ?? string.Empty);
    }

    public void Dispose()
    {
        if (_translatorHandle != IntPtr.Zero)
        {
            // Call the C++ function to destroy the C++ Translator object.
            NativeCTranslate2.DestroyTranslator(_translatorHandle);
            _translatorHandle = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }
}