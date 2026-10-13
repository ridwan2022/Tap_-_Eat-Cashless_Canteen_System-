// Shared helpers used by every page. Kept dependency-free (no build step)
// so the whole frontend is just static files served from wwwroot.

const Auth = {
  KEY: "tapandeat.session",

  save(session) {
    localStorage.setItem(Auth.KEY, JSON.stringify(session));
  },

  get() {
    const raw = localStorage.getItem(Auth.KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw);
    } catch {
      return null;
    }
  },

  clear() {
    localStorage.removeItem(Auth.KEY);
  },

  token() {
    const s = Auth.get();
    return s ? s.token : null;
  },

  user() {
    const s = Auth.get();
    return s ? s.user : null;
  },

  isAdmin() {
    const u = Auth.user();
    return !!u && u.role === "Admin";
  },

  isStaff() {
    const u = Auth.user();
    return !!u && (u.role === "Admin" || u.role === "KitchenStaff");
  },

  isCustomer() {
    const u = Auth.user();
    return !!u && u.role === "Customer";
  },

  isKitchen() {
    const u = Auth.user();
    return !!u && u.role === "KitchenStaff";
  }
};

/** Sprint 2 — the shopping cart lives in localStorage until an order is placed. */
const Cart = {
  KEY: "tapandeat.cart",
  get() {
    try { return JSON.parse(localStorage.getItem(Cart.KEY)) || {}; } catch { return {}; }
  },
  save(cart) { localStorage.setItem(Cart.KEY, JSON.stringify(cart)); },
  add(item, qty = 1) {
    const cart = Cart.get();
    const line = cart[item.id] || { id: item.id, name: item.name, price: item.price, qty: 0 };
    line.qty = Math.min(20, line.qty + (Number(qty) || 1));
    line.price = item.price;
    cart[item.id] = line;
    Cart.save(cart);
  },
  setQty(id, qty) {
    const cart = Cart.get();
    if (qty <= 0) delete cart[id]; else if (cart[id]) cart[id].qty = Math.min(20, qty);
    Cart.save(cart);
  },
  clear() { localStorage.removeItem(Cart.KEY); },
  count() { return Object.values(Cart.get()).reduce((n, l) => n + l.qty, 0); },
  lines() { return Object.values(Cart.get()); }
};

/** One fresh idempotency key per checkout attempt (Task 4.3). */
function newIdempotencyKey() {
  return (crypto.randomUUID ? crypto.randomUUID() : String(Date.now()) + Math.random()).replace(/-/g, "");
}

function money(n) { return "৳" + Number(n).toFixed(2); }

