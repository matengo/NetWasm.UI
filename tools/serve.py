"""Static file server with correct MIME types for .mjs/.wasm and on-the-fly compression.

Compresses text/wasm responses with Brotli (if the `brotli` package is installed) or gzip,
so DevTools "transferred" sizes mirror a production server.

Usage: python tools/serve.py <directory> [port]
"""
import gzip
import http.server
import io
import mimetypes
import os
import sys

try:
    import brotli
except ImportError:  # pragma: no cover
    brotli = None

mimetypes.add_type("text/javascript", ".mjs")
mimetypes.add_type("text/javascript", ".js")
mimetypes.add_type("application/wasm", ".wasm")
mimetypes.add_type("application/json", ".json")

COMPRESSIBLE = (".mjs", ".js", ".wasm", ".json", ".html", ".css", ".mts")


class Handler(http.server.SimpleHTTPRequestHandler):
    def do_GET(self):
        path = self.translate_path(self.path)
        if os.path.isdir(path) or not path.endswith(COMPRESSIBLE) or not os.path.isfile(path):
            return super().do_GET()

        accept = self.headers.get("Accept-Encoding", "")
        with open(path, "rb") as f:
            raw = f.read()

        if brotli and "br" in accept:
            body, encoding = brotli.compress(raw, quality=11), "br"
        elif "gzip" in accept:
            body, encoding = gzip.compress(raw, 9), "gzip"
        else:
            body, encoding = raw, None

        self.send_response(200)
        self.send_header("Content-Type", self.guess_type(path))
        self.send_header("Content-Length", str(len(body)))
        if encoding:
            self.send_header("Content-Encoding", encoding)
            self.send_header("Vary", "Accept-Encoding")
        self.end_headers()
        self.wfile.write(body)

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


if __name__ == "__main__":
    directory = sys.argv[1] if len(sys.argv) > 1 else "."
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 8765
    os.chdir(directory)
    print(f"Serving {os.getcwd()} on http://localhost:{port} "
          f"({'brotli' if brotli else 'gzip'} compression)")
    http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
