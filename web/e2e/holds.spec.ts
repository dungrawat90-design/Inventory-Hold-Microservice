import { expect, test } from '@playwright/test';

// Needs the seeded demo stock: DEMO-1004 has plenty, DEMO-1005 has none.
test.describe('inventory holds', () => {
  test('creating a hold moves stock to held and releasing it puts the stock back', async ({ page }) => {
    await page.goto('/');
    const row = page.getByTestId('inventory-row-DEMO-1004');
    await expect(row).toBeVisible();
    const availableBefore = Number(await row.getByRole('cell').first().innerText());

    await page.getByLabel('SKU').fill('DEMO-1004');
    await page.getByLabel('Quantity').fill('3');
    await page.getByRole('button', { name: 'Create hold' }).click();

    const hold = page.getByRole('list').getByRole('listitem').filter({ hasText: '3 × DEMO-1004' }).first();
    await expect(hold).toBeVisible();
    await expect(row.getByRole('cell').first()).toHaveText(String(availableBefore - 3));

    await hold.getByRole('button', { name: 'Release hold' }).click();

    await expect(hold.getByText('Released')).toBeVisible();
    await expect(row.getByRole('cell').first()).toHaveText(String(availableBefore));
  });

  test('asking for more than is available explains why and keeps the form', async ({ page }) => {
    await page.goto('/');

    await page.getByLabel('SKU').fill('DEMO-1005');
    await page.getByLabel('Quantity').fill('1');
    await page.getByRole('button', { name: 'Create hold' }).click();

    await expect(page.getByRole('alert')).toContainText('Not enough stock available');
    await expect(page.getByRole('alert')).toContainText('Trace id');
    await expect(page.getByLabel('SKU')).toHaveValue('DEMO-1005');
  });

  test('the form refuses an empty SKU before calling the server', async ({ page }) => {
    await page.goto('/');

    await page.getByRole('button', { name: 'Create hold' }).click();

    await expect(page.getByRole('alert')).toContainText('enter a SKU');
  });
});
