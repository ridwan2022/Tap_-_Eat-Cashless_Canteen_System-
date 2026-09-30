const { Builder, By } = require("selenium-webdriver");
const chrome = require("selenium-webdriver/chrome");

const BASE = "http://localhost:5095"; // Your local server URL[cite: 1]
const DELAY_MS = 300; 
async function test2() {
    // Chrome options to suppress background terminal warning noise
    const options = new chrome.Options();
    options.addArguments("--log-level=3");
    options.addArguments("--silent");

    const driver = await new Builder()
        .forBrowser("chrome")
        .setChromeOptions(options)
        .build();

    try {
        // Step 1: Perform Real Login
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

        // Wait 2.5 seconds for backend login API request & token saving to finish
        await driver.sleep(2500);
        console.log("Login submitted! Navigating through remaining pages...");

        // Step 2: Visit All Remaining Pages
        console.log("--- Testing Protected & Public Pages ---");

        // 1. Index Page
        await driver.get(`${BASE}/index.html`);
        console.log("Index page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // 2. Checkout Page
        await driver.get(`${BASE}/checkout.html`);
        console.log("Checkout page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // 3. Counter Page
        await driver.get(`${BASE}/counter.html`);
        console.log("Counter page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // 4. Kitchen Page
        await driver.get(`${BASE}/kitchen.html`);
        console.log("Kitchen page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // 5. Orders Page
        await driver.get(`${BASE}/orders.html`);
        console.log("Orders page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // 6. Wallet Page
        await driver.get(`${BASE}/wallet.html`);
        console.log("Wallet page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // 7. Admin Wallets Page
        await driver.get(`${BASE}/admin-wallets.html`);
        console.log("Admin Wallets page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // 8. Mock Gateway Page
        await driver.get(`${BASE}/mock-gateway.html`);
        console.log("Mock Gateway page opened | Title:", await driver.getTitle());
        await driver.sleep(DELAY_MS);

        // 9. Payment Result Page
        await driver.get(`${BASE}/payment-result.html`);
        console.log("Payment Result page opened | Title:", await driver.getTitle());

        const paymentLinks = await driver.findElements(By.tagName("a"));
        console.log("Total links on payment result page:", paymentLinks.length);
        await driver.sleep(DELAY_MS);

    } catch (error) {
        console.error("Test error:", error);
    } finally {
        await driver.quit();
    }
}

test2();