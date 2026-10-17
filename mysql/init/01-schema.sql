CREATE DATABASE IF NOT EXISTS hubhub DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE hubhub;

CREATE TABLE IF NOT EXISTS repositories (
    id INT AUTO_INCREMENT PRIMARY KEY,
    github_repo_id BIGINT NOT NULL UNIQUE,
    owner VARCHAR(255) NOT NULL,
    name VARCHAR(255) NOT NULL,
    full_name VARCHAR(512) NOT NULL,
    html_url VARCHAR(1024) NOT NULL,
    scanned_at DATETIME NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_owner_name (owner, name)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS links (
    id INT AUTO_INCREMENT PRIMARY KEY,
    repository_id INT NOT NULL,
    url VARCHAR(2048) NOT NULL,
    normalized_url VARCHAR(2048) NOT NULL,
    domain VARCHAR(255) NOT NULL,
    title VARCHAR(512) NULL,
    source_type ENUM('readme','markdown','text') NOT NULL,
    source_path VARCHAR(512) NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (repository_id) REFERENCES repositories(id) ON DELETE CASCADE,
    INDEX idx_repository (repository_id),
    INDEX idx_domain (domain),
    INDEX idx_normalized (normalized_url(255))
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS scans (
    id INT AUTO_INCREMENT PRIMARY KEY,
    repository_id INT NOT NULL,
    status ENUM('pending','completed','failed') NOT NULL DEFAULT 'pending',
    links_found INT NOT NULL DEFAULT 0,
    links_new INT NOT NULL DEFAULT 0,
    scanned_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (repository_id) REFERENCES repositories(id) ON DELETE CASCADE,
    INDEX idx_repository_status (repository_id, status)
) ENGINE=InnoDB;