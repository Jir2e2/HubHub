#!/usr/bin/env bash
set -e

echo "=== HubHub build script ==="

if [ ! -f .env ]; then
  echo "Creating .env from example..."
  cp .env.example .env 2>/dev/null || true
fi

export $(grep -v '^#' .env | xargs)

echo "Building Docker images..."
docker compose build

echo "Starting services..."
docker compose up -d

echo "Waiting for MySQL to be ready..."
for i in $(seq 1 30); do
  if docker compose exec -T mysql mysqladmin ping -h localhost --silent 2>/dev/null; then
    break
  fi
  sleep 2
done

echo "Applying database migration..."
docker compose exec -T mysql mysql -u"${MYSQL_USER}" -p"${MYSQL_PASSWORD}" "${MYSQL_DATABASE}" < mysql/init/01-schema.sql

echo "Services started:"
echo "  Frontend:  http://localhost:3000"
echo "  C# API:    http://localhost:5001"
echo "  Python API: http://localhost:5002"

echo "Done."