import base64
import html
import hmac
import json
import os
import secrets
import time
import uuid

from flask import Flask, jsonify, redirect, request, session, url_for

app = Flask(__name__)
app.config["SECRET_KEY"] = os.environ.get(
    "FLASK_SECRET_KEY", "ctf-builder-flask-development-key"
)

# Sample guest credentials: username=adventurer
ACCOUNTS = {
    "adventurer": {"password": os.environ["ADVENTURER_PASSWORD"], "role": "Guest"},
    "warrior": {"password": os.environ["WARRIOR_PASSWORD"], "role": "Employee"},
    "legend": {"password": os.environ["LEGEND_PASSWORD"], "role": "Administrator"},
}
USERS = {
    "adventurer": {
        "id": "10000000-0000-4000-8000-000000000001",
        "name": "Test Guest",
        "email": "adventurer@example.test",
        "department": "Visitor",
    },
    "warrior": {
        "id": "10000000-0000-4000-8000-000000000101",
        "name": "Warrior",
        "email": "warrior@example.test",
        "department": "Customer Support",
    },
    "legend": {
        "id": "10000000-0000-4000-8000-000000000102",
        "name": "Legend",
        "email": "legend@example.test",
        "department": "IT",
    },
}
FLAG1 = os.environ["FLAG1"]
FLAG2 = os.environ["FLAG2"]
FLAG3 = os.environ["FLAG3"]
OTP_LIFETIME_SECONDS = 60
otp_store = {}
BLOG_POSTS = [
    {
        "id": 1,
        "author": "warrior",
        "title": "Team Building was a B.L.A.S.T.",
        "body": "B.L.A.S.T. means Breaking while Learning in Application Security...and the team building event was a blast! We had a great time and learned a lot about each other. We should have more events like this in the future."
    },
    {
        "id": 2,
        "author": "warrior",
        "title": "I love my JOB!",
        "body": "Sometimes I forget how lucky I am to have a job that I love. I get to work with amazing people and do meaningful work. I am grateful for the opportunities I have and the support of my team."
    },
    {
        "id": 3,
        "author": "warrior",
        "title": "Ad Astra per Aspera",
        "body": "Ad Astra per Aspera is a Latin phrase that means 'to the stars through difficulties.' It reminds me that even when things are tough, I can still reach for the stars and achieve my goals. I am grateful for the challenges I face because they make me stronger and more resilient."
    },
    {
        "id": 4,
        "author": "legend",
        "title": "My Private Diary",
        "body": "I hope no one reads this, but I wanted to write down my thoughts. I am grateful for my team and the work we do. I hope to continue growing and learning in my role. But I always forget my password, so I wrote it down here while encoding it into base64: " + base64.b64encode(ACCOUNTS["legend"]["password"].encode()).decode()
    },
]
def page(title, content):
    return f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{html.escape(title)} | Capture the Flag!</title>
  <style>
    :root {{ color-scheme: light; font-family: system-ui, sans-serif; color: #172033; background: #eef2f8; }}
    body {{ margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 24px; }}
    main {{ box-sizing: border-box; width: min(100%, 560px); padding: 42px; border: 1px solid #d9e0ec; border-radius: 18px; background: white; box-shadow: 0 18px 50px #18264018; }}
    h1 {{ margin: 0 0 12px; font-size: 2rem; }}
    p {{ color: #536078; line-height: 1.6; }}
    label {{ display: block; margin: 20px 0 8px; font-weight: 650; }}
    input {{ box-sizing: border-box; width: 100%; padding: 13px 14px; border: 1px solid #bdc8d8; border-radius: 8px; font: inherit; }}
    button {{ width: 100%; margin-top: 24px; padding: 13px; border: 0; border-radius: 8px; color: white; background: #315be8; font: inherit; font-weight: 700; cursor: pointer; }}
    .eyebrow {{ color: #315be8; font-size: .8rem; font-weight: 750; letter-spacing: .1em; text-transform: uppercase; }}
    .notice {{ margin-top: 20px; padding: 12px 14px; border-radius: 8px; background: #edf3ff; }}
    .error {{ color: #a12626; }}
    .code {{ font-size: 1.5rem; font-weight: 750; letter-spacing: .2em; color: #172033; }}
    a {{ color: #315be8; }}
  </style>
</head>
<body><main>{content}</main></body>
</html>"""


def login_page(error=None):
    message = (
        f'<p class="error" role="alert">{html.escape(error)}</p>' if error else ""
    )
    return page(
        "Sign in",
        '<p class="eyebrow">Capture the Flag!</p>'
        "<h1>Welcome!</h1>"
        "<p>Sign in to continue to your account."
        f"{message}"
        '<form method="post" action="/login">'
        '<label for="username">Username</label>'
        '<input id="username" name="username" autocomplete="username" required>'
        '<label for="password">Password</label>'
        '<input id="password" name="password" type="password" '
        'autocomplete="current-password" required>'
        '<button type="submit">Continue</button></form>',
    )


def issue_otp(username):
    code = f"{secrets.randbelow(10_000):04d}"
    otp_store[username] = (code, time.monotonic() + OTP_LIFETIME_SECONDS)
    return code


@app.before_request
def redirect_unauthenticated_users():
    public_endpoints = {"index", "login", "two_factor", "mailbox", "health"}
    if request.endpoint not in public_endpoints and "username" not in session:
        return redirect(url_for("login"))
    return None


@app.get("/")
def index():
    if request.user_agent.string.lower().startswith("curl/"):
        response = app.response_class(
            (
                f"{FLAG1}\n"
                f"Adventurer username: adventurer\n"
                f"Adventurer password: {ACCOUNTS['adventurer']['password']}\n"
            ),
            mimetype="text/plain",
        )
    else:
        response = app.response_class(
            page(
                "Welcome",
                "<h1>Welcome!</h1>"
                "<p>In order to start your adventure, you have to unlock this door.</p>"
                "<p>But there is another way of accessing this door.</p>"
                "<p>Try another way!</p>",
            ),
            mimetype="text/html",
        )
    response.headers["Vary"] = "User-Agent"
    return response


@app.route("/login", methods=["GET", "POST"])
def login():
    if "username" in session:
        return redirect(url_for("account"))
    if request.method == "GET":
        return login_page()

    username = request.form.get("username", "")
    password = request.form.get("password", "")
    account = ACCOUNTS.get(username)
    if not account or not account["password"] or not hmac.compare_digest(
        password, account["password"]
    ):
        return login_page("Those credentials were not recognized."), 401

    issue_otp(username)
    session["preauth_user"] = username
    return redirect(url_for("two_factor"))


@app.route("/2fa", methods=["GET", "POST"])
def two_factor():
    preauth_user = session.get("preauth_user")
    if preauth_user not in ACCOUNTS:
        return redirect(url_for("login"))

    entry = otp_store.get(preauth_user)
    if request.method == "GET":
        if entry is None or time.monotonic() >= entry[1]:
            return page(
                "Code expired",
                "<h1>Your security code expired</h1>"
                "<p>Sign in again to receive a new code.</p>"
                '<a href="/login">Return to sign in</a>',
            ), 401
        return page(
            "Two-factor verification",
            '<p class="eyebrow">Step 2 of 2</p>'
            "<h1>Check your email</h1>"
            "<p>Enter the four-digit security code from your inbox. "
            "It expires after one minute.</p>"
            '<form method="post" action="/2fa">'
            f'<input type="hidden" name="username" value="{html.escape(preauth_user)}">'
            '<label for="code">Security code</label>'
            '<input id="code" name="code" inputmode="numeric" pattern="[0-9]{4}" '
            'maxlength="4" autocomplete="one-time-code" required>'
            '<button type="submit">Verify</button></form>'
            '<p><a href="/mail">Open mock inbox</a></p>',
        )

    submitted_username = request.form.get("username", "")
    code = request.form.get("code", "")
    token = base64.b64encode(
        f"{submitted_username}:{code}".encode("ascii", errors="replace")
    ).decode("ascii")
    decoded_token = base64.b64decode(token, validate=True).decode("ascii")
    username, separator, token_code = decoded_token.partition(":")

    if (
        not separator
        or username not in ACCOUNTS
        or entry is None
        or time.monotonic() >= entry[1]
        or len(token_code) != 4
        or not token_code.isascii()
        or not token_code.isdigit()
        or not hmac.compare_digest(token_code, entry[0])
        or (
            username != preauth_user
            and not (
                ACCOUNTS[preauth_user]["role"] == "Guest"
                and ACCOUNTS[username]["role"] == "Employee"
            )
        )
    ):
        return (
            page(
                "Two-factor verification",
                '<p class="eyebrow">Step 2 of 2</p>'
                "<h1>Code not accepted</h1>"
                '<p class="error" role="alert">The code is invalid or expired. '
                "Return to the login page and try again.</p>"
                '<a href="/login">Return to sign in</a>',
            ),
            401,
        )

    # Deliberate flaw: validate the OTP against the first-factor session user,
    # but trust the username inside the client-editable token as the login user.
    otp_store.pop(preauth_user, None)
    session.clear()
    session["username"] = username
    return redirect(url_for("account"))


@app.get("/mail")
def mailbox():
    preauth_user = session.get("preauth_user")
    if preauth_user not in ACCOUNTS:
        return redirect(url_for("login"))

    entry = otp_store.get(preauth_user)
    if entry is None or time.monotonic() >= entry[1]:
        content = (
            '<p class="eyebrow">Mock inbox</p><h1>No active messages</h1>'
            "<p>Your security code has expired. Sign in again to receive a new one.</p>"
            '<a href="/login">Return to sign in</a>'
        )
    else:
        remaining_seconds = max(0, int(entry[1] - time.monotonic()))
        content = (
            '<p class="eyebrow">Mock inbox</p><h1>Security code</h1>'
            '<div class="notice"><p>Your verification code is:</p>'
            f'<span class="code">{html.escape(entry[0])}</span>'
            f"<p>This code expires in about {remaining_seconds} seconds.</p></div>"
            '<p><a href="/2fa">Return to verification</a></p>'
        )
    return page("Mock inbox", content)


@app.get("/account")
def account():
    username = session["username"]
    role = ACCOUNTS[username]["role"]
    if role == "Guest":
        content = (
            '<p class="eyebrow">Guest</p><h1>Guest test page</h1>'
            "<p>Nothing in here. Train more to being an Adventurer into a Hero!</p>"
        )
    else:
        content = (
            f'<p class="eyebrow">{html.escape(role)} dashboard</p>'
            f"<h1>Welcome, {html.escape(USERS[username]['name'])}</h1>"
            "<p>Internal staff portal</p>"
            '<nav><a href="/profile">My Profile</a> | '
            '<a href="/blog">Employee blog</a></nav>'
        )
        if role == "Employee":
            content += (
                f'<div class="notice"><strong>Flag 2:</strong> '
                f"{html.escape(FLAG2)}</div>"
            )
        elif role == "Administrator":
            content += (
                f'<div class="notice"><strong>Flag 3:</strong> '
                f"{html.escape(FLAG3)}</div>"
            )
    return page(
        "Account",
        f"{content}<p><a href=\"/logout\">Sign out</a></p>",
    )


@app.get("/blog")
def blog():
    current_role = ACCOUNTS[session["username"]]["role"]
    if current_role not in {"Employee", "Administrator"}:
        return redirect(url_for("account"))

    posts = []
    for post in BLOG_POSTS:
        author = post["author"]
        author_id = USERS[author]["id"]
        posts.append(
            '<article class="notice">'
            f'<h2><a href="{url_for("blog_post", post_id=post["id"])}">'
            f'{html.escape(post["title"])}</a></h2>'
            f"<p>By {html.escape(USERS[author]['name'])}</p>"
            f'<p>Author user ID: <code>{html.escape(author_id)}</code></p>'
            "</article>"
        )
    return page(
        "Employee blog",
        '<p class="eyebrow">Staff portal</p><h1>Employee blog</h1>'
        + "".join(posts)
        + '<p><a href="/account">Back to dashboard</a></p>',
    )


@app.get("/blog/<int:post_id>")
def blog_post(post_id):
    username = session["username"]
    if ACCOUNTS[username]["role"] not in {"Employee", "Administrator"}:
        return redirect(url_for("account"))

    post = next((post for post in BLOG_POSTS if post["id"] == post_id), None)
    if post is None:
        return "Post not found", 404

    author = post["author"]
    content = (
        f'<p class="eyebrow">By {html.escape(USERS[author]["name"])}</p>'
        f"<h1>{html.escape(post['title'])}</h1>"
        f"<p>{html.escape(post['body'])}</p>"
        f'<p>Author user ID: <code>{html.escape(USERS[author]["id"])}</code></p>'
    )

    return page(
        "Employee blog post",
        content + '<p><a href="/blog">Back to employee blog</a></p>',
    )


def get_user_profile(user_id):
    if ACCOUNTS[session["username"]]["role"] not in {
        "Employee",
        "Administrator",
    }:
        return {"error": "Staff access required"}, 403

    try:
        normalized_id = str(uuid.UUID(user_id))
    except ValueError:
        return {"error": "User ID must be a GUID"}, 400

    username = next(
        (name for name, user in USERS.items() if user["id"] == normalized_id),
        None,
    )
    if username is None:
        return {"error": "User not found"}, 404

    user = USERS[username]
    return {
        "id": user["id"],
        "username": username,
        "name": user["name"],
        "role": ACCOUNTS[username]["role"],
        "email": user["email"],
        "department": user["department"],
    }, 200


@app.get("/profile")
def profile():
    username = session["username"]
    profile_id = request.args.get("id", USERS[username]["id"])
    data, status = get_user_profile(profile_id)
    content = (
        '<p class="eyebrow">Staff portal</p><h1>My Profile</h1>'
        '<p>Profile details are shown as JSON. The profile ID is editable.</p>'
        '<form method="get" action="/profile">'
        '<label for="user-id">User ID (GUID)</label>'
        f'<input id="user-id" name="id" value="{html.escape(profile_id)}" required>'
        '<button type="submit">View profile</button></form>'
    )
    if status == 200:
        profile_json = json.dumps(data, indent=2)
        content += f"<pre class=\"notice\">{html.escape(profile_json)}</pre>"
    else:
        content += (
            f'<p class="error" role="alert">{html.escape(data["error"])}</p>'
        )
    return page(
        "My Profile",
        content + '<p><a href="/account">Back to dashboard</a></p>',
    ), status


@app.get("/api/users")
def user_profile_by_query():
    data, status = get_user_profile(request.args.get("id", ""))
    return jsonify(data), status


@app.get("/api/users/<user_id>")
def user_profile_by_id(user_id):
    data, status = get_user_profile(user_id)
    return jsonify(data), status


@app.get("/logout")
def logout():
    session.clear()
    return redirect(url_for("login"))


@app.get("/health")
def health():
    return {"status": "ok"}
