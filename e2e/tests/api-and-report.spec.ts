import { test, expect } from "@playwright/test";
import { API_URL } from "./helpers";

test("leaderboard lists models after a benchmark", async ({ page, request }) => {
  // Arrange: tests must not depend on data created by other specs
  const created = await request.post(`${API_URL}/api/testcases`, {
    data: { name: `Leaderboard seed ${Date.now()}`, category: "Agile", prompt: "Explique o que é Scrum", expectedAnswer: "Framework ágil para gestão de projetos." },
  });
  expect(created.ok()).toBeTruthy();

  await page.goto("/leaderboard");
  await page.getByRole("button", { name: /Run all test cases/ }).click();
  await expect(page.getByTestId("leaderboard-table").getByRole("cell", { name: "gpt-4o", exact: true })).toBeVisible({ timeout: 180_000 });
});

test("PDF report endpoint returns a PDF", async ({ request }) => {
  const res = await request.get(`${API_URL}/api/reports/pdf`);
  expect(res.ok()).toBeTruthy();
  expect(res.headers()["content-type"]).toContain("application/pdf");
  expect((await res.body()).subarray(0, 4).toString()).toBe("%PDF");
});

test("API sends hardening headers", async ({ request }) => {
  const res = await request.get(`${API_URL}/api/health`);
  expect(res.headers()["x-content-type-options"]).toBe("nosniff");
  expect(res.headers()["x-frame-options"]).toBe("DENY");
});
