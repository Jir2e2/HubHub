import os
import re
import json
import requests
from flask import Flask, request, jsonify
import mysql.connector

app = Flask(__name__)

DB_CONFIG = {
    "host": os.getenv("MYSQL_HOST", "localhost"),
    "port": int(os.getenv("MYSQL_PORT", 3306)),
    "user": os.getenv("MYSQL_USER", "hubhub"),
    "password": os.getenv("MYSQL_PASSWORD", "hubhubpass"),
    "database": os.getenv("MYSQL_DATABASE", "hubhub"),
}

GITHUB_TOKEN = os.getenv("GITHUB_TOKEN", "")
LLM_API_URL = os.getenv("LLM_API_URL", "")
LLM_API_KEY = os.getenv("LLM_API_KEY", "")


def get_db():
    return mysql.connector.connect(**DB_CONFIG)


def github_headers():
    return {"Authorization": f"token {GITHUB_TOKEN}", "Accept": "application/vnd.github+json"}


def extract_urls_llm(content, file_path):
    """Извлекает ссылки из текста через LLM с промпт-инжинирингом.
    Falls back to regex if LLM unavailable."""
    prompt = (
        "Ты извлекаешь полезные HTTP(S) ссылки из текста файла репозитория.\n"
        "Верни только JSON-массив объектов {\"url\": \"...\", \"title\": \"...\"}.\n"
        "Исключи дубли, ссылки на аватары, иконки, shields.io бэйджи, "
        "локальные адреса (localhost, 127.0.0.1, 0.0.0.0), "
        "ссылки внутри кода (примеры в блоков кода), и относительные пути.\n"
        "Нормализуй URL: убери tracking-параметры (utm_*), trailing slash.\n"
        f"\nФайл: {file_path}\nСодержимое:\n{content[:6000]}\n"
    )

    if LLM_API_URL and LLM_API_KEY:
        try:
            resp = requests.post(
                LLM_API_URL,
                headers={"Authorization": f"Bearer {LLM_API_KEY}", "Content-Type": "application/json"},
                json={"model": "gpt-3.5-turbo", "messages": [{"role": "user", "content": prompt}], "temperature": 0},
                timeout=30,
            )
            data = resp.json()
            text = data["choices"][0]["message"]["content"]
            match = re.search(r"\[.*\]", text, re.DOTALL)
            if match:
                return json.loads(match.group(0))
        except Exception:
            pass

    # Fallback: regex URL extraction
    urls = re.findall(r"https?://[^\s<>\"]+", content)
    result = []
    seen = set()
    for u in urls:
        u = re.sub(r"[\),\.;\"]$", "", u)
        u = re.sub(r"[?&]utm_[^=&]+", "", u)
        u = u.rstrip("/")
        if u not in seen and not any(x in u for x in ("shields.io", "github.com/", "raw.githubusercontent.com", "localhost", "127.0.0.1", "0.0.0.0")):
            seen.add(u)
            result.append({"url": u, "title": ""})
    return result


def normalize_url(url):
    u = re.sub(r"[?&]utm_[^=&]+", "", url)
    u = u.rstrip("/")
    return u


def get_domain(url):
    m = re.search(r"https?://([^/]+)/", url)
    return m.group(1) if m else ""


@app.route("/health", methods=["GET"])
def health():
    return jsonify({"status": "ok"})


@app.route("/repos", methods=["GET"])
def list_repos():
    resp = requests.get("https://api.github.com/user/repos?type=public&per_page=100", headers=github_headers())
    repos = resp.json()
    out = []
    for r in repos:
        out.append({
            "github_repo_id": r["id"],
            "owner": r["owner"]["login"],
            "name": r["name"],
            "full_name": r["full_name"],
            "html_url": r["html_url"],
        })
    return jsonify(out)


@app.route("/scan", methods=["POST"])
def scan():
    if not GITHUB_TOKEN:
        return jsonify({"error": "GITHUB_TOKEN not set. Add it to .env and restart."}), 400

    data = request.get_json(force=True)
    repos = data.get("repositories", [])

    db = get_db()
    cursor = db.cursor()

    results = []
    for rdata in repos:
        github_repo_id = rdata.get("github_repo_id")
        owner = rdata.get("owner", "")
        name = rdata.get("name", "")
        full_name = rdata.get("full_name", "")
        html_url = rdata.get("html_url", "")

        if not github_repo_id:
            continue

        cursor.execute(
            "SELECT id FROM repositories WHERE github_repo_id=%s", (github_repo_id,),
        )
        row = cursor.fetchone()
        if row:
            repo_db_id = row[0]
        else:
            cursor.execute(
                "INSERT INTO repositories (github_repo_id, owner, name, full_name, html_url) "
                "VALUES (%s,%s,%s,%s,%s)",
                (github_repo_id, owner, name, full_name, html_url),
            )
            db.commit()
            repo_db_id = cursor.lastrowid

        # mark scan pending
        cursor.execute(
            "INSERT INTO scans (repository_id, status) VALUES (%s, 'pending')", (repo_db_id,),
        )
        db.commit()
        scan_id = cursor.lastrowid

        links_found = 0
        links_new = 0

        # get repo contents
        resp = requests.get(
            f"https://api.github.com/repos/{owner}/{name}/contents", headers=github_headers()
        )
        if resp.status_code == 401:
            return jsonify({"error": "GitHub API: 401 Unauthorized. Check GITHUB_TOKEN."}), 401
        if resp.status_code != 200:
            cursor.execute(
                "UPDATE scans SET status='failed' WHERE id=%s", (scan_id,),
            )
            db.commit()
            results.append({
                "repository": f"{owner}/{name}",
                "error": f"GitHub API {resp.status_code}",
            })
            continue

        contents = resp.json()

        target_extensions = (".md", ".txt")
        for item in contents:
            if item["type"] != "file":
                continue
            fname = item["name"].lower()
            if not fname.endswith(target_extensions):
                continue

            file_resp = requests.get(item["download_url"], headers=github_headers())
            content = file_resp.text

            source_type = "readme" if fname == "readme.md" else (
                "markdown" if fname.endswith(".md") else "text"
            )
            extracted = extract_urls_llm(content, item["path"])

            for link in extracted:
                links_found += 1
                norm = normalize_url(link["url"])
                domain = get_domain(norm)

                cursor.execute(
                    "SELECT id FROM links WHERE repository_id=%s AND normalized_url=%s",
                    (repo_db_id, norm),
                )
                if cursor.fetchone():
                    continue

                cursor.execute(
                    "INSERT INTO links (repository_id, url, normalized_url, domain, "
                    "title, source_type, source_path) VALUES (%s,%s,%s,%s,%s,%s,%s)",
                    (
                        repo_db_id, link["url"], norm, domain,
                        link.get("title", ""), source_type, item["path"],
                    ),
                )
                links_new += 1

        cursor.execute(
            "UPDATE scans SET status='completed', links_found=%s, links_new=%s WHERE id=%s",
            (links_found, links_new, scan_id),
        )
        cursor.execute(
            "UPDATE repositories SET scanned_at=NOW() WHERE id=%s", (repo_db_id,),
        )
        db.commit()

        results.append({
            "repository": f"{owner}/{name}",
            "links_found": links_found,
            "links_new": links_new,
        })

    cursor.close()
    db.close()
    return jsonify({"scanned": results})


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5002)