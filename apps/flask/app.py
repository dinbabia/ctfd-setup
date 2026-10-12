from flask import Flask, request

app = Flask(__name__)


@app.get("/")
def index():
    return (
        "<!doctype html><html><body><h1>Flask CTF starter</h1>"
        '<form action="/search"><input name="q" placeholder="Search">'
        "<button>Search</button></form></body></html>"
    )


@app.get("/search")
def search():
    query = request.args.get("q", "")
    return (
        "<!doctype html><html><body><h1>Search results</h1>"
        f"<p>You searched for: {query}</p><a href=\"/\">Back</a></body></html>"
    )


@app.get("/health")
def health():
    return {"status": "ok"}
