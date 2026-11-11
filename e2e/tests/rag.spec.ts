import { test, expect } from "@playwright/test";

test("upload a TXT document, ask a question and get RAGAS-style scores", async ({ page }) => {
  await page.goto("/rag");
  await page.getByLabel("Upload document").setInputFiles({
    name: "scrum.txt", mimeType: "text/plain",
    buffer: Buffer.from("O Scrum é um framework ágil. As sprints duram até quatro semanas. O Product Owner prioriza o backlog."),
  });
  await expect(page.getByTestId("doc-list")).toContainText("scrum.txt");

  await page.getByLabel("Question").fill("Quanto dura uma sprint?");
  await page.getByLabel(/Ground truth/).fill("Até quatro semanas.");
  await page.getByRole("button", { name: "Ask" }).click();
  await expect(page.getByTestId("rag-answer")).toBeVisible({ timeout: 60_000 });
  await expect(page.getByText("Context precision", { exact: true })).toBeVisible();
  await expect(page.getByText("Context recall", { exact: true })).toBeVisible();
});

test("rejects unsupported file types", async ({ page }) => {
  await page.goto("/rag");
  await page.getByLabel("Upload document").setInputFiles({ name: "x.exe", mimeType: "application/octet-stream", buffer: Buffer.from("MZ") });
  await expect(page.locator("main").getByRole("alert")).toContainText(/PDF, DOCX and TXT/);
});
