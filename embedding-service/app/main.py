from fastapi import FastAPI
from pydantic import BaseModel
from sentence_transformers import SentenceTransformer

app = FastAPI()

# load model saat container start
model = SentenceTransformer("BAAI/bge-base-en")

class EmbedRequest(BaseModel):
    text: str

class EmbedResponse(BaseModel):
    vector: list[float]

@app.get("/health")
def health():
    return {"status": "ok"}

@app.post("/embed", response_model=EmbedResponse)
def embed(req: EmbedRequest):
    vector = model.encode(req.text, normalize_embeddings=True).tolist()
    return {"vector": vector}