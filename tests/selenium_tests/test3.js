const { Builder, By } = require("selenium-webdriver");
const chrome = require("selenium-webdriver/chrome");

const BASE = "http://localhost:5095"; // Your local server URL
const DELAY_MS = 300;

async function testSprint3() {
    // Chrome options to suppress background terminal warning noise
    const options = new chrome.Options();
    options.addArguments("--log-level=3");
    options.addArguments("--silent");

    const driver = await new Builder()
        .forBrowser("chrome")
        .setChromeOptions(options)
        .build();

    try {
        // ═══════════════════════════════════════════════════════════
        // Step 1: Perform Real Login
        // ═══════════════════════════════════════════════════════════
        console.log("--- Logging in ---");
        await driver.get(`${BASE}/login.html`);
        await driver.sleep(DELAY_MS);

        const emailInput = await driver.findElement(
            By.css("input[type='email'], #email, input[name='email']")
        );
        await emailInput.clear();
        await emailInput.sendKeys("23201019@uap-bd.edu");

        const passwordInput = await driver.findElement(
            By.css("input[type='password'], #password, input[name='password']")
        );
        await passwordInput.clear();
        await passwordInput.sendKeys("redo1234");

        const submitButton = await driver.findElement(
            By.css("button[type='submit'], form button, #loginBtn, input[type='submit']")
        );
        await submitButton.click();

        await driver.sleep(2500);
        console.log("Login submitted! Navigating through Sprint 3 pages...");

        // ═══════════════════════════════════════════════════════════
        // Step 2: Sprint 3 — Token Display Board (Story 7 / Tasks 8.3–8.4)
        // ═══════════════════════════════════════════════════════════
        console.log("--- Testing Token Display Board (board.html) ---");

        await driver.get(`${BASE}/board.html`);
        console.log("Board page opened | Title:", await driver.getTitle());
        await driver.sleep(1000);

        try {
            const h1 = await driver.findElement(By.css("h1, h2, .title"));
            console.log("Board heading:", await h1.getText());
        } catch (e) {
            console.log("Board heading note:", e.message);
        }

        try {
            const tokens = await driver.findElements(
                By.css(".token, .token-card, [data-token], #board-list, #token-board, .board-item")
            );
            console.log("Token / board items found:", tokens.length);
        } catch (e) {
            console.log("Board tokens note:", e.message);
        }

        const boardLinks = await driver.findElements(By.tagName("a"));
        console.log("Total links on board page:", boardLinks.length);
        await driver.sleep(DELAY_MS);

        // ═══════════════════════════════════════════════════════════
        // Step 3: Sprint 3 — Kitchen Display System (Story 9 / Tasks 10.3, 10.5)
        // ═══════════════════════════════════════════════════════════
        console.log("--- Testing Kitchen Display System (kitchen.html) ---");

        await driver.get(`${BASE}/kitchen.html`);
        console.log("Kitchen page opened | Title:", await driver.getTitle());
        await driver.sleep(1200);

        try {
            const h1 = await driver.findElement(By.css("h1, h2"));
            console.log("Kitchen heading:", await h1.getText());
        } catch (e) {
            console.log("Kitchen heading note:", e.message);
        }

        try {
            // Common KDS status columns / lists
            const queueEls = await driver.findElements(
                By.css(
                    "#queue, #queued, .queued, [data-status='Queued'], .status-queued, #orders-list, .order-card"
                )
            );
            console.log("Kitchen queue / order elements:", queueEls.length);

            const preparing = await driver.findElements(
                By.css("#preparing, .preparing, [data-status='Preparing'], .status-preparing")
            );
            console.log("Preparing elements:", preparing.length);

            const ready = await driver.findElements(
                By.css("#ready, .ready, [data-status='Ready'], .status-ready")
            );
            console.log("Ready elements:", ready.length);

            // Low-stock banner (Task 10.3)
            const lowStock = await driver.findElements(
                By.css("#low-stock, .low-stock, .low-stock-banner, [data-low-stock]")
            );
            console.log("Low-stock banners / sections:", lowStock.length);
        } catch (e) {
            console.log("Kitchen KDS note:", e.message);
        }
        await driver.sleep(DELAY_MS);

        // ═══════════════════════════════════════════════════════════
        // Step 4: Sprint 3 — Admin Demand Forecast (Story 8 / Tasks 9.3–9.4)
        // ═══════════════════════════════════════════════════════════
        console.log("--- Testing Admin Forecast Dashboard (admin-forecast.html) ---");

        await driver.get(`${BASE}/admin-forecast.html`);
        console.log("Admin Forecast page opened | Title:", await driver.getTitle());
        await driver.sleep(1200);

        try {
            const h1 = await driver.findElement(By.css("h1, h2"));
            console.log("Forecast heading:", await h1.getText());
        } catch (e) {
            console.log("Forecast heading note:", e.message);
        }

        try {
            const charts = await driver.findElements(
                By.css("canvas, .chart, #forecast-chart, #chart, svg")
            );
            console.log("Forecast chart / canvas elements:", charts.length);

            const rows = await driver.findElements(
                By.css("table tr, .forecast-row, .forecast-item, #forecast-list li")
            );
            console.log("Forecast rows / items:", rows.length);
        } catch (e) {
            console.log("Forecast UI note:", e.message);
        }

        const forecastLinks = await driver.findElements(By.tagName("a"));
        console.log("Total links on admin-forecast page:", forecastLinks.length);
        await driver.sleep(DELAY_MS);

        // ═══════════════════════════════════════════════════════════
        // Step 5: Sprint 3 — Admin Inventory (Story 9 / Task 10.4)
        // ═══════════════════════════════════════════════════════════
        console.log("--- Testing Admin Inventory (admin-inventory.html) ---");

        await driver.get(`${BASE}/admin-inventory.html`);
        console.log("Admin Inventory page opened | Title:", await driver.getTitle());
        await driver.sleep(1200);

        try {
            const h1 = await driver.findElement(By.css("h1, h2"));
            console.log("Inventory heading:", await h1.getText());
        } catch (e) {
            console.log("Inventory heading note:", e.message);
        }

        try {
            const tableRows = await driver.findElements(By.css("table tr, .inventory-row, .stock-row"));
            console.log("Inventory table / rows:", tableRows.length);

            const lowAlerts = await driver.findElements(
                By.css(".low-stock, .alert, .warning, [data-low-stock], #low-stock-alert")
            );
            console.log("Low-stock alerts / warnings:", lowAlerts.length);

            // Common form controls for adjust stock
            const inputs = await driver.findElements(
                By.css("input[type='number'], input[name*='stock'], #stock, .stock-input")
            );
            console.log("Stock input fields:", inputs.length);
        } catch (e) {
            console.log("Inventory UI note:", e.message);
        }
        await driver.sleep(DELAY_MS);

        // ═══════════════════════════════════════════════════════════
        // Step 6: Sprint 3 — Orders page (queue position / my position — Task 8.5)
        // ═══════════════════════════════════════════════════════════
        console.log("--- Testing Orders page (queue position) ---");

        await driver.get(`${BASE}/orders.html`);
        console.log("Orders page opened | Title:", await driver.getTitle());
        await driver.sleep(1000);

        try {
            const positionEls = await driver.findElements(
                By.css(
                    "#queue-position, .queue-position, #my-position, .position, [data-position], .token-number"
                )
            );
            console.log("Queue position / token elements:", positionEls.length);
            if (positionEls.length > 0) {
                console.log("First position text:", await positionEls[0].getText());
            }

            const orderCards = await driver.findElements(
                By.css(".order, .order-card, #orders-list li, table tr")
            );
            console.log("Order list items / rows:", orderCards.length);
        } catch (e) {
            console.log("Orders queue-position note:", e.message);
        }
        await driver.sleep(DELAY_MS);

        // ═══════════════════════════════════════════════════════════
        // Step 7: Related Sprint 2/3 pages (smoke after login)
        // ═══════════════════════════════════════════════════════════
        console.log("--- Testing related Sprint 3 journey pages ---");

        await driver.get(`${BASE}/menu.html`);
        console.log("Menu page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        await driver.get(`${BASE}/checkout.html`);
        console.log("Checkout page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        await driver.get(`${BASE}/counter.html`);
        console.log("Counter page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        await driver.get(`${BASE}/wallet.html`);
        console.log("Wallet page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        await driver.get(`${BASE}/admin-wallets.html`);
        console.log("Admin Wallets page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        await driver.get(`${BASE}/admin.html`);
        console.log("Admin page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        await driver.get(`${BASE}/index.html`);
        console.log("Index page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        console.log("--- Sprint 3 Selenium test finished successfully ---");

    } catch (error) {
        console.error("Test error:", error);
    } finally {
        await driver.quit();
    }
}

testSprint3();