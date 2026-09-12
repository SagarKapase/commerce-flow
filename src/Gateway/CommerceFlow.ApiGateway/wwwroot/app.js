/* ==========================================================================
   CommerceFlow UI - plain JavaScript, no framework, no build step.
   ==========================================================================

   Deliberately dependency-free. Every line here runs in the browser exactly
   as written, so you can put a breakpoint anywhere in dev tools and see the
   request being built. A bundler would have bought nothing except a step
   between the file and what runs.

   THE ONE IDEA WORTH TAKING FROM THIS FILE: every fetch goes through api()
   below, which logs method, path, status and duration into the panel on the
   right. Nothing talks to the network any other way, so the log is a complete
   record of what the UI did - which is the whole point of the page.
   ========================================================================== */

'use strict';

/* --------------------------------------------------------------------------
   State. Tokens live in localStorage so a refresh does not sign you out.

   Worth knowing: localStorage is readable by any script on this origin, so a
   cross-site scripting bug leaks the token. The more secure alternative is an
   httpOnly cookie, which JavaScript cannot read at all - at the cost of
   needing CSRF protection, because the browser then attaches it automatically.
   Both are defensible; neither is free.
   -------------------------------------------------------------------------- */
const store = {
  get access()  { return localStorage.getItem('cf.access'); },
  get refresh() { return localStorage.getItem('cf.refresh'); },
  get user()    { try { return JSON.parse(localStorage.getItem('cf.user') || 'null'); } catch { return null; } },
  set(session) {
    localStorage.setItem('cf.access', session.accessToken);
    localStorage.setItem('cf.refresh', session.refreshToken);
    localStorage.setItem('cf.user', JSON.stringify({
      id: session.userId, email: session.email, name: session.fullName, roles: session.roles || []
    }));
  },
  clear() { ['cf.access', 'cf.refresh', 'cf.user'].forEach(k => localStorage.removeItem(k)); }
};

const isAdmin = () => (store.user?.roles || []).includes('Admin');

let categories = [];
let products = [];
let page = 1;

/* ==========================================================================
   THE ONLY PLACE THIS APP TOUCHES THE NETWORK
   ========================================================================== */
