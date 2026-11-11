"use client";

import { useCallback, useEffect, useState } from "react";
import { api, num, post, type Run, type TestCase } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge, ErrorBox, Input, Label, Select, Textarea } from "@/components/ui/form";

const empty = { name: "", category: "", prompt: "", expectedAnswer: "", acceptanceCriteria: "" };

export default function TestsPage() {
  const [cases, setCases] = useState<TestCase[]>([]);
  const [runs, setRuns] = useState<Run[]>([]);
  const [models, setModels] = useState<string[]>([]);
  const [model, setModel] = useState("");
  const [form, setForm] = useState(empty);
  const [busy, setBusy] = useState<string | null>(null);
  const [err, setErr] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const [c, r, m] = await Promise.all([api<TestCase[]>("/testcases"), api<Run[]>("/runs?take=50"), api<string[]>("/models")]);
      setCases(c); setRuns(r); setModels(m); setModel((cur) => cur || m[0]);
    } catch (e) { setErr((e as Error).message); }
  }, []);
  useEffect(() => { load(); }, [load]);

  const wrap = async (key: string, fn: () => Promise<unknown>) => {
    setBusy(key); setErr(null);
    try { await fn(); await load(); } catch (e) { setErr((e as Error).message); } finally { setBusy(null); }
  };

  const set = (k: keyof typeof empty) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setForm({ ...form, [k]: e.target.value });

  return (
    <>
      <h1 className="text-2xl font-bold">Test Cases</h1>
      <ErrorBox message={err} />

      <Card>
        <CardHeader><CardTitle>New test case</CardTitle></CardHeader>
        <CardContent>
          <form
            className="grid gap-3 md:grid-cols-2"
            onSubmit={(e) => { e.preventDefault(); wrap("create", async () => { await post("/testcases", form); setForm(empty); }); }}
          >
            <div><Label htmlFor="name">Test name</Label><Input id="name" required value={form.name} onChange={set("name")} /></div>
            <div><Label htmlFor="category">Category</Label><Input id="category" required value={form.category} onChange={set("category")} /></div>
            <div className="md:col-span-2"><Label htmlFor="prompt">Prompt</Label><Textarea id="prompt" required value={form.prompt} onChange={set("prompt")} /></div>
            <div className="md:col-span-2"><Label htmlFor="expected">Expected answer</Label><Textarea id="expected" required value={form.expectedAnswer} onChange={set("expectedAnswer")} /></div>
            <div className="md:col-span-2"><Label htmlFor="criteria">Acceptance criteria</Label><Textarea id="criteria" value={form.acceptanceCriteria} onChange={set("acceptanceCriteria")} /></div>
            <div><Button type="submit" disabled={busy === "create"}>Create test case</Button></div>
          </form>
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="flex-row items-center justify-between">
          <CardTitle>Cases ({cases.length})</CardTitle>
          <div className="flex items-center gap-2">
            <Select aria-label="Model" value={model} onChange={(e) => setModel(e.target.value)} className="w-52">
              {models.map((m) => <option key={m}>{m}</option>)}
            </Select>
            <Button variant="outline" disabled={busy === "all" || !cases.length} onClick={() => wrap("all", () => post("/runs/batch", { models: [model] }))}>
              {busy === "all" ? "Running…" : "Run all"}
            </Button>
          </div>
        </CardHeader>
        <CardContent className="overflow-x-auto">
          <table className="w-full text-left text-sm" data-testid="cases-table">
            <thead className="text-xs text-muted-foreground"><tr><th className="p-2">Name</th><th>Category</th><th>Prompt</th><th /></tr></thead>
            <tbody>
              {cases.map((c) => (
                <tr key={c.id} className="border-t">
                  <td className="p-2 font-medium">{c.name}</td><td>{c.category}</td>
                  <td className="max-w-md truncate">{c.prompt}</td>
                  <td className="space-x-1 whitespace-nowrap text-right">
                    <Button size="sm" disabled={busy === c.id} onClick={() => wrap(c.id, () => post(`/testcases/${c.id}/run`, { model }))}>{busy === c.id ? "…" : "Run"}</Button>
                    <Button size="sm" variant="ghost" aria-label={`Delete ${c.name}`} onClick={() => wrap(c.id, () => api(`/testcases/${c.id}`, { method: "DELETE" }))}>✕</Button>
                  </td>
                </tr>
              ))}
              {!cases.length && <tr><td colSpan={4} className="p-4 text-muted-foreground">No test cases yet.</td></tr>}
            </tbody>
          </table>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>Recent executions</CardTitle></CardHeader>
        <CardContent className="overflow-x-auto">
          <table className="w-full text-left text-sm" data-testid="runs-table">
            <thead className="text-xs text-muted-foreground">
              <tr><th className="p-2">Test</th><th>Model</th><th>Result</th><th>Score</th><th>Rel</th><th>Faith</th><th>Compl</th><th>Clar</th><th>Cons</th><th>ms</th><th>Tokens</th><th>Cost</th><th>Human QA</th></tr>
            </thead>
            <tbody>
              {runs.map((r) => (
                <tr key={r.id} className="border-t align-top" title={r.judgeReason ?? ""}>
                  <td className="p-2">{r.testName}</td><td>{r.model}</td>
                  <td><Badge ok={r.passed}>{r.passed ? "PASS" : `FAIL${r.failureType ? ` · ${r.failureType}` : ""}`}</Badge></td>
                  <td>{num(r.score)}</td><td>{num(r.relevance)}</td><td>{num(r.faithfulness)}</td><td>{num(r.completeness)}</td><td>{num(r.clarity)}</td><td>{num(r.consistency)}</td>
                  <td>{r.latencyMs}</td><td>{r.promptTokens + r.completionTokens}</td><td>${r.costUsd.toFixed(5)}</td>
                  <td className="space-x-1 whitespace-nowrap">
                    <Button size="sm" variant={r.humanVerdict === true ? "default" : "outline"} aria-label="Human approves" onClick={() => wrap(r.id, () => post(`/runs/${r.id}/label`, { verdict: true }))}>👍</Button>
                    <Button size="sm" variant={r.humanVerdict === false ? "danger" : "outline"} aria-label="Human rejects" onClick={() => wrap(r.id, () => post(`/runs/${r.id}/label`, { verdict: false }))}>👎</Button>
                  </td>
                </tr>
              ))}
              {!runs.length && <tr><td colSpan={13} className="p-4 text-muted-foreground">No executions yet.</td></tr>}
            </tbody>
          </table>
        </CardContent>
      </Card>
    </>
  );
}
