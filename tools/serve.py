"""Static file server with correct MIME types for .mjs/.wasm.

Usage: python tools/serve.py <directory> [port]
"""
import http.server
import mimetypes
import os
import sys

mimetypes.add_type("text/javascript", ".mjs")
mimetypes.add_type("text/javascript", ".js")
mimetypes.add_type("application/wasm", ".wasm")
mimetypes.add_type("application/json", ".json")


class Handler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


if __name__ == "__main__":
    directory = sys.argv[1] if len(sys.argv) > 1 else "."
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 8765
    os.chdir(directory)
    http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
