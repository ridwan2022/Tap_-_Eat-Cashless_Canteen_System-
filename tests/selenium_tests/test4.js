const { Builder, By } = require("selenium-webdriver");
const chrome = require("selenium-webdriver/chrome");

const BASE = "http://localhost:5095"; // Your local server URL
const DELAY_MS = 300;

async function testSprint4() {
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

        // Find Email field and type email address
        const emailInput = await driver.findElement(
            By.css("input[type='email'], #email, input[name='email']")
        );
        await emailInput.clear();
        await emailInput.sendKeys("23201019@uap-bd.edu");

        // Find Password field and type password
        const passwordInput = await driver.findElement(
            By.css("input[type='password'], #password, input[name='password']")
        );
        await passwordInput.clear();
        await passwordInput.sendKeys("redo1234");

        // Click Login / Submit button
        const submitButton = await driver.findElement(
            By.css("button[type='submit'], form button, #loginBtn, input[type='submit']")
        );
        await submitButton.click();

        // Wait for backend login API request & token saving to finish
        await driver.sleep(2500);
        console.log("Login submitted! Navigating through Sprint 4 pages...");

        // ═══════════════════════════════════════════════════════════
        // Step 2: Sprint 4 — Notifications (Story 10 / Tasks 11.3)
        // ═══════════════════════════════════════════════════════════
        console.log("--- Testing Notifications UI ---");

        await driver.get(`${BASE}/menu.html`);
        console.log("Menu page opened | Title:", await driver.getTitle());
        await driver.sleep(800);

        // Notification bell button
        try {
            const notifBell = await driver.findElement(By.css("#notif-bell-btn, .notif-bell-btn"));
            console.log("Notification bell found");
            await notifBell.click();
            await driver.sleep(600);

            // Dropdown + list
            const dropdown = await driver.findElement(By.css("#notif-dropdown, .notif-dropdown"));
            console.log("Notification dropdown visible:", await dropdown.isDisplayed());

            // Badge (may be hidden if 0)
            const badges = await driver.findElements(By.css("#notif-badge, .notif-badge"));
            if (badges.length > 0) {
                console.log("Notification badge text:", await badges[0].getText());
            }

            // Mark all read (if present)
            const markAllBtns = await driver.findElements(By.css("#notif-read-all-btn"));
            if (markAllBtns.length > 0) {
                await markAllBtns[0].click();
                console.log("Clicked 'Mark all read'");
                await driver.sleep(500);
            }
        } catch (e) {
            console.log("Notification UI note:", e.message);
        }
        await driver.sleep(DELAY_MS);

        // ═══════════════════════════════════════════════════════════
        // Step 3: Sprint 4 — Language Switcher (Story 13 / Task 14.3)
        // ═══════════════════════════════════════════════════════════
        console.log("--- Testing Language Switcher ---");

        try {
            const langBtn = await driver.findElement(By.css("#lang-switch-btn, .lang-btn"));
            console.log("Language switch button text:", await langBtn.getText());
            await langBtn.click();
            await driver.sleep(700);

            const htmlLang = await driver.findElement(By.css("html")).getAttribute("lang");
            console.log("HTML lang after switch:", htmlLang);

            // Switch back
            await langBtn.click();
            await driver.sleep(500);
            console.log("Language toggled back");
        } catch (e) {
            console.log("Language switcher note:", e.message);
        }
        await driver.sleep(DELAY_MS);

        // ═══════════════════════════════════════════════════════════
        // Step 4: Sprint 4 — Order History filters & pagination (Story 11)
        // ═══════════════════════════════════════════════════════════
        console.log("--- Testing Order History (filters + pagination) ---");

        await driver.get(`${BASE}/orders.html`);
        console.log("Orders / History page opened | Title:", await driver.getTitle());
        await driver.sleep(1000);

        // Status filter
        try {
            const statusSelect = await driver.findElement(By.css("#filter-status"));
            await statusSelect.sendKeys("Paid");
            console.log("Selected status filter: Paid");

            const fromDate = await driver.findElement(By.css("#filter-from"));
            await fromDate.sendKeys("2026-01-01");
            console.log("Set From Date");

            const toDate = await driver.findElement(By.css("#filter-to"));
            await toDate.sendKeys("2026-12-31");
            console.log("Set To Date");

            const filterBtn = await driver.findElement(By.css("#filter-btn"));
            await filterBtn.click();
            console.log("Clicked Filter button");
            await driver.sleep(1000);
        } catch (e) {
            console.log("Order history filter note:", e.message);
        }

        // Pagination controls
        try {
            const prevBtns = await driver.findElements(By.css("#prev-page-btn"));
            const nextBtns = await driver.findElements(By.css("#next-page-btn"));
            console.log("Pagination prev buttons:", prevBtns.length, "| next buttons:", nextBtns.length);
            if (nextBtns.length > 0) {
                await nextBtns[0].click();
                console.log("Clicked Next page");
                await driver.sleep(600);
            }
        } catch (e) {
            console.log("Pagination note:", e.message);
        }

        // Feedback / star rating widgets (Story 12 / Task 13.3)
        try {
            const feedbackBoxes = await driver.findElements(By.css("[data-order-feedback], .feedback-box, .star-rating"));
            console.log("Feedback / rating widgets found:", feedbackBoxes.length);
        } catch (e) {
            console.log("Feedback widget note:", e.message);
        }
        await driver.sleep(DELAY_MS);

        // ═══════════════════════════════════════════════════════════
        // Step 5: Sprint 4 — Wallet Subsidy Summary (Story 11 / Task 12.4)
        // ═══════════════════════════════════════════════════════════
        console.log("--- Testing Wallet Subsidy Summary ---");

        await driver.get(`${BASE}/wallet.html`);
        console.log("Wallet page opened | Title:", await driver.getTitle());
        await driver.sleep(1000);

        try {
            const balance = await driver.findElement(By.css("#balance"));
            console.log("Wallet balance:", await balance.getText());

            const subsidy = await driver.findElement(By.css("#subsidy"));
            console.log("Subsidy label:", await subsidy.getText());

            const credited = await driver.findElement(By.css("#sub-credited"));
            console.log("Subsidy Credited:", await credited.getText());

            const spent = await driver.findElement(By.css("#sub-spent"));
            console.log("Subsidy Spent:", await spent.getText());

            // Period selector
            const periodSelect = await driver.findElement(By.css("#subsidy-period"));
            await periodSelect.sendKeys("All Time");
            console.log("Changed subsidy period to All Time");
            await driver.sleep(800);
        } catch (e) {
            console.log("Wallet subsidy note:", e.message);
        }
        await driver.sleep(DELAY_MS);

        // ═══════════════════════════════════════════════════════════
        // Step 6: Sprint 4 — Admin Feedback Dashboard (Story 12 / Task 13.4)
        // ═══════════════════════════════════════════════════════════
        console.log("--- Testing Admin Feedback Dashboard ---");

        await driver.get(`${BASE}/admin-feedback.html`);
        console.log("Admin Feedback page opened | Title:", await driver.getTitle());
        await driver.sleep(1200);

        try {
            const h1 = await driver.findElement(By.css("h1, h2"));
            console.log("Admin Feedback heading:", await h1.getText());

            const commentsList = await driver.findElements(By.css("#comments-list"));
            console.log("Comments list containers:", commentsList.length);

            const links = await driver.findElements(By.tagName("a"));
            console.log("Total links on admin-feedback page:", links.length);
        } catch (e) {
            console.log("Admin feedback note:", e.message);
        }
        await driver.sleep(DELAY_MS);

        // ═══════════════════════════════════════════════════════════
        // Step 7: Other Sprint 4 related / updated pages
        // ═══════════════════════════════════════════════════════════
        console.log("--- Testing remaining Sprint 4 related pages ---");

        // Menu (voice order + i18n live here)
        await driver.get(`${BASE}/menu.html`);
        console.log("Menu page (revisit) | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // Kitchen
        await driver.get(`${BASE}/kitchen.html`);
        console.log("Kitchen page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // Board
        await driver.get(`${BASE}/board.html`);
        console.log("Board page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // Admin inventory (low-stock notifications relate here)
        await driver.get(`${BASE}/admin-inventory.html`);
        console.log("Admin Inventory page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // Admin wallets
        await driver.get(`${BASE}/admin-wallets.html`);
        console.log("Admin Wallets page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // Admin forecast
        await driver.get(`${BASE}/admin-forecast.html`);
        console.log("Admin Forecast page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // Index
        await driver.get(`${BASE}/index.html`);
        console.log("Index page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // Accessibility quick check — lang attribute on a few pages
        console.log("--- Accessibility quick check (lang attribute) ---");
        const pagesToCheck = [
            "menu.html",
            "orders.html",
            "wallet.html",
            "admin-feedback.html"
        ];
        for (const page of pagesToCheck) {
            await driver.get(`${BASE}/${page}`);
            await driver.sleep(400);
            const lang = await driver.findElement(By.css("html")).getAttribute("lang");
            console.log(`${page} | lang="${lang}"`);
        }

        console.log("--- Sprint 4 Selenium test finished successfully ---");

    } catch (error) {
        console.error("Test error:", error);
    } finally {
        await driver.quit();
    }
}

testSprint4();