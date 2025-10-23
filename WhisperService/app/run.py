from fastapi import FastAPI, UploadFile, File
import subprocess
import shutil
import os
from fastapi.responses import PlainTextResponse
import uvicorn
import requests

app = FastAPI()

WHISPER_CLI = "/usr/local/bin/whisper-cli"
MODEL_PATH = os.environ.get("MODEL_PATH", "/opt/whisper/models/ggml-base.en.bin")

def translate_text(text, source_lang="en", target_lang="nl"):
    try:
        response = requests.post(
            "http://127.0.0.1:5000/translate",
            headers={"Content-Type": "application/json"},
            json={"text": text, "from": source_lang, "to": target_lang}
        )
        response.raise_for_status()
        return response.json().get("translation", "")
    except Exception as e:
        print("Translation error:", e)
        return None


@app.post("/transcribe")
async def transcribe(file: UploadFile = File(...)):
    # Save uploaded file temporarily
    temp_path = f"/tmp/{file.filename}"
    with open(temp_path, "wb") as f:
        f.write(await file.read())

    # Run whisper-cli
        try:
            result = subprocess.run(
                [WHISPER_CLI, "--model", MODEL_PATH, "--no-timestamps", temp_path], # <--- MODIFIED
                capture_output=True, text=True, check=True
            )
            text = result.stdout
        except subprocess.CalledProcessError as e:
            # ---- MODIFY THESE LINES ----
            error_message = f"Whisper-CLI Error: {e.stderr}"
            print(error_message) # Log the detailed error to the container's console
            return PlainTextResponse(error_message, status_code=500) # Return it to the API service
            # ----------------------------
        finally:
            os.remove(temp_path)

    translated_text = translate_text(text)
    return {
        "original_text": text,
        "translated_text": translated_text
    }


@app.get("/health")
def health():
    return {"status": "ok"}

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=int(os.environ.get("PORT", 5001)))
