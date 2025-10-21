#include "pch.h"
#include <string>
#include <vector>
#include <ctranslate2/translator.h>  // Using single Translator instead of pool

#define DLL_EXPORT __declspec(dllexport)

extern "C"
{
    /**
     * Creates a CTranslate2 translator instance.
     * @param model_path: Path to the CTranslate2 model directory
     * @param num_threads: Number of threads to use for translation
     * @return: Pointer to the translator instance, or nullptr on failure
     */
    DLL_EXPORT void* CreateTranslator(const char* model_path, int num_threads)
    {
        try
        {
            // Create a single translator instance
            auto* translator = new ctranslate2::Translator(
                model_path,
                ctranslate2::Device::CPU,
                ctranslate2::ComputeType::DEFAULT
            );

            return translator;
        }
        catch (const std::exception& e)
        {
            // In production, log the error message: e.what()
            return nullptr;
        }
    }

    /**
     * Destroys the translator instance and frees memory.
     * @param handle: Pointer to the translator instance
     */
    DLL_EXPORT void DestroyTranslator(void* handle)
    {
        if (handle != nullptr)
        {
            auto* translator = static_cast<ctranslate2::Translator*>(handle);
            delete translator;
        }
    }

    /**
     * Translates text from source language to target language.
     * @param handle: Pointer to the translator instance
     * @param source_text: Text to translate (space-separated words)
     * @param target_lang_code: Target language code (e.g., "__en__" for English)
     * @return: Translated text as a C-string (must be freed with FreeString), or nullptr on failure
     */
    DLL_EXPORT char* Translate(void* handle, const char* source_text, const char* target_lang_code)
    {
        // Validate input parameters
        if (handle == nullptr || source_text == nullptr || target_lang_code == nullptr)
        {
            return nullptr;
        }

        auto* translator = static_cast<ctranslate2::Translator*>(handle);

        // Tokenize input text by splitting on spaces
        // NOTE: This is a simple demo. For production, use SentencePiece tokenization!
        std::vector<std::string> input_tokens;
        std::string text(source_text);
        std::string delimiter = " ";
        size_t position = 0;

        while ((position = text.find(delimiter)) != std::string::npos)
        {
            std::string token = text.substr(0, position);
            if (!token.empty())
            {
                input_tokens.push_back(token);
            }
            text.erase(0, position + delimiter.length());
        }

        // Add the last token
        if (!text.empty())
        {
            input_tokens.push_back(text);
        }

        // Prepare batch input (single sentence)
        std::vector<std::vector<std::string>> source_batch = { input_tokens };

        // Prepare target prefix (language code)
        std::vector<std::vector<std::string>> target_prefix_batch = {
            { std::string(target_lang_code) }
        };

        try
        {
            // Perform translation
            std::vector<ctranslate2::TranslationResult> results = translator->translate_batch(
                source_batch,
                target_prefix_batch
            );

            // Check if translation was successful
            if (results.empty() || results[0].hypotheses.empty())
            {
                return nullptr;
            }

            // Get the best translation hypothesis
            const auto& best_hypothesis = results[0].hypotheses[0];

            // Join tokens back into a single string
            std::string translated_text = "";
            for (size_t i = 0; i < best_hypothesis.size(); ++i)
            {
                translated_text += best_hypothesis[i];

                // Add space between tokens (except after the last one)
                if (i < best_hypothesis.size() - 1)
                {
                    translated_text += " ";
                }
            }

            // Allocate memory for the result string
            // IMPORTANT: C# must call FreeString() to avoid memory leaks
            char* result_string = new char[translated_text.length() + 1];
            strcpy_s(result_string, translated_text.length() + 1, translated_text.c_str());

            return result_string;
        }
        catch (const std::exception& e)
        {
            // In production, log the error message: e.what()
            return nullptr;
        }
    }

    /**
     * Frees memory allocated by the Translate function.
     * @param str_to_free: Pointer to the string that needs to be freed
     */
    DLL_EXPORT void FreeString(char* str_to_free)
    {
        if (str_to_free != nullptr)
        {
            delete[] str_to_free;
        }
    }
}