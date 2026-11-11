import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

const skills = [
  ["AI Testing", "Test cases with expected answers and acceptance criteria, automated regression of prompts and models."],
  ["LLM Evaluation", "LLM-as-judge and DeepEval metrics (relevance, faithfulness, completeness, clarity) plus embedding-based consistency."],
  ["GenAI Validation", "RAG pipeline validated with RAGAS: context precision, answer relevancy, faithfulness, context recall."],
  ["Responsible AI", "Prompt injection, jailbreak, data leakage and toxicity suites aligned with the OWASP Top 10 for LLM Applications."],
  ["Quality Engineering", "xUnit + Testcontainers, Playwright E2E, Allure reports, GitHub Actions, SonarQube and OWASP checks in CI."],
];

export default function AboutPage() {
  return (
    <>
      <h1 className="text-2xl font-bold">About this Project</h1>
      <p className="max-w-3xl text-sm text-muted-foreground">
        LLM Quality Evaluator was built to demonstrate how Quality Engineering applies to generative AI in production:
        non-deterministic outputs are measured, compared across models, stress-tested for safety and tracked over time —
        following practices adopted by companies that run GenAI at scale.
      </p>
      <div className="grid gap-4 md:grid-cols-2">
        {skills.map(([t, d]) => (
          <Card key={t}><CardHeader><CardTitle>{t}</CardTitle></CardHeader><CardContent className="text-sm text-muted-foreground">{d}</CardContent></Card>
        ))}
      </div>
      <Card>
        <CardHeader><CardTitle>Stack</CardTitle></CardHeader>
        <CardContent className="text-sm text-muted-foreground">
          Next.js 15 · React · TypeScript · Tailwind · shadcn/ui · .NET 9 Web API · PostgreSQL · OpenAI / Anthropic / Gemini ·
          DeepEval · RAGAS · Docker Compose · Playwright · xUnit · Testcontainers · Allure · GitHub Actions · SonarQube.
        </CardContent>
      </Card>
    </>
  );
}