async function api(method, path, body, opts = {}) {
  const started = performance.now();
  const headers = {};
  if (body !== undefined) headers['Content-Type'] = 'application/json';

  // The token goes on every request that has one. Endpoints that do not need
  // it simply ignore it - Catalog's GET is anonymous whether or not a token
  // is present, which is why browsing works signed out.
  if (store.access && !opts.anonymous) headers['Authorization'] = `Bearer ${store.access}`;

  let response, text;
  try {
    response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    text = await response.text();
  } catch (networkError) {
    log(method, path, 0, performance.now() - started, body, String(networkError));
    toast('Could not reach the gateway. Is it running on port 7100?', true);
    throw networkError;
  }

  const ms = performance.now() - started;
  let payload = null;
  if (text) { try { payload = JSON.parse(text); } catch { payload = text; } }

  /* ----------------------------------------------------------------------
     TRANSPARENT TOKEN REFRESH.

     Access tokens last 15 minutes. Rather than making the user sign in again,
     a 401 triggers one refresh attempt and then ONE retry of the original
     request.

     "One" is doing real work in that sentence. Without the guard, a refresh
     endpoint that also returns 401 would recurse until the stack gives out -
     and because refresh tokens ROTATE, a second concurrent attempt would
     present an already-used token and trip the reuse detection built in
     Phase 2, logging the user out of every session they have.
     ---------------------------------------------------------------------- */
  if (response.status === 401 && !opts.isRetry && !opts.anonymous && store.refresh) {
    log(method, path, 401, ms, body, payload, '401 - attempting refresh');
    const refreshed = await tryRefresh();
    if (refreshed) return api(method, path, body, { ...opts, isRetry: true });
  }

  log(method, path, response.status, ms, body, payload);

  if (!response.ok) {
    const detail = payload?.detail || payload?.title || response.statusText;
    const validation = payload?.errors
      ? Object.entries(payload.errors).map(([field, msgs]) => `${field}: ${[].concat(msgs).join(' ')}`).join(' | ')
      : null;
    const error = new Error(validation || detail || `HTTP ${response.status}`);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload;
}

async function tryRefresh() {
  if (!store.refresh) return false;
  try {
    const session = await api('POST', '/api/auth/refresh',
      { refreshToken: store.refresh }, { anonymous: true, isRetry: true });
    store.set(session);
    paintSession();
    return true;
  } catch {
    // The refresh token is gone, expired, or was reused. Nothing left to do
    // but sign out cleanly rather than leave a half-authenticated page.
    store.clear();
    paintSession();
    toast('Session expired - please sign in again', true);
    return false;
  }
}

/* ==========================================================================
   REQUEST LOG
   ========================================================================== */
function log(method, path, status, ms, requestBody, responseBody, note) {
  const list = document.getElementById('logList');
  const entry = document.createElement('details');
  entry.className = 'entry';

  const cls = status === 0 ? 'c0' : `c${String(status)[0]}`;
  const label = status === 0 ? 'ERR' : status;

  entry.innerHTML = `
    <summary>
      <span class="m">${method}</span>
      <span class="p" title="${escapeHtml(path)}">${escapeHtml(path)}</span>
      <span class="c ${cls}">${label}</span>
      <span class="ms">${Math.round(ms)}ms</span>
    </summary>
    <pre>${escapeHtml(
      (note ? `// ${note}\n\n` : '') +
      (requestBody !== undefined && requestBody !== null ? `REQUEST\n${JSON.stringify(requestBody, null, 2)}\n\n` : '') +
      `RESPONSE\n${typeof responseBody === 'string' ? responseBody : JSON.stringify(responseBody, null, 2)}`
    )}</pre>`;

  list.prepend(entry);
  while (list.children.length > 60) list.lastElementChild.remove();
}

const escapeHtml = s => String(s ?? '').replace(/[&<>"']/g, c =>
  ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

function toast(message, isError) {
  const el = document.getElementById('toast');
  el.textContent = message;
  el.className = isError ? 'toast err' : 'toast';
  el.hidden = false;
  clearTimeout(toast._t);
  toast._t = setTimeout(() => { el.hidden = true; }, 4200);
}

const money = n => Number(n ?? 0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/* ==========================================================================
   SESSION
   ========================================================================== */
function paintSession() {
  const el = document.getElementById('session');
  const user = store.user;

  // These two panels are SIBLINGS of panel-shop, not children, so hiding the
  // Shop tab does not hide them - they have to be told about the active tab.
  const onShop = document.getElementById('panel-shop')?.hidden === false;
  document.getElementById('panel-account').hidden = !onShop || !user;
  document.getElementById('panel-signedout').hidden = !onShop || !!user;

  if (!user) {
    el.innerHTML = `<button class="signin" data-call="openSignIn">Sign in</button>`;
    document.getElementById('adminTab').hidden = true;
    document.getElementById('basketCount').textContent = '0';
    stopCountdown();
    return;
  }

  el.innerHTML = `
    <span class="who">${escapeHtml(user.name || user.email)}</span>
    ${(user.roles || []).map(r => `<span class="roles">${escapeHtml(r)}</span>`).join('')}
    <button class="link" data-call="logout">sign out</button>`;

  document.getElementById('acctInitial').textContent = (user.name || user.email || '?').trim().charAt(0).toUpperCase();
  document.getElementById('acctName').textContent = user.name || '(no name)';
  document.getElementById('acctEmail').textContent = user.email;
  document.getElementById('acctRoles').innerHTML = (user.roles || [])
    .map(r => `<span class="role-chip ${r === 'Admin' ? 'admin' : ''}">${escapeHtml(r)}</span>`).join('');

  // The Admin tab is HIDDEN, not protected. Hiding a button is a convenience
  // for the person using the page, never a security control - every admin
  // endpoint is enforced server-side by [Authorize(Roles = "Admin")], and a
  // customer who unhides this tab in dev tools gets 403s, not access.
  document.getElementById('adminTab').hidden = !isAdmin();

  startCountdown();
}

/* --------------------------------------------------------------------------
   ACCESS TOKEN COUNTDOWN

   Reads "exp" straight out of the JWT payload. Worth noticing that the browser
   can do this AT ALL: a JWT is base64url-encoded, NOT encrypted, so anyone
   holding one can read every claim in it. That is why a token never carries a
   secret - the signature stops it being ALTERED, it does not hide anything.
   -------------------------------------------------------------------------- */
let countdownTimer = null;

function decodeJwt(token) {
  try {
    const payload = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    return JSON.parse(atob(payload.padEnd(payload.length + (4 - payload.length % 4) % 4, '=')));
  } catch { return null; }
}

function startCountdown() {
  stopCountdown();
  const el = document.getElementById('acctExpiry');
  const claims = decodeJwt(store.access || '');
  if (!claims?.exp) { el.textContent = '—'; return; }

  const tick = () => {
    const secondsLeft = claims.exp - Math.floor(Date.now() / 1000);
    if (secondsLeft <= 0) {
      el.textContent = 'expired';
      el.classList.add('low');
      return;
    }
    const m = Math.floor(secondsLeft / 60), s = secondsLeft % 60;
    el.textContent = `${m}:${String(s).padStart(2, '0')}`;
    el.classList.toggle('low', secondsLeft < 60);
  };
  tick();
  countdownTimer = setInterval(tick, 1000);
}

function stopCountdown() {
  if (countdownTimer) { clearInterval(countdownTimer); countdownTimer = null; }
}

/* ==========================================================================
   AUTH DIALOG
   ========================================================================== */
const dialog = () => document.getElementById('authDialog');

function openAuth(mode) {
  setAuthMode(mode);
  clearFormError('siError');
  clearFormError('rgError');
  const d = dialog();
  if (!d.open) d.showModal();
  setTimeout(() => document.getElementById(mode === 'register' ? 'rgName' : 'siEmail')?.focus(), 40);
}

function setAuthMode(mode) {
  document.querySelectorAll('.auth-tab').forEach(t =>
    t.classList.toggle('active', t.dataset.authmode === mode));
  document.getElementById('signinForm').hidden = mode !== 'signin';
  document.getElementById('registerForm').hidden = mode !== 'register';
  document.getElementById('authTitle').textContent = mode === 'register' ? 'Create account' : 'Sign in';
  document.getElementById('authSubtitle').textContent =
    mode === 'register' ? 'New accounts get the Customer role' : 'Welcome back';
}

function showFormError(id, message, list) {
  const el = document.getElementById(id);
  el.innerHTML = escapeHtml(message) +
    (list?.length ? `<ul>${list.map(x => `<li>${escapeHtml(x)}</li>`).join('')}</ul>` : '');
  el.hidden = false;
}

function clearFormError(id) {
  const el = document.getElementById(id);
  el.hidden = true;
  el.textContent = '';
}

/* The password rules mirror what Identity is configured with in Program.cs.
   This is a CONVENIENCE, not the check - the server rejects a weak password
   whatever the browser thinks, and its 400 response lists exactly which rules
   failed. Duplicating the rules here just saves a round trip. */
function checkPolicy(password) {
  const rules = {
    length: password.length >= 8,
    upper: /[A-Z]/.test(password),
    lower: /[a-z]/.test(password),
    digit: /[0-9]/.test(password)
  };
  document.querySelectorAll('#rgPolicy li').forEach(li =>
    li.classList.toggle('ok', rules[li.dataset.rule]));
  return Object.values(rules).every(Boolean);
}

/* ==========================================================================
   ACTIONS - one per button, named by its data-call attribute
   ========================================================================== */
const actions = {

  /* ---------------- auth ---------------- */
  openSignIn() { openAuth('signin'); },
  openRegister() { openAuth('register'); },

  async refresh() {
    const ok = await tryRefresh();
    show('authOut', ok
      ? { refreshed: true, note: 'A NEW refresh token was issued and the old one is now dead. Send the old one again and every session for this user is revoked - that is reuse detection.' }
      : { refreshed: false });
    if (ok) toast('Session refreshed');
  },

  async me() { show('authOut', await api('GET', '/api/users/me')); },

  async logout() {
    if (store.refresh) await api('POST', '/api/auth/logout', { refreshToken: store.refresh }, { anonymous: true }).catch(() => {});
    store.clear();
    paintSession();
    document.getElementById('basketLines').innerHTML = '';
    document.getElementById('checkoutBox').hidden = true;
    document.getElementById('orderList').innerHTML = '';
    document.getElementById('authOut').textContent = '';
    switchTab('shop');
    toast('Signed out. The ACCESS token stays valid until it expires - logout revokes the REFRESH token.');
  },

  /* ---------------- catalog ---------------- */
  async categories() {
    categories = await api('GET', '/api/categories', undefined, { anonymous: true });
    const options = ['<option value="">any</option>']
      .concat(categories.map(c => `<option value="${c.id}">${escapeHtml(c.name)}</option>`));
    document.getElementById('fCategory').innerHTML = options.join('');
    document.getElementById('pCategory').innerHTML = categories
      .map(c => `<option value="${c.id}">${escapeHtml(c.name)}</option>`).join('');
  },

  async products(targetPage) {
    page = targetPage || 1;

    // Only non-empty filters are sent. Sending "search=" would be a filter on
    // an empty string, which is a different question from "do not filter".
    const query = new URLSearchParams({ page, pageSize: val('fSize') || 12 });
    const add = (k, v) => { if (v) query.set(k, v); };
    add('search', val('fSearch'));
    add('categoryId', val('fCategory'));
    add('minPrice', val('fMin'));
    add('maxPrice', val('fMax'));
    add('isActive', val('fActive'));

    const result = await api('GET', `/api/products?${query}`, undefined, { anonymous: true });
    products = result.items;
    renderProducts(result);
    fillProductPicker();
  },

  productsReset() {
    ['fSearch', 'fMin', 'fMax'].forEach(id => { document.getElementById(id).value = ''; });
    document.getElementById('fCategory').value = '';
    document.getElementById('fActive').value = '';
    return actions.products(1);
  },

  /* ---------------- basket ---------------- */
  async basket() {
    if (!store.access) { toast('Sign in first', true); return; }
    const basket = await api('GET', '/api/basket');
    renderBasket(basket);
  },

  async addItem(productId, quantity) {
    if (!store.access) { toast('Sign in to add things to a basket', true); return; }
    // Only productId and quantity. There is no price field in this request -
    // Basket fetches the price from Catalog itself (Phase 5).
    const basket = await api('POST', '/api/basket/items', { productId, quantity: Number(quantity) || 1 });
    renderBasket(basket);
    toast('Added to basket');
  },

  async setQuantity(productId, quantity) {
    const basket = await api('PUT', `/api/basket/items/${productId}`, { quantity: Number(quantity) });
    renderBasket(basket);
  },

  async removeItem(productId) {
    await api('DELETE', `/api/basket/items/${productId}`);
    toast('Removed');
    return actions.basket();
  },

  async basketClear() {
    await api('DELETE', '/api/basket');
    renderBasket({ items: [], totalQuantity: 0, totalAmount: 0 });
    toast('Basket cleared');
  },

  /* ---------------- orders ---------------- */
  async placeOrder() {
    const order = await api('POST', '/api/orders', {
      shippingAddress: {
        line1: val('addr1'), city: val('addrCity'),
        postalCode: val('addrPost'), country: val('addrCountry')
      }
    });

    // A REJECTED order still arrives as 201 Created. The HTTP status describes
    // the request; the body describes the business outcome. A client that only
    // checked response.ok would show "success" for an order that failed.
    if (order.status === 'Rejected') {
      toast(`Order rejected: ${order.failureReason}`, true);
    } else {
      toast(`Order placed - ${order.status}`);
    }
    await actions.basket();
    switchTab('orders');
    await actions.myOrders();
  },

  async myOrders() {
    const orders = await api('GET', '/api/orders/my-orders');
    renderOrders('orderList', orders);
  },

  async allOrders() {
    const status = val('oStatus');
    const query = new URLSearchParams({ page: 1, pageSize: 50 });
    if (status) query.set('status', status);
    const result = await api('GET', `/api/orders?${query}`);
    renderOrders('allOrderList', result.items, true);
  },

  async pay(orderId, token) {
    const order = await api('POST', `/api/orders/${orderId}/pay`, { paymentMethodToken: token });
    if (order.status === 'Confirmed') toast('Payment taken - order confirmed');
    else toast(`${order.status}: ${order.failureReason || ''}`, true);
    await actions.myOrders();
  },

  async cancel(orderId) {
    await api('POST', `/api/orders/${orderId}/cancel`);
    toast('Order cancelled');
    await actions.myOrders();
  },

  /* ---------------- admin ---------------- */
  async adjust() {
    const result = await api('POST', '/api/inventory/adjustments', {
      productId: val('adjProduct'),
      quantityChange: Number(val('adjQty')),
      reason: val('adjReason')
    });
    toast(`Stock now ${result.availableQuantity} available, ${result.reservedQuantity} reserved`);
    await actions.products(page);
  },

  async stockLevel() {
    const stock = await api('GET', `/api/inventory/${val('adjProduct')}`);
    toast(`${stock.availableQuantity} available / ${stock.reservedQuantity} reserved / ${stock.totalQuantity} total`);
  },

  async createCategory() {
    await api('POST', '/api/categories', { name: val('catName'), slug: val('catSlug') });
    toast('Category created');
    await actions.categories();
  },

  async createProduct() {
    await api('POST', '/api/products', {
      name: val('pName'), description: val('pDesc'), sku: val('pSku'),
      price: Number(val('pPrice')), categoryId: val('pCategory')
    });
    toast('Product created');
    await actions.products(1);
  },

  /* ---------------- endpoint map ---------------- */
  async proveBlocked() {
    // Every one of these returns 404 from the GATEWAY. Watch the services'
    // consoles while this runs: none of them is contacted at all.
    for (const [method, path, body] of [
      ['POST', '/api/payments', { orderId: '11111111-1111-1111-1111-111111111111', customerId: '22222222-2222-2222-2222-222222222222', amount: 0.01, paymentMethodToken: 'tok_success' }],
      ['GET', '/api/payments/11111111-1111-1111-1111-111111111111', undefined],
      ['POST', '/api/inventory/reservations', { referenceId: '33333333-3333-3333-3333-333333333333', lines: [] }]
    ]) {
      await api(method, path, body).catch(() => {});
    }
    toast('All 404 - the gateway has no route to them, so no service was contacted');
  },

  clearLog() { document.getElementById('logList').innerHTML = ''; }
};

/* ==========================================================================
   RENDERING
   ========================================================================== */
function renderProducts(result) {
  const grid = document.getElementById('productGrid');

  if (!result.items.length) {
    grid.innerHTML = `<p class="muted">No products match. An administrator can create one on the Admin tab.</p>`;
    document.getElementById('pager').innerHTML = '';
    return;
  }

  grid.innerHTML = result.items.map(p => `
    <div class="product ${p.isActive ? '' : 'inactive'}">
      <div class="name">${escapeHtml(p.name)} ${p.isActive ? '' : '<span class="badge-off">inactive</span>'}</div>
      <div class="sku">${escapeHtml(p.sku)}</div>
      <div class="cat">${escapeHtml(p.categoryName)}</div>
      <div class="price">${money(p.price)}</div>
      <footer>
        <input type="number" min="1" max="99" value="1" id="qty-${p.id}">
        <button class="primary" data-add="${p.id}" ${p.isActive ? '' : 'disabled'}>Add</button>
      </footer>
    </div>`).join('');

  document.getElementById('pager').innerHTML = `
    <button ${result.page <= 1 ? 'disabled' : ''} data-page="${result.page - 1}">&larr; prev</button>
    <span>page ${result.page} of ${result.totalPages || 1} &middot; ${result.totalCount} product(s)</span>
    <button ${result.page >= result.totalPages ? 'disabled' : ''} data-page="${result.page + 1}">next &rarr;</button>`;
}

function fillProductPicker() {
  const picker = document.getElementById('adjProduct');
  if (picker) picker.innerHTML = products.map(p =>
    `<option value="${p.id}">${escapeHtml(p.name)} (${escapeHtml(p.sku)})</option>`).join('');
}

function renderBasket(basket) {
  const box = document.getElementById('basketLines');
  const count = basket.totalQuantity || 0;
  document.getElementById('basketCount').textContent = count;

  if (!basket.items?.length) {
    box.innerHTML = `<p class="muted">Empty. Add something from the Shop tab.</p>`;
    document.getElementById('checkoutBox').hidden = true;
    return;
  }

  box.innerHTML = basket.items.map(i => `
    <div class="line">
      <div class="grow">
        <div class="name">${escapeHtml(i.productName)}</div>
        <div class="muted num">${money(i.unitPrice)} each</div>
      </div>
      <input type="number" min="1" max="99" value="${i.quantity}" data-qty="${i.productId}">
      <div class="num"><strong>${money(i.lineTotal)}</strong></div>
      <button class="danger" data-remove="${i.productId}">Remove</button>
    </div>`).join('')
    + `<div class="line"><div class="grow"><strong>Total</strong></div>
       <div class="num"><strong>${money(basket.totalAmount)}</strong></div></div>`;

  document.getElementById('checkoutBox').hidden = false;
}

function renderOrders(containerId, orders, adminView) {
  const box = document.getElementById(containerId);

  if (!orders?.length) {
    box.innerHTML = `<p class="muted">No orders yet.</p>`;
    return;
  }

  box.innerHTML = orders.map(o => {
    // The server tells us whether cancelling is currently allowed, so the UI
    // does not have to duplicate the state machine. It is still enforced
    // server-side - this only decides whether to show a button that would 409.
    const canPay = o.status === 'InventoryReserved';
    const items = o.items.map(i => `${i.quantity} x ${escapeHtml(i.productName)}`).join(', ');

    return `
      <div class="order">
        <div class="grow">
          <div class="oid">${o.id}</div>
          <div class="muted">${items}</div>
          ${o.failureReason ? `<div class="muted">${escapeHtml(o.failureReason)}</div>` : ''}
          ${adminView ? `<div class="muted">customer ${o.customerId}</div>` : ''}
        </div>
        <div class="num"><strong>${money(o.totalAmount)}</strong></div>
        <span class="status s-${o.status}">${o.status}</span>
        ${canPay ? `
          <select data-token="${o.id}">
            <option value="tok_success">tok_success</option>
            <option value="tok_declined">tok_declined</option>
            <option value="tok_insufficient_funds">tok_insufficient_funds</option>
            <option value="tok_timeout">tok_timeout</option>
          </select>
          <button class="primary" data-pay="${o.id}">Pay</button>` : ''}
        ${o.canBeCancelled ? `<button class="danger" data-cancel="${o.id}">Cancel</button>` : ''}
      </div>`;
  }).join('');
}

function renderEndpointMap() {
  const routed = [
    ['POST', '/api/auth/register', 'Identity', 'anonymous', 'Create a customer account'],
    ['POST', '/api/auth/login', 'Identity', 'anonymous', 'Access + refresh token'],
    ['POST', '/api/auth/refresh', 'Identity', 'anonymous', 'Rotates the refresh token'],
    ['POST', '/api/auth/logout', 'Identity', 'anonymous', 'Revokes the refresh token'],
    ['GET', '/api/users/me', 'Identity', 'any user', 'Id comes from the token'],
    ['GET', '/api/users/{id}', 'Identity', 'Admin', ''],
    ['GET', '/api/products', 'Catalog', 'anonymous', 'search, category, price range, paging'],
    ['GET', '/api/products/{id}', 'Catalog', 'anonymous', ''],
    ['POST', '/api/products', 'Catalog', 'Admin', ''],
    ['PUT', '/api/products/{id}', 'Catalog', 'Admin', ''],
    ['DELETE', '/api/products/{id}', 'Catalog', 'Admin', 'Soft delete - the row stays'],
    ['GET', '/api/categories', 'Catalog', 'anonymous', ''],
    ['GET', '/api/categories/{id}', 'Catalog', 'anonymous', ''],
    ['POST', '/api/categories', 'Catalog', 'Admin', ''],
    ['PUT', '/api/categories/{id}', 'Catalog', 'Admin', ''],
    ['DELETE', '/api/categories/{id}', 'Catalog', 'Admin', '409 if it still has products'],
    ['GET', '/api/basket', 'Basket', 'any user', 'Owner from the sub claim'],
    ['POST', '/api/basket/items', 'Basket', 'any user', 'Price fetched from Catalog'],
    ['PUT', '/api/basket/items/{productId}', 'Basket', 'any user', 'Sets an exact quantity'],
    ['DELETE', '/api/basket/items/{productId}', 'Basket', 'any user', ''],
    ['DELETE', '/api/basket', 'Basket', 'any user', ''],
    ['GET', '/api/inventory/{productId}', 'Inventory', 'any user', 'available / reserved / total'],
    ['POST', '/api/inventory/adjustments', 'Inventory', 'Admin', 'Receive or write off stock'],
    ['POST', '/api/orders', 'Ordering', 'any user', 'Items come from the basket'],
    ['GET', '/api/orders/my-orders', 'Ordering', 'any user', ''],
    ['GET', '/api/orders/{id}', 'Ordering', 'owner or Admin', '404 if not yours'],
    ['GET', '/api/orders', 'Ordering', 'Admin', 'Paged, filterable by status'],
    ['POST', '/api/orders/{id}/pay', 'Ordering', 'owner or Admin', 'Charges, then confirms or releases stock'],
    ['POST', '/api/orders/{id}/cancel', 'Ordering', 'owner or Admin', 'Only Pending or InventoryReserved']
  ];

  const blocked = [
    ['POST', '/api/payments', 'Payment', 'Takes the amount from its caller - a customer could pay 0.01'],
    ['GET', '/api/payments/{id}', 'Payment', ''],
    ['POST', '/api/inventory/reservations', 'Inventory', 'Internal machinery Ordering drives'],
    ['POST', '/api/inventory/reservations/{id}/confirm', 'Inventory', ''],
    ['POST', '/api/inventory/reservations/{id}/release', 'Inventory', '']
  ];

  document.getElementById('endpointTable').innerHTML =
    `<tr><th>Endpoint</th><th>Service</th><th>Authorization</th><th>Notes</th></tr>` +
    routed.map(([m, p, s, a, n]) =>
      `<tr><td class="ep">${m} ${escapeHtml(p)}</td><td>${s}</td><td>${a}</td><td class="muted">${escapeHtml(n)}</td></tr>`).join('');

  document.getElementById('blockedTable').innerHTML =
    `<tr><th>Endpoint</th><th>Service</th><th>Why it is not exposed</th></tr>` +
    blocked.map(([m, p, s, n]) =>
      `<tr><td class="ep">${m} ${escapeHtml(p)}</td><td>${s}</td><td class="muted">${escapeHtml(n)}</td></tr>`).join('');
}

/* ==========================================================================
   WIRING
   ========================================================================== */
const val = id => document.getElementById(id)?.value.trim() ?? '';
const show = (id, data) => { document.getElementById(id).textContent = JSON.stringify(data, null, 2); };

function switchTab(name) {
  // Basket and Orders are meaningless without a session, so asking for one is
  // friendlier than showing an empty page and a 401 in the log. The SERVER
  // still refuses either way - this only saves the round trip.
  if ((name === 'basket' || name === 'orders') && !store.access) {
    toast('Sign in to use a basket and place orders');
    openAuth('signin');
    return;
  }

  document.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t.dataset.panel === name));
  ['shop', 'basket', 'orders', 'admin', 'endpoints'].forEach(p => {
    document.getElementById('panel-' + p).hidden = p !== name;
  });

  // The account / signed-out panels belong to the Shop tab.
  const onShop = name === 'shop';
  document.getElementById('panel-account').hidden = !onShop || !store.user;
  document.getElementById('panel-signedout').hidden = !onShop || !!store.user;

  if (name === 'basket') actions.basket().catch(() => {});
  if (name === 'orders') actions.myOrders().catch(() => {});
}

// One listener for the whole page. Every button carries a data-* attribute
// saying what it does, so there is no per-element wiring to keep in step.
document.addEventListener('click', async event => {
  const el = event.target.closest('[data-call],[data-add],[data-remove],[data-pay],[data-cancel],[data-page],.tab');
  if (!el) return;

  try {
    if (el.classList.contains('tab')) return switchTab(el.dataset.panel);
    if (el.dataset.call) return await actions[el.dataset.call]();
    if (el.dataset.add) return await actions.addItem(el.dataset.add, val(`qty-${el.dataset.add}`));
    if (el.dataset.remove) return await actions.removeItem(el.dataset.remove);
    if (el.dataset.cancel) return await actions.cancel(el.dataset.cancel);
    if (el.dataset.page) return await actions.products(Number(el.dataset.page));
    if (el.dataset.pay) {
      const token = document.querySelector(`[data-token="${el.dataset.pay}"]`).value;
      el.disabled = true;
      try { return await actions.pay(el.dataset.pay, token); } finally { el.disabled = false; }
    }
  } catch (error) {
    // Errors are already in the request log with their full body; the toast
    // is just so you notice without reading it.
    toast(error.message || 'Request failed', true);
  }
});

document.addEventListener('change', async event => {
  const productId = event.target.dataset.qty;
  if (!productId) return;
  try { await actions.setQuantity(productId, event.target.value); }
  catch (error) { toast(error.message, true); await actions.basket(); }
});

/* ==========================================================================
   DIALOG WIRING
   ========================================================================== */

document.addEventListener('click', event => {
  const tab = event.target.closest('.auth-tab');
  if (tab) { event.preventDefault(); setAuthMode(tab.dataset.authmode); }

  const chip = event.target.closest('[data-fill]');
  if (chip) {
    const [email, password] = chip.dataset.fill.split('|');
    document.getElementById('siEmail').value = email;
    document.getElementById('siPassword').value = password;
    clearFormError('siError');
  }
});

// Live password policy feedback while registering.
document.getElementById('rgPassword').addEventListener('input', e => checkPolicy(e.target.value));

/* ---------------- sign in ---------------- */
document.getElementById('signinForm').addEventListener('submit', async event => {
  event.preventDefault();
  clearFormError('siError');

  const button = document.getElementById('siSubmit');
  button.disabled = true;
  button.textContent = 'Signing in...';

  try {
    const session = await api('POST', '/api/auth/login', {
      email: val('siEmail'), password: val('siPassword')
    }, { anonymous: true });

    store.set(session);
    paintSession();
    dialog().close();
    toast('Signed in as ' + session.email);

    await actions.products(1);
    await actions.basket().catch(() => {});
  } catch (error) {
    // 401 here is deliberately vague - "Invalid email or password" for BOTH a
    // wrong password and an unknown address. Anything more specific would let
    // somebody enumerate which addresses have accounts.
    showFormError('siError', error.message || 'Could not sign in');
  } finally {
    button.disabled = false;
    button.textContent = 'Sign in';
  }
});

/* ---------------- register ---------------- */
document.getElementById('registerForm').addEventListener('submit', async event => {
  event.preventDefault();
  clearFormError('rgError');

  const email = val('rgEmail'), password = val('rgPassword'), name = val('rgName');

  if (!name || !email || !password) {
    return showFormError('rgError', 'Please fill in every field.');
  }
  if (!checkPolicy(password)) {
    return showFormError('rgError', 'That password does not meet the requirements above.');
  }

  const button = document.getElementById('rgSubmit');
  button.disabled = true;
  button.textContent = 'Creating account...';

  try {
    // Registration returns 201 with the new user and NO tokens - creating an
    // account and signing in are separate operations, deliberately. So we sign
    // in straight afterwards, which is a UI convenience rather than something
    // the API does for us.
    await api('POST', '/api/auth/register',
      { email, password, fullName: name }, { anonymous: true });

    const session = await api('POST', '/api/auth/login',
      { email, password }, { anonymous: true });

    store.set(session);
    paintSession();
    dialog().close();
    toast('Welcome, ' + session.fullName + '. You have the Customer role.');
    await actions.products(1);
  } catch (error) {
    // api() has already flattened an Identity ProblemDetails - including the
    // LIST of password rules that failed - into error.message, so every broken
    // rule is shown at once rather than one attempt at a time.
    showFormError('rgError', error.message || 'Could not create the account');
  } finally {
    button.disabled = false;
    button.textContent = 'Create account';
  }
});

/* ---------------- start ---------------- */
(async function start() {
  paintSession();
  switchTab('shop');
  renderEndpointMap();

  try {
    await actions.categories();
    await actions.products(1);
    if (store.access) await actions.basket();
  } catch {
    toast('Could not load the catalog. Are all six services running?', true);
  }
})();
