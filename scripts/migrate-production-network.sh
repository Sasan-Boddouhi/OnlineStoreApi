#!/usr/bin/env bash
set -Eeuo pipefail

COMPOSE_DIR="${COMPOSE_DIR:-$HOME/onlinestore-prod}"
COMPOSE_FILE="${COMPOSE_FILE:-$COMPOSE_DIR/docker-compose.prod.yml}"
ENV_FILE="${ENV_FILE:-$COMPOSE_DIR/.env}"
PROJECT="${PROJECT:-onlinestore-prod}"
NETWORK="${NETWORK:-onlinestore-prod_onlinestore-network}"
STAMP="$(date -u +%Y%m%d_%H%M%S)"
BACKUP_DIR="$COMPOSE_DIR/.network-migration-backup-$STAMP"

log() { printf '\n[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }
die() { echo "ERROR: $*" >&2; exit 1; }
require() { command -v "$1" >/dev/null 2>&1 || die "Missing command: $1"; }

require docker
docker compose version >/dev/null 2>&1 || die "Docker Compose is unavailable"
[[ -f "$COMPOSE_FILE" ]] || die "Compose file not found: $COMPOSE_FILE"
[[ -f "$ENV_FILE" ]] || die "Environment file not found: $ENV_FILE"

mkdir -p "$BACKUP_DIR"

log "Validating persistent volumes before migration"
docker volume inspect onlinestoreapi_sqlserver_data >/dev/null
docker volume inspect onlinestoreapi_redisdata >/dev/null

log "Backing up deployment configuration"
cp -a "$COMPOSE_FILE" "$BACKUP_DIR/docker-compose.prod.yml"
cp -a "$ENV_FILE" "$BACKUP_DIR/.env"

log "Validating Compose configuration"
docker compose --project-name "$PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" config >/dev/null

log "Checking current production health"
curl --fail --silent --max-time 10 http://127.0.0.1:5000/health >/dev/null || die "API is not healthy before migration"

log "Creating target network if necessary"
docker network inspect "$NETWORK" >/dev/null 2>&1 || docker network create "$NETWORK" >/dev/null

legacy_containers=()
for c in onlinestore-redis onlinestore-otel-collector onlinestore-jaeger; do
  if docker inspect "$c" >/dev/null 2>&1; then
    project="$(docker inspect "$c" --format '{{index .Config.Labels "com.docker.compose.project"}}' 2>/dev/null || true)"
    if [[ "$project" == "onlinestoreapi" ]]; then
      legacy_containers+=("$c")
    fi
  fi
done

if ((${#legacy_containers[@]} > 0)); then
  log "Found legacy Compose containers: ${legacy_containers[*]}"
  log "Stopping legacy non-persistent observability/cache containers"
  docker stop "${legacy_containers[@]}" >/dev/null
  log "Removing only legacy containers (persistent volumes are untouched)"
  docker rm "${legacy_containers[@]}" >/dev/null
fi

log "Starting the complete production stack under project $PROJECT"
docker compose --project-name "$PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" up -d --remove-orphans

log "Waiting for API health"
healthy=0
for i in {1..24}; do
  if curl --fail --silent --max-time 5 http://127.0.0.1:5000/health >/dev/null; then
    healthy=1
    break
  fi
  sleep 5
done
((healthy == 1)) || { echo "Migration failed: API did not become healthy."; echo "Backup: $BACKUP_DIR"; exit 1; }

log "Verifying all five services"
for c in onlinestore-api onlinestore-sqlserver onlinestore-redis onlinestore-otel-collector onlinestore-jaeger; do
  docker inspect "$c" >/dev/null || die "Missing container: $c"
  project="$(docker inspect "$c" --format '{{index .Config.Labels "com.docker.compose.project"}}')"
  [[ "$project" == "$PROJECT" ]] || die "$c belongs to project '$project', expected '$PROJECT'"
done

log "Verifying target network membership"
for c in onlinestore-api onlinestore-sqlserver onlinestore-redis onlinestore-otel-collector onlinestore-jaeger; do
  docker inspect "$c" --format '{{json .NetworkSettings.Networks}}' | grep -q "\"\$NETWORK\"" || die "$c is not connected to $NETWORK"
done

log "Verifying API -> SQL and API -> Redis"
curl --fail --silent --max-time 10 http://127.0.0.1:5000/health

log "Verifying OTLP gRPC listener"
docker exec onlinestore-api sh -c 'timeout 5 sh -c "</dev/tcp/otel-collector/4317"' >/dev/null 2>&1 || die "API container cannot reach otel-collector:4317"

log "Verifying Jaeger OTLP listener"
docker exec onlinestore-otel-collector sh -c 'timeout 5 sh -c "</dev/tcp/jaeger/4317"' >/dev/null 2>&1 || die "OTEL collector cannot reach jaeger:4317"

log "Verifying Redis data volume"
docker inspect onlinestore-redis --format '{{range .Mounts}}{{if eq .Name "onlinestoreapi_redisdata"}}OK{{end}}{{end}}' | grep -q '^OK$' || die "Redis external volume is not attached"

log "Verifying SQL data volume"
docker inspect onlinestore-sqlserver --format '{{range .Mounts}}{{if eq .Name "onlinestoreapi_sqlserver_data"}}OK{{end}}{{end}}' | grep -q '^OK$' || die "SQL external volume is not attached"

log "Migration completed successfully"
echo "Backup: $BACKUP_DIR"
echo "Project: $PROJECT"
echo "Network: $NETWORK"
echo "Legacy network may remain until explicitly removed."
