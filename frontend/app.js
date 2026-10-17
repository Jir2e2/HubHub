let token = localStorage.getItem("hubhub_token") || "";
let repos = [];
let links = [];

const el = id => document.getElementById(id);

function api(path, opts = {}) {
  const headers = { ...opts.headers };
  if (token) headers.Authorization = "Bearer " + token;
  return fetch(path, { ...opts, headers });
}

async function loadRepos() {
  const r = await api("/api/github-repos", { method: "POST" });
  repos = await r.json();
  renderRepos();
  populateRepoFilter();
}

function renderRepos() {
  const list = el("repoList");
  list.innerHTML = "";
  repos.forEach(repo => {
    const div = document.createElement("div");
    div.className = "repo-item";
    div.innerHTML = `
      <input type="checkbox" value="${repo.id}">
      <div>
        <div class="repo-name">${repo.full_name}</div>
        <div class="repo-owner">${repo.owner} / ${repo.name}</div>
      </div>
    `;
    list.appendChild(div);
  });
  updateScanBtn();
  list.addEventListener("change", updateScanBtn);
}

function updateScanBtn() {
  const hasChecked = el("repoList").querySelector('input[type="checkbox"]:checked');
  el("scanBtn").disabled = !hasChecked;
}

function populateRepoFilter() {
  const sel = el("repoFilter");
  sel.innerHTML = '<option value="">Все репозитории</option>';
  repos.forEach(r => {
    const o = document.createElement("option");
    o.value = r.id;
    o.textContent = r.full_name;
    sel.appendChild(o);
  });
}

async function loadDomains() {
  const r = await api("/api/links/domains");
  const domains = await r.json();
  const sel = el("domainFilter");
  domains.forEach(d => {
    const o = document.createElement("option");
    o.value = d.domain;
    o.textContent = d.domain + " (" + d.cnt + ")";
    sel.appendChild(o);
  });
}

async function loadLinks() {
  const params = new URLSearchParams();
  const search = el("searchInput").value.trim();
  const domain = el("domainFilter").value;
  const repo = el("repoFilter").value;
  const date = el("dateFilter").value;
  if (search) params.set("search", search);
  if (domain) params.set("domain", domain);
  if (repo) params.set("repo", repo);
  if (date) params.set("date", date);

  const r = await api("/api/links?" + params.toString());
  links = await r.json();
  renderLinks();
}

function renderLinks() {
  const body = el("linksBody");
  body.innerHTML = "";
  links.forEach(l => {
    const tr = document.createElement("tr");
    const date = l.created_at ? new Date(l.created_at).toLocaleDateString("ru-RU") : "";
    tr.innerHTML = `
      <td><a href="${l.url}" target="_blank" rel="noopener">${l.url}</a></td>
      <td>${l.domain}</td>
      <td><span class="source">${l.source_type}</span>: ${l.source_path}</td>
      <td>${l.owner}/${l.name}</td>
      <td>${date}</td>
    `;
    body.appendChild(tr);
  });
  el("stats").textContent = "Всего ссылок: " + links.length;
}

async function scanSelected() {
  const boxes = el("repoList").querySelectorAll('input[type="checkbox"]:checked');
  if (!boxes.length) return;

  const repositories = Array.from(boxes).map(b => {
    const div = b.closest('.repo-item');
    const name = div.querySelector('.repo-name').textContent;
    const owner = div.querySelector('.repo-owner').textContent.split(' / ')[0];
    return {
      github_repo_id: parseInt(b.value),
      owner: owner,
      name: name.split(' / ')[1] || name,
      full_name: name,
      html_url: `https://github.com/${name}`,
    };
  });

  el("scanBtn").disabled = true;
  el("scanStatus").textContent = "Сканирование...";

  const r = await api("/scan", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ repositories }),
  });
  const result = await r.json();
  el("scanStatus").textContent = "Готово. Найдено ссылок: " + result.scanned.reduce((s, x) => s + (x.links_found || 0), 0);
  el("scanBtn").disabled = false;
  loadLinks();
}

el("loginBtn").onclick = async () => {
  const t = el("tokenInput").value.trim();
  if (!t) return;
  token = t;
  localStorage.setItem("hubhub_token", t);
  el("tokenInput").style.display = "none";
  el("loginBtn").style.display = "none";
  el("logoutBtn").style.display = "inline-block";
  const info = el("userInfo");
  info.style.display = "inline-block";
  info.textContent = "Токен установлен";
  await loadRepos();
  await loadDomains();
};

el("logoutBtn").onclick = () => {
  token = "";
  localStorage.removeItem("hubhub_token");
  location.reload();
};

el("scanBtn").onclick = scanSelected;

el("searchInput").addEventListener("input", loadLinks);
el("domainFilter").addEventListener("change", loadLinks);
el("repoFilter").addEventListener("change", loadLinks);
el("dateFilter").addEventListener("change", loadLinks);
el("clearFiltersBtn").onclick = () => {
  el("searchInput").value = "";
  el("domainFilter").value = "";
  el("repoFilter").value = "";
  el("dateFilter").value = "";
  loadLinks();
};

if (token) {
  el("tokenInput").style.display = "none";
  el("loginBtn").style.display = "none";
  el("logoutBtn").style.display = "inline-block";
  el("userInfo").style.display = "inline-block";
  el("userInfo").textContent = "Токен установлен";
  loadRepos();
  loadDomains();
}