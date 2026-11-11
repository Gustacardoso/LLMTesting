"use client";

import { useEffect, useState } from "react";
import { api, num, pct, post, type LeaderboardRow } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ErrorBox } from "@/components/ui/form";

export default function LeaderboardPage() {
  const [rows, setRows] = useState<LeaderboardRow[]>([]);
  const [models, setModels] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const load = () => Promise.all([api<LeaderboardRow[]>("/leaderboard"), api<string[]>("/models")])
    .then(([r, m]) => { setRows(r); setModels(m); }).catch((e) => setErr(e.message));
  useEffect(() => { load(); }, []);

  const benchmark = async () => {
    setBusy(true); setErr(null);
    try { await post("/runs/batch", { models }); await load(); } catch (e) { setErr((e as Error).message); } finally { setBusy(false); }
  };

  return (
    <>
      <h1 className="text-2xl font-bold">Model leaderboard</h1>
      <ErrorBox message={err} />
      <div>
        <Button disabled={busy || !models.length} onClick={benchmark}>{busy ? "Benchmarking…" : `Run all test cases on ${models.length} models`}</Button>
        <p className="mt-2 text-xs text-muted-foreground">Models: {models.join(", ")}. Models without an API key use the offline mock.</p>
      </div>
      <Card>
        <CardHeader><CardTitle>Ranking by average score</CardTitle></CardHeader>
        <CardContent className="overflow-x-auto">
          <table className="w-full text-left text-sm" data-testid="leaderboard-table">
            <thead className="text-xs text-muted-foreground"><tr><th className="p-2">#</th><th>Model</th><th>Runs</th><th>Avg score</th><th>Avg latency</th><th>Avg cost / run</th><th>Pass rate</th></tr></thead>
            <tbody>
              {rows.map((r, i) => (
                <tr key={r.model} className="border-t">
                  <td className="p-2">{i + 1}</td><td className="font-medium">{r.model}</td><td>{r.runs}</td>
                  <td>{num(r.avgScore)}</td><td>{r.avgLatencyMs.toFixed(0)} ms</td><td>${r.avgCostUsd.toFixed(5)}</td><td>{pct(r.passRate)}</td>
                </tr>
              ))}
              {!rows.length && <tr><td colSpan={7} className="p-4 text-muted-foreground">No data. Run test cases on multiple models.</td></tr>}
            </tbody>
          </table>
        </CardContent>
      </Card>
    </>
  );
}
