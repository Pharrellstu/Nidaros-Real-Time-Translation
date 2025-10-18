using System.Threading;
using System.Threading.Tasks;

public interface IMachineTranslationService
{
    // High-level method for the rest of the application to use.
    Task<string> TranslateAsync(string englishText, string targetLangCode, CancellationToken cancellationToken);
}