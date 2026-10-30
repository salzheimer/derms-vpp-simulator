# Rva.Intelligence: Python AI sidecar (FastAPI). Build context is src/Rva.Intelligence:
#   docker build -t rva-intelligence src/Rva.Intelligence
FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

WORKDIR /app

# torch first, from the CPU-only index, at the version pinned in requirements.txt; the second install then finds it
# already satisfied instead of pulling the much larger CUDA build from PyPI.
COPY requirements.txt .
RUN pip install --index-url https://download.pytorch.org/whl/cpu "$(grep -E '^torch==' requirements.txt)" \
    && pip install -r requirements.txt

COPY main.py .

RUN useradd --system --uid 10001 rva
USER rva

EXPOSE 8000
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
