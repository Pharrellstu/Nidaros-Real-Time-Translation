from fastapi import FastAPI, UploadFile, File
import subprocess
import shutil
import os
from fastapi.responses import PlainTextResponse
import uvicorn

app = FastAPI()

WHISPER_CLI = "/usr/local/bin/whisper-cli"
MODEL_PATH = os.environ.get("MODEL_PATH", "/opt/whisper/models/ggml-base.en.bin")

@app.post("/transcribe")
async def transcribe(file: UploadFile = File(...)):
    # Save uploaded file temporarily
    temp_path = f"/tmp/{file.filename}"
    with open(temp_path, "wb") as f:
        f.write(await file.read())

    # Run whisper-cli
    try:
        result = subprocess.run(
            [WHISPER_CLI, "--model", MODEL_PATH, temp_path],
            capture_output=True, text=True, check=True
        )
        text = result.stdout
    except subprocess.CalledProcessError as e:
        # ---- ADD THESE TWO LINES ----
        print(f"Whisper-CLI Error: {e.stderr}") # This will print the real error
        return PlainTextResponse(e.stderr, status_code=500) # Return the real error
        # -----------------------------
    finally:
        os.remove(temp_path)

    return {"text": text}

@app.get("/health")
def health():
    return {"status": "ok"}

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=int(os.environ.get("PORT", 5001)))
