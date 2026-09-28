const { Builder, By } = require("selenium-webdriver");

const BASE = "http://localhost:5095";

async function test() {

    const driver = await new Builder()
        .forBrowser("chrome")
        .build();

    try {

        await driver.get(`${BASE}/login.html`);
        console.log("Login page opened");
        console.log("Title:", await driver.getTitle());

        await driver.get(`${BASE}/register.html`);
        console.log("Register page opened");
        console.log("Title:", await driver.getTitle());

        const regLinks = await driver.findElements(By.tagName("a"));
        console.log("Total links on register page:", regLinks.length);

        await driver.get(`${BASE}/forgot-password.html`);
        console.log("Forgot password page opened");
        console.log("Title:", await driver.getTitle());

        await driver.get(`${BASE}/menu.html`);
        console.log("Menu page opened");
        console.log("Title:", await driver.getTitle());

        await driver.get(`${BASE}/admin.html`);
        console.log("Admin page opened");
        console.log("Title:", await driver.getTitle());

        console.log("\nAll tests passed!");

    } finally {
        await driver.quit();
    }
}

test();