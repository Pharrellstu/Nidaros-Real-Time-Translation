from fastapi import FastAPI, UploadFile, File, HTTPException
import subprocess
import os
from fastapi.responses import PlainTextResponse
import uvicorn
import requests
import tempfile
from pathlib import Path

app = FastAPI()
# Configuration
WHISPER_CLI = "/usr/local/bin/whisper-cli"
MODEL_PATH = os.environ.get("MODEL_PATH", "/opt/whisper/models/ggml-base.en.bin")
TRANSLATION_SERVICE_URL = os.environ.get("TRANSLATION_SERVICE_URL", "http://translator:5000/translate")

# listening to the request (incoming audio ) and creating the text and getting ready for running the translation
@app.post("/transcribe")
async def transcribe(file: UploadFile = File(...)):
    temp_file = None
    try:
        content = await file.read()
        file_ext = Path(file.filename).suffix.lower()

        temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=file_ext)
        temp_file.write(content)
        temp_file.close()

        # Run Whisper CLI
        result = subprocess.run(
            [WHISPER_CLI, "--model", MODEL_PATH, "--no-timestamps", temp_file.name],
            capture_output=True,
            text=True,
            check=True,
            timeout=300
        )
        text = result.stdout.strip()

        print(f"[TRANSCRIPTION] Original: {text}")

        # Translate the text
        translated_text = translate_text(text)

        print(f"[TRANSLATION] Translated: {translated_text}")

        return {
            "original_text": text,
            "translated_text": translated_text
        }

    except subprocess.CalledProcessError as e:
        error_message = f"Whisper-CLI Error: {e.stderr}"
        print(error_message)
        raise HTTPException(status_code=500, detail=error_message)

    except Exception as e:
        print(f"Unexpected error: {str(e)}")
        raise HTTPException(status_code=500, detail=f"Internal server error: {str(e)}")

    finally:
        if temp_file and os.path.exists(temp_file.name):
            try:
                os.unlink(temp_file.name)
            except Exception as e:
                print(f"Failed to delete temp file: {e}")

#getting the trancribed text and sending request to translator server for translation of the text from english to dutch

def translate_text(text: str, source_lang: str = "en", target_lang: str = "nl"):
    if not text:
        return ""

    try:
        response = requests.post(
            TRANSLATION_SERVICE_URL,
            headers={"Content-Type": "application/json"},
            json={"text": text, "from": source_lang, "to": target_lang},
            timeout=30
        )
        response.raise_for_status()

        data = response.json()

        # Attempt different field names depending on translator API
        translation = data.get("translated_text") or data.get("translation") or data.get("translatedText")

        return translation if translation else text

    except Exception as e:
        print(f"Translation service error: {e}")
        return text  # Return original if translation fails

# this functions helps us to see if running the server has failed or it has been successfull
@app.get("/health")
def health():
    return {"status": "ok"}


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=5001)
