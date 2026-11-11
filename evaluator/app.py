"""Evaluator sidecar: exposes DeepEval (quality metrics) and RAGAS (RAG metrics) over HTTP.

The .NET API calls this service when Llm__EvaluatorUrl is set, and falls back to its own
LLM-as-judge when the sidecar is unavailable. Requires OPENAI_API_KEY (DeepEval and RAGAS use an LLM judge).
"""
import os
from typing import List, Optional

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

app = FastAPI(title="LLM Quality Evaluator Sidecar", version="1.0.0")

JUDGE_MODEL = os.getenv("JUDGE_MODEL", "gpt-4o-mini")


class EvaluateRequest(BaseModel):
    input: str
    actual_output: str
    expected_output: str = ""
    context: List[str] = Field(default_factory=list)
    criteria: str = ""


class RagasRequest(BaseModel):
    question: str
    answer: str
    contexts: List[str]
    ground_truth: Optional[str] = ""


@app.get("/health")
def health():
    return {"status": "ok", "judge_model": JUDGE_MODEL, "has_key": bool(os.getenv("OPENAI_API_KEY"))}


@app.post("/evaluate")
def evaluate(req: EvaluateRequest):
    """DeepEval: relevance, faithfulness (only with context), completeness (GEval vs expected), clarity (GEval)."""
    if not os.getenv("OPENAI_API_KEY"):
        raise HTTPException(status_code=503, detail="OPENAI_API_KEY not configured")

    from deepeval.metrics import AnswerRelevancyMetric, FaithfulnessMetric, GEval
    from deepeval.test_case import LLMTestCase, LLMTestCaseParams

    ctx = req.context or ([req.expected_output] if req.expected_output else [])
    case = LLMTestCase(
        input=req.input,
        actual_output=req.actual_output,
        expected_output=req.expected_output or None,
        retrieval_context=ctx or None,
        context=ctx or None,
    )

    def run(metric) -> float:
        metric.measure(case)
        return float(metric.score or 0.0)

    relevance = AnswerRelevancyMetric(model=JUDGE_MODEL, threshold=0.5)
    completeness = GEval(
        name="Completeness",
        criteria="Does the actual output cover the important information of the expected output"
                 + (f" and satisfy: {req.criteria}" if req.criteria else "") + "?",
        evaluation_params=[LLMTestCaseParams.ACTUAL_OUTPUT, LLMTestCaseParams.EXPECTED_OUTPUT],
        model=JUDGE_MODEL,
    )
    clarity = GEval(
        name="Clarity",
        criteria="Is the actual output clear, well structured and easy to understand?",
        evaluation_params=[LLMTestCaseParams.ACTUAL_OUTPUT],
        model=JUDGE_MODEL,
    )

    result = {
        "relevance": run(relevance),
        "completeness": run(completeness),
        "clarity": run(clarity),
        "reason": getattr(relevance, "reason", "") or "",
    }
    result["faithfulness"] = run(FaithfulnessMetric(model=JUDGE_MODEL, threshold=0.5)) if ctx else result["relevance"]
    return result


@app.post("/ragas")
def ragas_eval(req: RagasRequest):
    """RAGAS: context_precision, answer_relevancy, faithfulness, context_recall."""
    if not os.getenv("OPENAI_API_KEY"):
        raise HTTPException(status_code=503, detail="OPENAI_API_KEY not configured")

    from datasets import Dataset
    from langchain_openai import ChatOpenAI, OpenAIEmbeddings
    from ragas import evaluate
    from ragas.embeddings import LangchainEmbeddingsWrapper
    from ragas.llms import LangchainLLMWrapper
    from ragas.metrics import answer_relevancy, context_precision, context_recall, faithfulness

    ds = Dataset.from_dict({
        "question": [req.question],
        "answer": [req.answer],
        "contexts": [req.contexts],
        "ground_truth": [req.ground_truth or ""],
    })
    llm = LangchainLLMWrapper(ChatOpenAI(model=JUDGE_MODEL))
    emb = LangchainEmbeddingsWrapper(OpenAIEmbeddings())
    result = evaluate(
        ds,
        metrics=[context_precision, answer_relevancy, faithfulness, context_recall],
        llm=llm,
        embeddings=emb,
    )
    row = result.to_pandas().iloc[0]

    def val(key: str) -> float:
        v = row.get(key, 0.0)
        return 0.0 if v != v else float(v)  # NaN -> 0

    return {
        "context_precision": val("context_precision"),
        "answer_relevancy": val("answer_relevancy"),
        "faithfulness": val("faithfulness"),
        "context_recall": val("context_recall"),
    }
