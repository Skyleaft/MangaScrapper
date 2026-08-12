from fastapi import FastAPI
from pydantic import BaseModel
from sentence_transformers import SentenceTransformer

app = FastAPI()

# multilingual-e5-base: 768 dims, 100+ languages incl. Indonesian
# Requires "passage: " prefix for documents, "query: " prefix for search queries
model = SentenceTransformer("intfloat/multilingual-e5-base")

class EmbedRequest(BaseModel):
    text: str
    # "passage" for indexing manga text, "query" for search queries
    mode: str = "passage"

class EmbedResponse(BaseModel):
    vector: list[float]

@app.get("/health")
def health():
    return {"status": "ok", "model": "intfloat/multilingual-e5-base"}

@app.post("/embed", response_model=EmbedResponse)
def embed(req: EmbedRequest):
    # e5 models require prefix for optimal retrieval quality
    prefix = "query: " if req.mode == "query" else "passage: "
    text = f"{prefix}{req.text}"
    vector = model.encode(text, normalize_embeddings=True).tolist()
    return {"vector": vector}