from fastapi import FastAPI
from pydantic import BaseModel
from sentence_transformers import SentenceTransformer

app = FastAPI()

# BAAI/bge-m3: 1024 dims, 8192 token context, state-of-the-art multilingual & cross-lingual retrieval
model = SentenceTransformer("BAAI/bge-m3")

class EmbedRequest(BaseModel):
    text: str
    mode: str = "passage"

class EmbedResponse(BaseModel):
    vector: list[float]

@app.get("/health")
def health():
    return {"status": "ok", "model": "BAAI/bge-m3"}

@app.post("/embed", response_model=EmbedResponse)
def embed(req: EmbedRequest):
    # bge-m3 supports direct encoding without prefixes
    vector = model.encode(req.text, normalize_embeddings=True).tolist()
    return {"vector": vector}