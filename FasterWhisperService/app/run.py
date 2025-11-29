from fastapi import FastAPI, UploadFile, File, HTTPException
import os
import requests
import tempfile
from pathlib import Path
from faster_whisper import WhisperModel
import uvicorn
import logging

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI()

# Configuration
MODEL_NAME = os.environ.get("MODEL_NAME", "small.en")
DEVICE = os.environ.get("DEVICE", "cpu")
COMPUTE_TYPE = os.environ.get("COMPUTE_TYPE", "int8")
MODEL_DIR = os.environ.get("MODEL_DIR", "/opt/whisper/models")
TRANSLATION_SERVICE_URL = os.environ.get("TRANSLATION_SERVICE_URL", "http://translator-service:5000/translate")

# Initialize the model globally (load once, use many times)
logger.info(f"Loading faster-whisper model: {MODEL_NAME} on {DEVICE} with {COMPUTE_TYPE}")
model = WhisperModel(MODEL_NAME, device=DEVICE, compute_type=COMPUTE_TYPE, download_root=MODEL_DIR)
logger.info("Model loaded successfully")


@app.post("/transcribe")
async def transcribe(file: UploadFile = File(...)):
    """
    Transcribe audio file and return both original and translated text.
    """
    temp_file = None
    try:
        # Save uploaded file to temporary location
        content = await file.read()
        file_ext = Path(file.filename).suffix.lower()
        
        temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=file_ext)
        temp_file.write(content)
        temp_file.close()
        
        logger.info(f"Transcribing file: {file.filename}")
        
        # Transcribe using faster-whisper
        segments, info = model.transcribe(
            temp_file.name,
            beam_size=5,
            language="en",
            condition_on_previous_text=False,
            vad_filter=True,  # Enable VAD to filter silence
            vad_parameters=dict(min_silence_duration_ms=500)
        )
        
        # Convert generator to list to extract timing info
        segment_list = list(segments)
        
        # Join all segments into one text
        text = " ".join([segment.text.strip() for segment in segment_list])
        
        # Extract timing from first and last segments
        start_seconds = segment_list[0].start if segment_list else 0.0
        end_seconds = segment_list[-1].end if segment_list else 5.0
        
        logger.info(f"[TRANSCRIPTION] Original: {text} (start: {start_seconds}s, end: {end_seconds}s)")
        
        # Translate the text and get status
        translated_text, translation_status = translate_text(text)
        
        logger.info(f"[TRANSLATION] Translated: {translated_text} (Status: {translation_status})")
        
        return {
            "original_text": text,
            "translated_text": translated_text if translated_text else text,
            "translation_status": translation_status,
            "source_language": "en",
            "target_language": "nl",
            "start_seconds": start_seconds,
            "end_seconds": end_seconds
        }
    
    except Exception as e:
        logger.error(f"Transcription error: {str(e)}")
        raise HTTPException(status_code=500, detail=f"Transcription error: {str(e)}")
    
    finally:
        # Clean up temporary file
        if temp_file and os.path.exists(temp_file.name):
            try:
                os.unlink(temp_file.name)
            except Exception as e:
                logger.error(f"Failed to delete temp file: {e}")


def translate_text(text: str, source_lang: str = "en", target_lang: str = "nl"):
    """
    Translate text using the translation service.
    Returns a tuple: (translated_text, status)
    Status can be: "success", "failed", "service_unavailable"
    """
    if not text:
        return "", "failed"
    
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
        
        if translation and translation != text:
            return translation, "success"
        else:
            logger.warning(f"Translation service returned empty or same text")
            return text, "failed"
    
    except requests.exceptions.Timeout:
        logger.error(f"Translation service timeout")
        return text, "service_unavailable"
    except requests.exceptions.ConnectionError as e:
        logger.error(f"Translation service connection error: {e}")
        return text, "service_unavailable"
    except requests.exceptions.HTTPError as e:
        logger.error(f"Translation service HTTP error: {e}")
        return text, "service_unavailable"
    except Exception as e:
        logger.error(f"Translation service unexpected error: {e}")
        return text, "failed"


@app.get("/health")
def health():
    """
    Health check endpoint.
    """
    return {"status": "ok", "model": MODEL_NAME, "device": DEVICE}


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=5001)