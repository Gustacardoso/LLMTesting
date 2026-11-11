"use client";

import { useCallback, useEffect, useState } from "react";
import { api, num, post, API, type RagAnswer, type RagDoc } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle, Kpi } from "@/components/ui/card";
import { ErrorBox, Input, Label, Select, Textarea } from "@/components/ui/form";

export default function RagPage() {
  const [docs, setDocs] = useState<RagDoc[]>([]);
  const [models, setModels] = useState<string[]>([]);
  const [model, setModel] = useState("");
  const [question, setQuestion] = useState("");
  const [truth, setTruth] = useState("");
  const [answer, setAnswer] = useState<RagAnswer | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [err, setErr] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setDocs(await api<RagDoc[]>("/rag/documents"));
      const m = await api<string[]>("/models"); setModels(m); setModel((c) => c || m[0]);
    } catch (e) { setErr((e as Error).message); }
  }, []);
  useEffect(() => { load(); }, [load]);

  const upload = async (file: File) => {
    setBusy("upload"); setErr(null);
    try {
      const fd = new FormData(); fd.append("file", file);
      const res = await fetch(`${API}/api/rag/documents`, { method: "POST", body: fd });
      if (!res.ok) throw new Error((await res.json().catch(() => ({}))).error ?? res.statusText);
      await load();
    } catch (e) { setErr((e as Error).message); } finally { setBusy(null); }
  };

  const ask = async () => {
    setBusy("ask"); setErr(null);
    try { setAnswer(await post<RagAnswer>("/rag/ask", { question, groundTruth: truth || null, model })); }
    catch (e) { setErr((e as Error).message); } finally { setBusy(null); }
  };

  return (
    <>
      <h1 className="text-2xl font-bold">RAG testing</h1>
      <ErrorBox message={err} />

      <Card>
        <CardHeader><CardTitle>1. Documents (PDF, DOCX, TXT — max 10MB)</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          <Input type="file" aria-label="Upload document" accept=".pdf,.docx,.txt" disabled={busy === "upload"}
            onChange={(e) => e.target.files?.[0] && upload(e.target.files[0])} />
          <ul className="text-sm" data-testid="doc-list">
            {docs.map((d) => (
              <li key={d.id} className="flex items-center justify-between border-t py-1">
                <span>{d.fileName} <span className="text-muted-foreground">· {d.chunks} chunks</span></span>
                <Button size="sm" variant="ghost" aria-label={`Delete ${d.fileName}`} onClick={async () => { await api(`/rag/documents/${d.id}`, { method: "DELETE" }); load(); }}>✕</Button>
              </li>
            ))}
            {!docs.length && <li className="text-muted-foreground">No documents indexed yet.</li>}
          </ul>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>2. Ask and evaluate (RAGAS)</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          <div><Label htmlFor="q">Question</Label><Textarea id="q" value={question} onChange={(e) => setQuestion(e.target.value)} /></div>
          <div><Label htmlFor="gt">Ground truth (needed for Context Recall)</Label><Textarea id="gt" value={truth} onChange={(e) => setTruth(e.target.value)} /></div>
          <div className="flex gap-2">
            <Select aria-label="Model" value={model} onChange={(e) => setModel(e.target.value)} className="w-52">{models.map((m) => <option key={m}>{m}</option>)}</Select>
            <Button disabled={!question.trim() || busy === "ask"} onClick={ask}>{busy === "ask" ? "Evaluating…" : "Ask"}</Button>
          </div>
        </CardContent>
      </Card>

      {answer && (
        <>
          <section className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <Kpi label="Context precision" value={num(answer.scores.contextPrecision)} />
            <Kpi label="Answer relevancy" value={num(answer.scores.answerRelevancy)} />
            <Kpi label="Faithfulness" value={num(answer.scores.faithfulness)} />
            <Kpi label="Context recall" value={num(answer.scores.contextRecall)} hint={`engine: ${answer.scores.engine}`} />
          </section>
          <Card>
            <CardHeader><CardTitle>Answer</CardTitle></CardHeader>
            <CardContent>
              <p className="whitespace-pre-wrap text-sm" data-testid="rag-answer">{answer.answer}</p>
              <h4 className="mt-4 text-xs font-semibold text-muted-foreground">Retrieved contexts</h4>
              <ol className="mt-1 list-decimal space-y-2 pl-5 text-sm text-muted-foreground">
                {answer.contexts.map((c, i) => <li key={i}>{c}</li>)}
              </ol>
            </CardContent>
          </Card>
        </>
      )}
    </>
  );
}
