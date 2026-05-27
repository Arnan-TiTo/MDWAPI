#!/usr/bin/env bash
set -euo pipefail

REPO_URL="${REPO_URL:-https://github.com/Arnan-TiTo/MDWAPI}"
RUNNER_TOKEN="${RUNNER_TOKEN:-}"
RUNNER_VERSION="${RUNNER_VERSION:-2.333.1}"
RUNNER_DIR="${RUNNER_DIR:-/opt/actions-runner-chmbapi}"
RUNNER_NAME="${RUNNER_NAME:-chmbapi-runner}"
RUNNER_LABELS="${RUNNER_LABELS:-self-hosted,chmbapi}"
RUNNER_GROUP="${RUNNER_GROUP:-$(id -gn)}"

if [[ -z "$RUNNER_TOKEN" ]]; then
  echo "RUNNER_TOKEN is required." >&2
  echo "Usage: RUNNER_TOKEN=<token> $0" >&2
  exit 1
fi

if [[ "$(uname -s)" != "Linux" ]]; then
  echo "This script supports Linux runners only." >&2
  exit 1
fi

case "$(uname -m)" in
  x86_64|amd64)
    RUNNER_ARCH="x64"
    ;;
  aarch64|arm64)
    RUNNER_ARCH="arm64"
    ;;
  armv7l)
    RUNNER_ARCH="arm"
    ;;
  *)
    echo "Unsupported architecture: $(uname -m)" >&2
    exit 1
    ;;
esac

RUNNER_PACKAGE="actions-runner-linux-${RUNNER_ARCH}-${RUNNER_VERSION}.tar.gz"
RUNNER_URL="https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/${RUNNER_PACKAGE}"

sudo mkdir -p "$RUNNER_DIR"
sudo chown -R "$USER":"$RUNNER_GROUP" "$RUNNER_DIR"
cd "$RUNNER_DIR"

if [[ ! -f ./config.sh ]]; then
  curl -fsSLO "$RUNNER_URL"
  tar xzf "$RUNNER_PACKAGE"
  rm -f "$RUNNER_PACKAGE"
fi

if [[ -f .runner ]]; then
  echo "Runner is already configured at $RUNNER_DIR"
else
  ./config.sh \
    --unattended \
    --url "$REPO_URL" \
    --token "$RUNNER_TOKEN" \
    --name "$RUNNER_NAME" \
    --labels "$RUNNER_LABELS" \
    --work _work \
    --replace
fi

sudo ./svc.sh install "$USER"
sudo ./svc.sh start
sudo ./svc.sh status
