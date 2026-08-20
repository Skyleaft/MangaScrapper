import logging
import os
import sys
import time
from contextlib import asynccontextmanager

import torch
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field
from FlagEmbedding import BGEM3FlagModel

# Configure logging
LOG_LEVEL = os.getenv("LOG_LEVEL", "INFO").upper()
logging.basicConfig(
    level=LOG_LEVEL,
    format="%(asctime)s [%(levelname)s] [%(name)s] %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
    handlers=[logging.StreamHandler(sys.stdout)],
)
logger = logging.getLogger("embedding-service")

model: BGEM3FlagModel | None = None
MODEL_NAME = os.getenv("MODEL_NAME", "BAAI/bge-m3")
USE_FP16 = os.getenv("USE_FP16", "false").lower() in ("true", "1", "t")

@asynccontextmanager
async def lifespan(app: FastAPI):
    global model
    logger.info("Initializing Embedding Service...")
    logger.info(
        "Model: %s | fp16: %s | PyTorch version: %s | CUDA available: %s",
        MODEL_NAME,
        USE_FP16,
        torch.__version__,
        torch.cuda.is_available(),
    )

    start_time = time.perf_counter()
    try:
        model = BGEM3FlagModel(MODEL_NAME, use_fp16=USE_FP16)
        elapsed = time.perf_counter() - start_time
        logger.info("Model '%s' loaded successfully in %.2f seconds.", MODEL_NAME, elapsed)
    except Exception as e:
        logger.critical("Failed to load model '%s': %s", MODEL_NAME, str(e), exc_info=True)
        raise

    yield

    logger.info("Embedding Service is shutting down.")

app = FastAPI(title="Manga Embedding Service", lifespan=lifespan)

class EmbedRequest(BaseModel):
    text: str = Field(..., min_length=1, description="Text to encode")
    mode: str = Field(default="passage", description="Mode: 'passage' or 'query'")

class SparseVector(BaseModel):
    indices: list[int]
    values: list[float]

class EmbedResponse(BaseModel):
    dense: list[float]
    sparse: SparseVector

@app.middleware("http")
async def log_requests(request: Request, call_next):
    start_time = time.perf_counter()
    response = await call_next(request)
    duration_ms = (time.perf_counter() - start_time) * 1000.0
    logger.info(
        "%s %s - Status: %s - Duration: %.2fms",
        request.method,
        request.url.path,
        response.status_code,
        duration_ms,
    )
    return response

@app.get("/health")
def health():
    if model is None:
        logger.warning("Health check requested but model is not loaded.")
        return JSONResponse(
            status_code=503,
            content={"status": "unhealthy", "error": "Model not loaded"},
        )
    return {"status": "ok", "model": MODEL_NAME}

@app.post("/embed", response_model=EmbedResponse)
def embed(req: EmbedRequest):
    if model is None:
        logger.error("Embed requested while model is not initialized.")
        raise HTTPException(status_code=503, detail="Model is not ready")

    text_len = len(req.text)
    preview = (req.text[:60] + "...") if text_len > 60 else req.text
    logger.info(
        "Embedding request received | mode: %s | text_len: %d chars | preview: %r",
        req.mode,
        text_len,
        preview,
    )

    encode_start = time.perf_counter()
    try:
        output = model.encode(
            req.text,
            return_dense=True,
            return_sparse=True,
            return_colbert_vecs=False,
        )
        encode_time_ms = (time.perf_counter() - encode_start) * 1000.0

        dense_vec = output["dense_vecs"].tolist()
        lexical_dict = output["lexical_weights"]

        indices: list[int] = []
        values: list[float] = []
        for token_id, weight in lexical_dict.items():
            indices.append(int(token_id))
            values.append(float(weight))

        logger.info(
            "Embedding complete | mode: %s | encode_time: %.2fms | dense_dim: %d | sparse_tokens: %d",
            req.mode,
            encode_time_ms,
            len(dense_vec),
            len(indices),
        )

        return {
            "dense": dense_vec,
            "sparse": {
                "indices": indices,
                "values": values,
            },
        }
    except Exception as ex:
        logger.exception("Error during embedding generation for mode: %s: %s", req.mode, str(ex))
        raise HTTPException(status_code=500, detail=f"Embedding error: {str(ex)}")