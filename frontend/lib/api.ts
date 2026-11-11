export const API = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080";

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const isForm = init?.body instanceof FormData;
  const res = await fetch(`${API}/api${path}`, {
    ...init,
    headers: isForm ? init?.headers : { "Content-Type": "application/json", ...init?.headers },
    cache: "no-store",
  });
  if (!res.ok) {
    let msg = `${res.status} ${res.statusText}`;
    try {
      const j = await res.json();
      msg = j.error ?? j.title ?? msg;
    } catch {}
    throw new Error(msg);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

export const post = <T>(path: string, body?: unknown) =>
  api<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) });

export type TestCase = {
  id: string; name: string; category: string; prompt: string; expectedAnswer: string;
  acceptanceCriteria: string; securityType: string; createdAt: string;
};
export type Run = {
  id: string; testCaseId: string; testName: string; category: string; model: string; executedAt: string;
  latencyMs: number; promptTokens: number; completionTokens: number; costUsd: number;
  relevance: number | null; faithfulness: number | null; completeness: number | null; clarity: number | null; consistency: number | null;
  score: number; passed: boolean; failureType: string | null; judgeReason: string | null; evaluatorEngine: string;
  humanVerdict: boolean | null; response: string;
};
export type Dashboard = {
  kpis: { totalRuns: number; passRate: number; failRate: number; avgScore: number; avgCostUsd: number; avgLatencyMs: number };
  runsPerDay: { day: string; total: number; passed: number; failed: number }[];
  qualityTrend: { day: string; avgScore: number }[];
  byCategory: { name: string; count: number }[];
  failuresByType: { name: string; count: number }[];
};
export type QaMetrics = {
  hallucinationRate: number | null; promptSuccessRate: number | null; falsePositiveRate: number | null;
  falseNegativeRate: number | null; toxicityRate: number | null; groundednessScore: number | null;
  answerRelevancyScore: number | null; humanLabeledRuns: number;
};
export type LeaderboardRow = { model: string; runs: number; avgScore: number; avgLatencyMs: number; avgCostUsd: number; passRate: number };
export type SecurityResult = { id: string; testName: string; type: string; model: string; passed: boolean; toxic: boolean; failureType: string | null; judgeReason: string; response: string };
export type RagDoc = { id: string; fileName: string; uploadedAt: string; chunks: number };
export type RagAnswer = {
  runId: string; answer: string; contexts: string[]; latencyMs: number; costUsd: number;
  scores: { contextPrecision: number; answerRelevancy: number; faithfulness: number; contextRecall: number; engine: string };
};

export const pct = (v: number | null | undefined) => (v == null ? "n/a" : `${(v * 100).toFixed(1)}%`);
export const num = (v: number | null | undefined, d = 2) => (v == null ? "n/a" : v.toFixed(d));
