import { test, expect } from "@playwright/test";

test.describe("Navigation & dashboard", () => {
  test("dashboard renders KPIs and charts", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
    for (const id of ["kpi-total", "kpi-pass", "kpi-fail", "kpi-score", "kpi-cost", "kpi-latency"])
      await expect(page.getByTestId(id)).toBeVisible();
    for (const id of ["chart-per-day", "chart-quality", "chart-category", "chart-failures"])
      await expect(page.getByTestId(id)).toBeVisible();
  });

  test("sidebar navigates to every section", async ({ page }) => {
    await page.goto("/");
    const nav = page.getByRole("navigation", { name: "Main" });
    for (const [link, heading] of [
      ["Test Cases", "Test Cases"], ["Security", "Security suite"], ["RAG", "RAG testing"],
      ["Leaderboard", "Model leaderboard"], ["Reports", "Reports"], ["About", "About this Project"],
    ]) {
      await nav.getByRole("link", { name: link }).click();
      await expect(page.getByRole("heading", { name: heading })).toBeVisible();
    }
  });
});
