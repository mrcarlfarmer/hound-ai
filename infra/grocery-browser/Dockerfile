# grocery-browser sidecar (spec 11) — Python + zendriver (undetected Chrome).
# Phase 1 scaffold: installs deps + Chromium/Xvfb and serves the stubbed FastAPI
# RPC surface. No real browser session is driven yet (Phase 3).
FROM python:3.12-slim AS runtime

ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PIP_NO_CACHE_DIR=1

# Chromium + Xvfb so zendriver can drive a headed (or headless-new) browser.
# Kept here so Phase 3 can wire up the real driver without changing the image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        chromium \
        xvfb \
        ca-certificates \
        fonts-liberation \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /srv

COPY infra/grocery-browser/requirements.txt .
RUN pip install -r requirements.txt

COPY infra/grocery-browser/app ./app

EXPOSE 8090

# Internal-only service on hound-net; no host ports published in compose.
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8090"]
