# translate_server.py
"""
Argos Translate HTTP server (simple, local).
- Installs models on demand (if available in the package index).
- Exposes POST /translate with JSON { "text": "...", "from": "en", "to": "nl" }.
- Returns { "translation": "..." } on success.
"""

from flask import Flask, request, jsonify
import argostranslate.package
import argostranslate.translate
import logging
import os
from typing import Optional


# Configuration and logging credintials

logging.basicConfig(level=logging.WARNING)
logger = logging.getLogger(__name__)
logger.setLevel(logging.INFO)

APP_HOST = "0.0.0.0"
APP_PORT = 5000
DOWNLOAD_DIR = os.path.join(os.getcwd(), "argospm_downloads")  # downloading argospm module
os.makedirs(DOWNLOAD_DIR, exist_ok=True)

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s"
)
logger = logging.getLogger("argos_server")

app = Flask(__name__)


# Helper functions
# this function is for connecting to argos translator service to access list of available modules
def _refresh_package_index() -> None:
    """
    Refreshes Argos package index from upstream. This is safe to call
    occasionally to get new model listings.
    """
    try:
        logger.info("Updating Argos package index...")
        argostranslate.package.update_package_index()
    except Exception as e:
        logger.warning("Failed to update package index: %s", e)
#based on our prefrnce we look for a module that translates from_code language  to to_code language
def find_available_package(from_code: str, to_code: str) -> Optional[argostranslate.package.Package]:
    """
    Returns an available package object from the Argos package index matching
    the `from_code -> to_code` pair, or None if not found.
    """
    _refresh_package_index()
    try:
        available = argostranslate.package.get_available_packages()
        for pkg in available:
            if pkg.from_code == from_code and pkg.to_code == to_code:
                logger.info("Found package in index: %s -> %s", from_code, to_code)
                return pkg
    except Exception as e:
        logger.exception("Error while searching package index: %s", e)
    return None

#check to see if the specific module has already been installed or not
def is_model_installed(from_code: str, to_code: str) -> bool:
    """Returns True if the requested model is already installed locally."""
    try:
        installed = argostranslate.package.get_installed_packages()
        for inst in installed:
            if inst.from_code == from_code and inst.to_code == to_code:
                logger.info("Model already installed: %s -> %s", from_code, to_code)
                return True
    except Exception as e:
        logger.warning("Could not list installed packages: %s", e)
    return False

#if the module is not instaleed this function downloads the module and install it
def install_model(from_code: str, to_code: str) -> bool:
    """
    Download and install the requested model. Returns True on success.
    Downloads into DOWNLOAD_DIR and removes the downloaded file after install.
    """
    if is_model_installed(from_code, to_code):
        return True

    pkg = find_available_package(from_code, to_code)
    if not pkg:
        logger.info("No package available for %s -> %s", from_code, to_code)
        return False

    try:
        # download to our download folder
        logger.info("Downloading model %s -> %s ...", from_code, to_code)
        path = pkg.download()
        logger.info("Downloaded model to: %s", path)

        # install from file
        logger.info("Installing model from %s ...", path)
        argostranslate.package.install_from_path(path)
        logger.info("Installed model %s -> %s", from_code, to_code)

        # cleanup the downloaded archive to save disk
        try:
            os.remove(path)
            logger.info("Removed temporary file: %s", path)
        except Exception:
            logger.debug("Could not remove temp file: %s", path)

        return True
    except Exception as e:
        logger.exception("Failed to download or install model %s->%s: %s", from_code, to_code, e)
        return False


# Flask route
# Listenning to every request to the translotor server and preparing the answer as JSON string
@app.route("/translate", methods=["POST"])
def translate_endpoint():
    """
    POST /translate
    Request JSON: { "text": "...", "from": "en", "to": "nl" }
    Response JSON: { "translation": "..." } or { "error": "..." }
    """
    payload = request.get_json(silent=True) or {}
    text = payload.get("text", "")
    from_lang = payload.get("from", "en")
    to_lang = payload.get("to", "nl")

    if not text:
        return jsonify({"error": "no text provided"}), 400

    # Ensure the model is present (blocking install if necessary).
    if not is_model_installed(from_lang, to_lang):
        success = install_model(from_lang, to_lang)
        if not success:
            return jsonify({"error": f"model {from_lang}->{to_lang} not available"}), 404

    try:
        # Perform translation using argostranslate's API.
        translated_text = argostranslate.translate.translate(text, from_lang, to_lang)
        return jsonify({"translation": translated_text})
    except Exception as e:
        logger.exception("Translation failed for %s -> %s: %s", from_lang, to_lang, e)
        return jsonify({"error": "internal translation error", "details": str(e)}), 500


@app.route("/health", methods=["GET"])
def health_endpoint():
    """
    GET /health
    Health check endpoint for monitoring
    Returns JSON: { "status": "ok" }
    """
    return jsonify({"status": "ok", "service": "argos-translator"})

#python entry point

if __name__ == "__main__":
    logger.info("Starting Argos Translate server on %s:%d", APP_HOST, APP_PORT)
    app.run(host=APP_HOST, port=APP_PORT)
