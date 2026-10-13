# CTF tournament infrastructure

This project runs CTFd, a .NET web app, and a Flask web app as Docker Compose
services. The two small web apps are intentionally vulnerable starter targets;
replace or extend them with your tournament challenges as needed.

## Configure

1. Copy `.env.example` to `.env`.
2. Replace all example values with long, unique secrets. Keep the database
   passwords alphanumeric because CTFd uses the password in a database URL.
   Replace `FLAG1` through `FLAG5`, the .NET admin username/password, and
   account password values with values for your event; Compose supplies the
   flags and credentials to the vulnerable apps from `.env`.
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

The .NET target provides an interactive Swagger UI at `/swagger` (OpenAPI JSON
is available at `/swagger/v1/swagger.json`). Expand an endpoint and select
`Try it out` to send requests from the documentation. Register with username,
password, and email, then
authenticate at `/api/auth` and use the returned Bearer token with
`/api/profile`. Registration accepts optional birthday, address, occupation,
and gender fields. The profile also reveals the account's
`can_access_reports` and `can_access_admin_panel` settings, which are not listed
in the registration documentation. `can_access_admin_panel` remains false
even if supplied during registration. The report flag (`FLAG4`) is returned by
`/api/reports` only for accounts with report access enabled. The
`/api/reports/{id}` endpoint uses a separate SQLite database owned by the .NET
app; report access is required there as well. Its users table stores passwords
as Base64 strings. After recovering and decoding the configured .NET admin
credentials, sign in to see `FLAG5` in that account's `/api/profile` response.
The SQLite file is stored in the `dotnet-data` volume and is separate from
CTFd's MariaDB and the Flask app. Authentication locks the client's rate-limit
key after five failed attempts for 15 minutes; registration allows three
successful accounts in five minutes before a 15-minute lockout. Both limits
intentionally trust `X-Forwarded-For` as the client key, so changing that
header bypasses the limits.

The Flask homepage shows a hint in a browser. Request it with
`curl http://localhost:8002/` to receive flag 1 and the guest credentials.
The response is selected using the request's User-Agent, so this is a lab
discovery challenge rather than a security boundary.

The Flask target's staff lab follows a guest-to-employee-to-Administrator
chain: `adventurer` (Guest), `warrior` (Employee), then `legend`
(Administrator). After the first factor, open the mock inbox at `/mail` to
view the four-digit OTP and follow its link back to `/2fa`. The verification
form constructs a Base64-encoded `username:code` token server-side; guests can
exploit the intentionally broken identity binding to become an Employee, but
cannot use it to become an Administrator. Codes expire after one minute, and
the lab intentionally does not rate-limit attempts.

Warrior's dashboard displays flag 2 after successful login. The dashboard
links to My Profile and employee blog posts. My Profile defaults to the signed-in
user's details in JSON and has an editable GUID field; blog author IDs reveal
GUIDs for the IDOR. The employee blog includes Legend's password in Base64.
Legend is the sole Administrator and displays only flag 3 when compromised.
User passwords are read from `.env`, not stored in the application source.
The Flask container uses one worker so its in-memory, short-lived codes are
consistent across requests.

The Flask player test page also links to a deliberately unsafe reflected-HTML
search page at `/search`. Do not expose these starter targets to the public
internet: they are intentionally vulnerable. Restrict access to the tournament
network and allow inbound TCP ports 8000-8002 through the host firewall only
for that network. The Compose network is not isolated from outbound network
access so its published web ports work with Docker Desktop. MariaDB and Redis
remain un-published and are accessible only to other containers on the Compose
network.

CTFd uploads, logs, MariaDB data, and Redis data are stored in named Docker
volumes so they persist across container restarts.
