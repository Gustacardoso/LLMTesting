"use client";

import { useEffect, useState } from "react";
import { api, post, type SecurityResult } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge, ErrorBox, Select } from "@/components/ui/form";

const TYPES = ["", "PromptInjection", "Jailbreak", "DataLeakage", "Toxicity"];

export default function SecurityPage() {
  const [models, setModels] = useState<string[]>([]);
  const [model, setModel] = useState("");
  const [type, setType] = useState("");
  const [results, setResults] = useState<SecurityResult[]>([]);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => { api<string[]>("/models").then((m) => { setModels(m); setModel(m[0]); }).catch((e) => setErr(e.message)); }, []);

  const run = async (fn: () => Promise<void>) => {
    setBusy(true); setErr(null); setMsg(null);
    try { await fn(); } catch (e) { setErr((e as Error).message); } finally { setBusy(false); }
  };

  const failed = results.filter((r) => !r.passed).length;

  return (
    <>
      <h1 className="text-2xl font-bold">Security suite</h1>
      <p className="text-sm text-muted-foreground">
        Adversarial tests aligned with the OWASP Top 10 for LLM Applications: prompt injection, jailbreak, data leakage and toxicity.
        A test <strong>passes</strong> when the model resists the attack.
      </p>
      <ErrorBox message={err} />
      <div className="flex flex-wrap items-end gap-2">
        <Button variant="outline" disabled={busy} onClick={() => run(async () => { const r = await post<{ added: number }>("/security/seed"); setMsg(`${r.added} new security cases added.`); })}>
          Seed security cases
        </Button>
        <Select aria-label="Model" value={model} onChange={(e) => setModel(e.target.value)} className="w-52">{models.map((m) => <option key={m}>{m}</option>)}</Select>
        <Select aria-label="Attack type" value={type} onChange={(e) => setType(e.target.value)} className="w-44">
          {TYPES.map((t) => <option key={t} value={t}>{t || "All types"}</option>)}
        </Select>
        <Button disabled={busy} onClick={() => run(async () => setResults(await post<SecurityResult[]>("/security/run", { models: [model], type: type || null })))}>
          {busy ? "Running…" : "Run suite"}
        </Button>
      </div>
      {msg && <p className="text-sm text-success">{msg}</p>}

      {results.length > 0 && (
        <Card>
          <CardHeader><CardTitle data-testid="security-summary">{results.length - failed}/{results.length} attacks resisted</CardTitle></CardHeader>
          <CardContent className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-xs text-muted-foreground"><tr><th className="p-2">Test</th><th>Type</th><th>Result</th><th>Reason</th><th>Model response</th></tr></thead>
              <tbody>
                {results.map((r) => (
                  <tr key={r.id} className="border-t align-top">
                    <td className="p-2">{r.testName}</td><td>{r.type}</td>
                    <td><Badge ok={r.passed}>{r.passed ? "RESISTED" : r.toxic ? "TOXIC" : "VULNERABLE"}</Badge></td>
                    <td className="max-w-xs">{r.judgeReason}</td>
                    <td className="max-w-md text-muted-foreground"><span className="line-clamp-3">{r.response}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </CardContent>
        </Card>
      )}
    </>
  );
}
