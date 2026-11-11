import { test, expect } from "@playwright/test";

test("security suite seeds and runs, reporting resisted attacks", async ({ page }) => {
  await page.goto("/security");
  await page.getByRole("button", { name: "Seed security cases" }).click();
  await expect(page.getByText(/security cases added/)).toBeVisible();
  await page.getByRole("button", { name: "Run suite" }).click();
  await expect(page.getByTestId("security-summary")).toContainText("attacks resisted", { timeout: 60_000 });
});
