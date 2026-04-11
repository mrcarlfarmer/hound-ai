# WSL2 Setup & Infrastructure Guide for Hound AI

Step-by-step instructions for setting up WSL2 from scratch and installing all infrastructure required to run the Hound AI platform.

---

## 1. Install WSL2

### 1.1 Enable WSL

Open **PowerShell as Administrator** and run:

```powershell
wsl --install
```

This installs WSL2 with Ubuntu as the default distribution. Restart your machine when prompted.

### 1.2 Verify WSL2 is the default version

```powershell
wsl --set-default-version 2
```

### 1.3 Launch Ubuntu and complete initial setup

After reboot, open **Ubuntu** from the Start menu. Create your UNIX username and password when prompted.

### 1.4 Update packages

```bash
sudo apt update && sudo apt upgrade -y
```

---

## 2. Install Docker Engine (inside WSL2)

> **Option A (recommended):** Install Docker Engine directly in WSL2 (no Docker Desktop required).
> **Option B:** Install Docker Desktop for Windows with WSL2 backend integration.

### Option A: Docker Engine in WSL2

```bash
# Remove old versions
sudo apt remove docker docker-engine docker.io containerd runc 2>/dev/null

# Install prerequisites
sudo apt install -y ca-certificates curl gnupg lsb-release

# Add Docker's official GPG key
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

# Add the repository
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Install Docker Engine + Compose plugin
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Add your user to the docker group (avoids needing sudo)
sudo usermod -aG docker $USER

# Start Docker daemon
sudo service docker start
```

Log out and back in (or run `newgrp docker`) for group changes to take effect.

### Option B: Docker Desktop for Windows

