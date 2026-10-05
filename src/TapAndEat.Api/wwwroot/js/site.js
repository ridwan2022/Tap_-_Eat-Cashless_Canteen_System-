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
  }
};

/** Sprint 2 — the shopping cart lives in localStorage until an order is placed. */
const Cart = {
  KEY: "tapandeat.cart",
  get() {
    try { return JSON.parse(localStorage.getItem(Cart.KEY)) || {}; } catch { return {}; }
  },
  save(cart) { localStorage.setItem(Cart.KEY, JSON.stringify(cart)); },
  add(item) {
    const cart = Cart.get();
    const line = cart[item.id] || { id: item.id, name: item.name, price: item.price, qty: 0 };
    line.qty = Math.min(20, line.qty + 1);
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
  const links = [`<a href="/menu.html">Menu</a>`];
  if (user) {
    const n = Cart.count();
    links.push(`<a href="/checkout.html">Cart${n ? " (" + n + ")" : ""}</a>`);
    links.push(`<a href="/orders.html">My orders</a>`);
    links.push(`<a href="/wallet.html">Wallet</a>`);
  }
  links.push(`<a href="/board.html">Token Board</a>`);
  if (Auth.isStaff()) {
    links.push(`<a href="/kitchen.html">Kitchen</a>`);
    links.push(`<a href="/counter.html">Counter</a>`);
  }
  if (Auth.isAdmin()) {
    links.push(`<a href="/admin.html">Admin</a>`);
    links.push(`<a href="/admin-wallets.html">Wallets</a>`);
    links.push(`<a href="/admin-inventory.html">Inventory</a>`);
    links.push(`<a href="/admin-forecast.html">Forecast</a>`);
  }

  let rightSide;
  if (user) {
    rightSide = `<span id="nav-user">${escapeHtml(user.fullName)} &middot; ${escapeHtml(user.role)}</span>
                 <a href="#" id="nav-logout">Log out</a>`;
  } else {
    rightSide = `<a href="/login.html">Log in</a><a href="/register.html">Sign up</a>`;
  }

  mount.innerHTML = `
    <header class="site-header">
      <div class="brand"><a href="/">🍛 Tap &amp; Eat</a></div>
      <nav class="site-nav">
        ${links.join("")}
        ${rightSide}
      </nav>
    </header>`;

  const logoutLink = document.getElementById("nav-logout");
  if (logoutLink) {
    logoutLink.addEventListener("click", (e) => {
      e.preventDefault();
      Auth.clear();
      window.location.href = "/login.html";
    });
  }
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

function escapeHtml(str) {
  return String(str)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

document.addEventListener("DOMContentLoaded", renderNav);
