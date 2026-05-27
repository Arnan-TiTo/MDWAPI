# CHMBAPI Deployment

CHMBAPI is deployed by `.github/workflows/deploy-chmbapi.yml`.

## GitHub Actions runner

Run this on the CHMBAPI deployment server with a fresh repository runner token:

```bash
RUNNER_TOKEN='<new-runner-token>' ./scripts/setup-chmbapi-runner.sh
```

The runner is registered with these labels:

```text
self-hosted,chmbapi
```

## Application service

Install the systemd service on the server:

```bash
sudo cp deploy/chmbapi.service /etc/systemd/system/chmbapi.service
sudo systemctl daemon-reload
sudo systemctl enable chmbapi
sudo systemctl start chmbapi
sudo systemctl status chmbapi --no-pager
```

The GitHub Actions workflow publishes to `/opt/chmbapi`, restarts `chmbapi`,
and checks `http://127.0.0.1:7292/health`.