1. Download and install [Docker Desktop](https://www.docker.com/products/docker-desktop/) for Windows.
2. In Docker Desktop Settings → **General**, enable **Use the WSL 2 based engine**.
3. In Settings → **Resources** → **WSL Integration**, enable your Ubuntu distro.
4. Restart Docker Desktop.

### Verify Docker

```bash
docker --version
docker compose version
docker run hello-world
```

---

## 3. Enable NVIDIA GPU Passthrough (for Ollama)

> Required for GPU-accelerated LLM inference. Skip if running CPU-only.

### 3.1 Install NVIDIA drivers on Windows

Download and install the latest [NVIDIA GPU drivers](https://www.nvidia.com/Download/index.aspx) for your card on the **Windows side**. WSL2 uses the Windows GPU driver — do NOT install NVIDIA drivers inside WSL.

### 3.2 Install NVIDIA Container Toolkit in WSL2

```bash
# Add NVIDIA package repository
curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | sudo gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg
curl -s -L https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list | \
  sed 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g' | \
  sudo tee /etc/apt/sources.list.d/nvidia-container-toolkit.list

sudo apt update
sudo apt install -y nvidia-container-toolkit

# Configure Docker to use NVIDIA runtime
sudo nvidia-ctk runtime configure --runtime=docker
sudo systemctl restart docker   # or: sudo service docker restart
```

### 3.3 Verify GPU access

```bash
nvidia-smi                                      # Should show your GPU
docker run --rm --gpus all nvidia/cuda:12.6.0-base-ubuntu24.04 nvidia-smi  # GPU visible inside container
```

---

## 4. Install .NET 9 SDK

```bash
# Install Microsoft package repository
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 9.0

# Add to PATH (append to ~/.bashrc)
echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
echo 'export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools' >> ~/.bashrc
source ~/.bashrc
```

Verify:

```bash
dotnet --version   # Should output 9.x.x
```

---

## 5. Install Node.js 22+ and Angular CLI

```bash
# Install Node.js via nvm (recommended)
curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.1/install.sh | bash
source ~/.bashrc

nvm install 22
nvm use 22
nvm alias default 22

# Verify
node --version   # Should output v22.x.x
npm --version

# Install Angular CLI globally
npm install -g @angular/cli
ng version
```

---

## 6. Clone the Repository

```bash
cd ~
git clone https://github.com/mrcarlfarmer/hound-ai.git
cd hound-ai
```

---

## 7. Configure Environment Variables

```bash
# Copy the environment template
cp .env.example .env

# Edit with your values
nano .env
```

Set the following variables in `.env`:

| Variable | Description | Example |
|----------|-------------|---------|
| `ALPACA_API_KEY` | Alpaca paper trading API key | `PK...` |
| `ALPACA_API_SECRET` | Alpaca paper trading secret | `...` |
| `ALPACA_BASE_URL` | Alpaca paper trading base URL | `https://paper-api.alpaca.markets` |
| `OLLAMA_MODEL` | Default LLM model for hounds | `gemma3` |
| `RAVENDB_URL` | RavenDB connection URL (internal) | `http://ravendb:8080` |
| `GHCR_TOKEN` | GitHub PAT with `read:packages` scope | `ghp_...` |

### Configure .NET User Secrets (for local dev outside Docker)

```bash
cd src/Hound.Trading
dotnet user-secrets set "Alpaca:ApiKey" "YOUR_ALPACA_KEY"
dotnet user-secrets set "Alpaca:ApiSecret" "YOUR_ALPACA_SECRET"

cd ../Hound.Api
dotnet user-secrets set "RavenDb:Url" "http://localhost:8080"
```

---

## 8. Start All Services with Docker Compose

### 8.1 Pull and build all containers

```bash
cd ~/hound-ai
docker compose pull        # Pull pre-built images (ollama, ravendb, watchtower)
docker compose build       # Build custom images (trading-pack, hound-api, hound-ui)
```

### 8.2 Start the full stack

```bash
docker compose up -d
```

This starts all 6 containers:

| Container | Port | URL |
|-----------|------|-----|
| `ollama` | 11434 | `http://localhost:11434` |
| `ravendb` | 8080 | `http://localhost:8080` (Management Studio) |
| `hound-api` | 5000 | `http://localhost:5000` |
| `hound-ui` | 4200 | `http://localhost:4200` |
| `trading-pack` | — | Internal only |
| `watchtower` | — | Background service |

### 8.3 Pull Ollama models

After the Ollama container is running, pull the required models:

```bash
docker exec ollama ollama pull gemma3
docker exec ollama ollama pull qwen2.5
docker exec ollama ollama pull phi3
```

Or run the bootstrap script:

```bash
docker exec ollama sh /infra/ollama/pull-models.sh
```

### 8.4 Verify services are running

```bash
# Check all containers are up
docker compose ps

# Test Ollama
curl http://localhost:11434/api/tags

# Test RavenDB (Management Studio in browser)
curl -s http://localhost:8080/databases | head

# Test API
curl http://localhost:5000/api/packs

# Test Dashboard (open in browser)
echo "Open http://localhost:4200 in your browser"
```

---

## 9. Watchtower Configuration & Verification

Watchtower automatically monitors GHCR for updated container images and redeploys them.

### 9.1 Review Watchtower configuration

The Watchtower configuration lives in `infra/watchtower/config.env`:

```env
WATCHTOWER_POLL_INTERVAL=300          # Check for updates every 5 minutes (in seconds)
WATCHTOWER_CLEANUP=true               # Remove old images after update
WATCHTOWER_INCLUDE_STOPPED=false      # Only monitor running containers
WATCHTOWER_ROLLING_RESTART=true       # Restart containers one at a time
WATCHTOWER_LABEL_ENABLE=false         # Monitor all containers (not just labelled ones)
```

### 9.2 GHCR authentication for Watchtower

Watchtower needs a GitHub Personal Access Token (PAT) with `read:packages` scope to pull from GHCR.

1. Go to [GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens](https://github.com/settings/tokens?type=beta).
2. Create a new token with the `read:packages` permission.
3. Add the token to your `.env` file as `GHCR_TOKEN`.

The `docker-compose.yml` should map this into the Watchtower container environment:

```yaml
watchtower:
  image: containrrr/watchtower
  volumes:
    - /var/run/docker.sock:/var/run/docker.sock
  env_file:
    - infra/watchtower/config.env
  environment:
    - WATCHTOWER_HTTP_API_TOKEN=${GHCR_TOKEN}
    - REPO_USER=mrcarlfarmer
    - REPO_PASS=${GHCR_TOKEN}
  restart: unless-stopped
  networks:
    - hound-net
```

### 9.3 Verify Watchtower is running

```bash
# Check Watchtower container status
docker compose ps watchtower

# View Watchtower logs to confirm it's polling
docker compose logs watchtower --tail 50

# You should see lines like:
#   time="..." level=info msg="Checking for updated images..."
#   time="..." level=info msg="Session done" ...
```

### 9.4 Test Watchtower update cycle

To manually trigger a Watchtower check:

```bash
# Send SIGHUP to trigger an immediate poll
docker kill --signal=SIGHUP $(docker compose ps -q watchtower)

# Then check logs for update activity
docker compose logs watchtower --tail 20 -f
```

---

## 10. Development Workflow

### Start in dev mode (with hot reload)

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d
```

This uses volume mounts and enables:
- **.NET hot reload** via `dotnet watch` for `trading-pack` and `hound-api`
- **Angular dev server** via `ng serve` with proxy for `hound-ui`

### Run tests locally

```bash
# .NET tests
cd ~/hound-ai
dotnet test src/Hound.sln

# Angular tests
cd ui/hound-dashboard
ng test --watch=false --browsers=ChromeHeadless
```

### View container logs

```bash
# All containers
docker compose logs -f

# Specific container
docker compose logs trading-pack -f
docker compose logs hound-api -f
```

### Stop all services

```bash
docker compose down
```

### Stop and remove all data (fresh start)

```bash
docker compose down -v   # -v removes named volumes (RavenDB data, Ollama models)
```

---

## Troubleshooting

### Docker daemon not starting in WSL2

```bash
sudo service docker start
# If that fails, check:
sudo dockerd --debug
```

### GPU not visible in containers

1. Ensure NVIDIA drivers are installed on **Windows** (not inside WSL).
2. Verify `nvidia-smi` works inside WSL.
3. Ensure `nvidia-container-toolkit` is installed.
4. Restart Docker: `sudo service docker restart`

### Port conflicts

If ports 4200, 5000, 8080, or 11434 are already in use:

```bash
# Find what's using the port
sudo lsof -i :8080
# Or change the port mapping in docker-compose.yml
```

### Watchtower not pulling updates

1. Check GHCR token is valid: `echo $GHCR_TOKEN | docker login ghcr.io -u mrcarlfarmer --password-stdin`
2. Check Watchtower logs: `docker compose logs watchtower`
3. Ensure images are tagged and pushed to GHCR correctly.
4. Verify poll interval isn't set too high.

### WSL2 memory limits

If WSL2 is consuming too much memory, create/edit `%USERPROFILE%\.wslconfig` on Windows:

```ini
[wsl2]
memory=8GB
processors=4
swap=2GB
```

Then restart WSL: `wsl --shutdown` from PowerShell.
