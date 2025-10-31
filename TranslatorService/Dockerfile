FROM python:3.10-slim

WORKDIR /app

COPY translate_server.py .

# Install system dependencies for Argos Translate
RUN apt-get update && apt-get install -y \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

# Install Argos Translate & dependencies
RUN pip install --no-cache-dir flask argostranslate

EXPOSE 5000

CMD ["python", "translate_server.py"]
