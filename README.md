# CTF tournament infrastructure

This project runs CTFd, a .NET web app, and a Flask web app as Docker Compose
services. The two small web apps are intentionally vulnerable starter targets;
replace or extend them with your tournament challenges as needed.

## Configure

1. Copy `.env.example` to `.env`.
2. Replace all example values with long, unique secrets. Keep the database
   passwords alphanumeric because CTFd uses the password in a database URL.
3. Ensure Docker Engine and the Docker Compose plugin are installed.

## Initialize Git

From the project directory, initialize a repository and make the first commit:

```powershell
git init
git add .
git status
git commit -m "Initial project setup"
```

The `.gitignore` excludes local secrets such as `.env` while allowing
`.env.example` to be tracked. Confirm `git status` does not list `.env` before
committing.

## Deploy

From this directory, build and start the stack with:

```powershell
docker compose up --build -d
```

This project has not been started as part of its setup.

## Access

Use the Docker host's LAN IP address from tournament participants' devices:

| Service | URL |
| --- | --- |
| CTFd | `http://<docker-host-ip>:8000` |
| .NET target | `http://<docker-host-ip>:8001` |
| Flask target | `http://<docker-host-ip>:8002` |

The container-only service names are `dotnet-app:8080` and `flask-app:5000`.
For challenge links shown to participants, use the host's LAN IP and the
published ports above instead.

The apps include a deliberately unsafe reflected-HTML search page at `/search`
for confirming the targets are reachable. Do not expose these starter targets
to the public internet: they are intentionally vulnerable. Restrict access to
the tournament network and allow inbound TCP ports 8000-8002 through the host
firewall only for that network. The Compose network is not isolated from
outbound network access so its published web ports work with Docker Desktop.
MariaDB and Redis remain un-published and are accessible only to other
containers on the Compose network.

CTFd uploads, logs, MariaDB data, and Redis data are stored in named Docker
volumes so they persist across container restarts.
