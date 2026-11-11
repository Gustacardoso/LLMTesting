"use client";

import { useEffect, useState } from "react";
import { Bar, BarChart, CartesianGrid, Cell, Legend, Line, LineChart, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { api, num, pct, type Dashboard, type QaMetrics } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle, Kpi } from "@/components/ui/card";
import { ErrorBox } from "@/components/ui/form";

const COLORS = ["#6366f1", "#22c55e", "#f59e0b", "#ef4444", "#06b6d4", "#a855f7"];

function Chart({ title, children, testId }: { title: string; children: React.ReactNode; testId: string }) {
  return (
    <Card data-testid={testId}>
      <CardHeader><CardTitle>{title}</CardTitle></CardHeader>
      <CardContent className="h-64">
        <ResponsiveContainer width="100%" height="100%">{children as React.ReactElement}</ResponsiveContainer>
      </CardContent>
    </Card>
  );
}

export default function DashboardPage() {
  const [d, setD] = useState<Dashboard | null>(null);
  const [qa, setQa] = useState<QaMetrics | null>(null);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([api<Dashboard>("/dashboard"), api<QaMetrics>("/metrics")])
      .then(([a, b]) => { setD(a); setQa(b); })
      .catch((e) => setErr(e.message));
  }, []);

  if (err) return <ErrorBox message={`Could not load dashboard: ${err}`} />;
  if (!d || !qa) return <p className="text-muted-foreground">Loading…</p>;
  const k = d.kpis;

  return (
    <>
      <h1 className="text-2xl font-bold">Dashboard</h1>
      <section className="grid grid-cols-2 gap-3 lg:grid-cols-3 xl:grid-cols-6" aria-label="KPIs">
        <Kpi label="Tests executed" value={String(k.totalRuns)} testId="kpi-total" />
        <Kpi label="Pass rate" value={pct(k.passRate)} testId="kpi-pass" />
        <Kpi label="Fail rate" value={pct(k.failRate)} testId="kpi-fail" />
        <Kpi label="Avg score" value={num(k.avgScore)} testId="kpi-score" />
        <Kpi label="Est. cost / run" value={`$${k.avgCostUsd.toFixed(4)}`} testId="kpi-cost" />
        <Kpi label="Avg response time" value={`${k.avgLatencyMs.toFixed(0)} ms`} testId="kpi-latency" />
      </section>

      <section className="grid grid-cols-2 gap-3 lg:grid-cols-4" aria-label="AI QA metrics">
        <Kpi label="Hallucination rate" value={pct(qa.hallucinationRate)} hint="faithfulness < 0.5" />
        <Kpi label="Prompt success rate" value={pct(qa.promptSuccessRate)} />
        <Kpi label="Toxicity rate" value={pct(qa.toxicityRate)} />
        <Kpi label="Groundedness" value={pct(qa.groundednessScore)} />
        <Kpi label="Answer relevancy" value={pct(qa.answerRelevancyScore)} />
        <Kpi label="False positive rate" value={pct(qa.falsePositiveRate)} hint={`${qa.humanLabeledRuns} human labels`} />
        <Kpi label="False negative rate" value={pct(qa.falseNegativeRate)} hint="judge vs human QA" />
      </section>

      <section className="grid gap-4 lg:grid-cols-2">
        <Chart title="Tests per day" testId="chart-per-day">
          <BarChart data={d.runsPerDay}>
            <CartesianGrid strokeDasharray="3 3" /><XAxis dataKey="day" /><YAxis allowDecimals={false} /><Tooltip /><Legend />
            <Bar dataKey="passed" stackId="a" fill="#22c55e" /><Bar dataKey="failed" stackId="a" fill="#ef4444" />
          </BarChart>
        </Chart>
        <Chart title="Quality evolution (avg score)" testId="chart-quality">
          <LineChart data={d.qualityTrend}>
            <CartesianGrid strokeDasharray="3 3" /><XAxis dataKey="day" /><YAxis domain={[0, 1]} /><Tooltip />
            <Line type="monotone" dataKey="avgScore" stroke="#6366f1" strokeWidth={2} />
          </LineChart>
        </Chart>
        <Chart title="Distribution by category" testId="chart-category">
          <PieChart>
            <Pie data={d.byCategory} dataKey="count" nameKey="name" outerRadius={80} label>
              {d.byCategory.map((_, i) => <Cell key={i} fill={COLORS[i % COLORS.length]} />)}
            </Pie>
            <Tooltip /><Legend />
          </PieChart>
        </Chart>
        <Chart title="Failures by type" testId="chart-failures">
          <BarChart data={d.failuresByType} layout="vertical">
            <CartesianGrid strokeDasharray="3 3" /><XAxis type="number" allowDecimals={false} /><YAxis type="category" dataKey="name" width={100} /><Tooltip />
            <Bar dataKey="count" fill="#ef4444" />
          </BarChart>
        </Chart>
      </section>
      {k.totalRuns === 0 && <p className="text-sm text-muted-foreground">No executions yet. Create test cases and run them, or seed the security suite.</p>}
    </>
  );
}
