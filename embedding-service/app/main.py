from fastapi import FastAPI
from pydantic import BaseModel
from FlagEmbedding import BGEM3FlagModel

app = FastAPI()

# BAAI/bge-m3: Supports Dense (1024 dims) and Learned Lexical Sparse vectors
model = BGEM3FlagModel("BAAI/bge-m3", use_fp16=False)

class EmbedRequest(BaseModel):
    text: str
    mode: str = "passage"

class SparseVector(BaseModel):
    indices: list[int]
    values: list[float]

class EmbedResponse(BaseModel):
    dense: list[float]
    sparse: SparseVector

@app.get("/health")
def health():
    return {"status": "ok", "model": "BAAI/bge-m3"}

@app.post("/embed", response_model=EmbedResponse)
def embed(req: EmbedRequest):
    output = model.encode(
        req.text,
        return_dense=True,
        return_sparse=True,
        return_colbert_vecs=False
    )
    
    dense_vec = output["dense_vecs"].tolist()
    
    # lexical_weights: dict of {token_id (as str/int): weight (float)}
    lexical_dict = output["lexical_weights"]
    indices = []
    values = []
    for token_id, weight in lexical_dict.items():
        indices.append(int(token_id))
        values.append(float(weight))
        
    return {
        "dense": dense_vec,
        "sparse": {
            "indices": indices,
            "values": values
        }
    }