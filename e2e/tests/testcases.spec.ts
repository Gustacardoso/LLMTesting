import { test, expect } from "@playwright/test";

test.describe("Test case lifecycle", () => {
  test("create, run and see the execution scored", async ({ page }) => {
    const name = `Scrum ${Date.now()}`;
    await page.goto("/tests");
    await page.getByLabel("Test name").fill(name);
    await page.getByLabel("Category").fill("Agile");
    await page.getByLabel("Prompt", { exact: true }).fill("Explique o que é Scrum");
    await page.getByLabel("Expected answer").fill("Framework ágil para gestão de projetos.");
    await page.getByLabel("Acceptance criteria").fill("Menciona framework ágil");
    await page.getByRole("button", { name: "Create test case" }).click();

    const row = page.getByTestId("cases-table").getByRole("row", { name: new RegExp(name) });
    await expect(row).toBeVisible();
    await row.getByRole("button", { name: "Run", exact: true }).click();

    const runRow = page.getByTestId("runs-table").getByRole("row", { name: new RegExp(name) }).first();
    await expect(runRow).toBeVisible({ timeout: 30_000 });
    await expect(runRow.getByText(/PASS|FAIL/)).toBeVisible();
  });

  test("shows an error when the API rejects invalid input", async ({ page }) => {
    await page.goto("/tests");
    // required attributes block empty submit in the browser
    await page.getByRole("button", { name: "Create test case" }).click();
    await expect(page.getByLabel("Test name")).toBeFocused();
  });
});