/** Thin fetch wrapper: adds the bearer token, parses JSON, throws readable errors. */
async function api(path, { method = "GET", body, auth = true, headers: extraHeaders = {} } = {}) {
  const headers = { "Content-Type": "application/json", ...extraHeaders };
  if (auth) {
    const token = Auth.token();
    if (token) headers["Authorization"] = "Bearer " + token;
  }

  const res = await fetch(path, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  const text = await res.text();
  let data = null;
  if (text) {
    try { data = JSON.parse(text); } catch { data = text; }
  }

  if (!res.ok) {
    const message = (data && data.message) || `Request failed (HTTP ${res.status}).`;
    const err = new Error(message);
    err.status = res.status;
    err.data = data;
    throw err;
  }
  return data;
}

function showMessage(el, text, kind = "info") {
  el.textContent = text;
  el.className = "msg " + kind;
  el.classList.remove("hidden");
}

function hideMessage(el) {
  el.classList.add("hidden");
}

/** Renders the shared header nav based on current auth state. Call once per page. */
function renderNav() {
  const mount = document.getElementById("site-header");
  if (!mount) return;

  const user = Auth.user();
  const isCust = user && user.role === "Customer";
  const isKitch = user && user.role === "KitchenStaff";
  const isAdm = user && user.role === "Admin";

  const links = [`<a href="/menu.html">Menu</a>`];

  // Only customers can order food, add to cart, and have my orders or wallets options
  if (isCust) {
    const n = Cart.count();
    links.push(`<a href="/checkout.html">Cart${n ? " (" + n + ")" : ""}</a>`);
    links.push(`<a href="/orders.html">My orders</a>`);
    links.push(`<a href="/wallet.html">Wallet</a>`);
  }

  links.push(`<a href="/board.html">Token Board</a>`);

  // Kitchen board is exclusive for kitchen role
  if (isKitch) {
    links.push(`<a href="/kitchen.html">Kitchen</a>`);
    links.push(`<a href="/counter.html">Counter</a>`);
  }

  // Admins manage the system: counter, admin dashboards (no kitchen, no cart, no orders, no wallet)
  if (isAdm) {
    links.push(`<a href="/counter.html">Counter</a>`);
    links.push(`<a href="/admin.html">Admin</a>`);
    links.push(`<a href="/admin-wallets.html">Wallets</a>`);
    links.push(`<a href="/admin-inventory.html">Inventory</a>`);
    links.push(`<a href="/admin-forecast.html">Forecast</a>`);
    links.push(`<a href="/admin-feedback.html">Feedback</a>`);
  }

  const langLabel = (typeof I18n !== "undefined" && I18n.getLanguage() === "bn") ? "English" : "বাংলা";
  const langBtn = `<button id="lang-switch-btn" class="lang-btn" type="button" aria-label="Switch Language">🌐 ${langLabel}</button>`;

  let notifBell = "";
  if (user) {
    notifBell = `
      <div class="notif-bell-wrap">
        <button id="notif-bell-btn" class="notif-bell-btn" aria-label="Notifications" title="Notifications">
          🔔<span id="notif-badge" class="notif-badge hidden">0</span>
        </button>
        <div id="notif-dropdown" class="notif-dropdown hidden" role="region" aria-live="polite">
          <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:8px;">
            <strong>Notifications</strong>
            <button id="notif-read-all-btn" class="btn" style="padding:2px 8px; font-size:0.75rem;">Mark all read</button>
          </div>
          <div id="notif-list" style="font-size:0.85rem;">Loading...</div>
        </div>
      </div>`;
  }

  let rightSide;
  if (user) {
    rightSide = `
      ${notifBell}
      ${langBtn}
      <span id="nav-user">${escapeHtml(user.fullName)} &middot; ${escapeHtml(user.role)}</span>
      <a href="#" id="nav-logout">Log out</a>`;
  } else {
    rightSide = `${langBtn}<a href="/login.html">Log in</a><a href="/register.html">Sign up</a>`;
  }

  mount.innerHTML = `
    <a href="#main-content" class="skip-link">Skip to main content</a>
    <header class="site-header" role="banner">
      <div class="brand"><a href="/">🍛 Tap &amp; Eat</a></div>
      <nav class="site-nav" role="navigation" aria-label="Main Navigation">
        ${links.join("")}
        ${rightSide}
      </nav>
    </header>`;

  const langSwitch = document.getElementById("lang-switch-btn");
  if (langSwitch) {
    langSwitch.addEventListener("click", () => {
      if (typeof I18n !== "undefined") {
        I18n.toggleLanguage();
        renderNav();
      }
    });
  }

  if (user) {
    setupNotifications();
  }

  const logoutLink = document.getElementById("nav-logout");
  if (logoutLink) {
    logoutLink.addEventListener("click", (e) => {
      e.preventDefault();
      Auth.clear();
      window.location.href = "/login.html";
    });
  }
}

/** Sprint 4: Notifications UI helper */
async function setupNotifications() {
  const bell = document.getElementById("notif-bell-btn");
  const dropdown = document.getElementById("notif-dropdown");
  const badge = document.getElementById("notif-badge");
  const list = document.getElementById("notif-list");
  const markAllBtn = document.getElementById("notif-read-all-btn");

  if (!bell || !dropdown) return;

  async function refreshUnread() {
    try {
      const data = await api("/api/notifications/unread-count");
      if (data && data.count > 0) {
        badge.textContent = String(data.count);
        badge.classList.remove("hidden");
      } else {
        badge.classList.add("hidden");
      }
    } catch { /* silent */ }
  }

  async function loadList() {
    try {
      const items = await api("/api/notifications?limit=10");
      if (!items || items.length === 0) {
        list.innerHTML = `<p class="muted" style="margin:8px 0;">No notifications yet.</p>`;
        return;
      }
      list.innerHTML = items.map(n => `
        <div class="notif-item ${n.isRead ? '' : 'unread'}" data-id="${n.id}">
          <div style="display:flex; justify-content:space-between; align-items:flex-start;">
            <div style="font-weight:600;">${escapeHtml(n.title)}</div>
            ${!n.isRead ? `<button class="btn notif-read-btn" data-id="${n.id}" style="padding:1px 6px; font-size:0.7rem;">Read</button>` : ''}
          </div>
          <div style="color:var(--color-muted); margin-top:2px;">${escapeHtml(n.message)}</div>
          <div class="notif-time">${new Date(n.createdAtUtc).toLocaleTimeString([], {hour:'2-digit', minute:'2-digit'})}</div>
        </div>
      `).join("");

      list.querySelectorAll(".notif-read-btn").forEach(btn => {
        btn.addEventListener("click", async (e) => {
          e.stopPropagation();
          const id = btn.getAttribute("data-id");
          try {
            await api(`/api/notifications/${id}/read`, { method: "PUT" });
            await refreshUnread();
            await loadList();
          } catch { /* silent */ }
        });
      });
    } catch (err) {
      list.innerHTML = `<p class="muted">Could not load notifications.</p>`;
    }
  }

  bell.addEventListener("click", (e) => {
    e.stopPropagation();
    dropdown.classList.toggle("hidden");
    if (!dropdown.classList.contains("hidden")) {
      loadList();
    }
  });

  document.addEventListener("click", (e) => {
    if (!dropdown.contains(e.target) && e.target !== bell) {
      dropdown.classList.add("hidden");
    }
  });

  if (markAllBtn) {
    markAllBtn.addEventListener("click", async () => {
      try {
        await api("/api/notifications/read-all", { method: "PUT" });
        await refreshUnread();
        await loadList();
      } catch { /* silent */ }
    });
  }

  await refreshUnread();
}

/** Redirects to login if not authenticated; returns the user object otherwise. */
function requireAuth() {
  const user = Auth.user();
  if (!user) {
    window.location.href = "/login.html";
    return null;
  }
  return user;
}

/** Redirects non-staff (kitchen staff / admin) away. */
function requireStaff() {
  const user = requireAuth();
  if (user && !Auth.isStaff()) {
    window.location.href = "/menu.html";
    return null;
  }
  return user;
}

/** Redirects non-admins away; returns the user object for admins. */
function requireAdmin() {
  const user = requireAuth();
  if (user && user.role !== "Admin") {
    window.location.href = "/menu.html";
    return null;
  }
  return user;
}

/** Redirects non-customers away (customers only). */
function requireCustomer() {
  const user = requireAuth();
  if (user && user.role !== "Customer") {
    alert("This feature is reserved for customer accounts.");
    window.location.href = "/menu.html";
    return null;
  }
  return user;
}

/** Redirects non-kitchen staff away (exclusive for Kitchen Staff). */
function requireKitchen() {
  const user = requireAuth();
  if (user && user.role !== "KitchenStaff") {
    alert("Kitchen board is exclusive for kitchen role.");
    window.location.href = "/";
    return null;
  }
  return user;
}

function escapeHtml(str) {
  return String(str)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

document.addEventListener("DOMContentLoaded", renderNav);
