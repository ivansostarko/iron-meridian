#!/usr/bin/env python3
"""Serve a Unity WebGL build locally, with the headers it actually needs.

    python scripts/serve-web.py [root] [port]

Python's own ``http.server`` is nearly enough, and wrong in one way that matters:
a Brotli-compressed Unity build ships ``*.br`` files that the browser must be
told are compressed. Without a ``Content-Encoding`` header the browser hands the
raw Brotli stream to the WASM loader, which fails with a message about an invalid
magic number and nothing about compression at all.

So this is ``SimpleHTTPRequestHandler`` plus:

* ``Content-Encoding`` for ``.br`` and ``.gz``, and the underlying type
  underneath it, so ``build.wasm.br`` is served as WASM-that-is-Brotli rather
  than as an unknown binary;
* ``application/wasm`` for ``.wasm``, which older Python versions do not know;
* no-cache, because the whole point of running this is to look at a build you
  have just replaced.

Not a production server. See docs/41-WEB.md for what a real host needs.
"""

import functools
import http.server
import os
import socketserver
import sys

# What is underneath a compressed file, keyed by the extension left when the
# compression suffix is taken off.
CONTENT_TYPES = {
    ".wasm": "application/wasm",
    ".js": "application/javascript",
    ".json": "application/json",
    ".data": "application/octet-stream",
    ".symbols": "application/octet-stream",
}

ENCODINGS = {".br": "br", ".gz": "gzip"}


class UnityHandler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        path = self.translate_path(self.path)
        _, ext = os.path.splitext(path)

        encoding = ENCODINGS.get(ext)
        if encoding:
            self.send_header("Content-Encoding", encoding)

        # A build you are actively rebuilding must not be cached, or the tab
        # shows the previous one and the difference looks like a bug in the game.
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def guess_type(self, path):
        base, ext = os.path.splitext(path)
        if ext in ENCODINGS:
            # build.wasm.br -> the type of build.wasm; the encoding header above
            # says how it is wrapped.
            _, inner = os.path.splitext(base)
            return CONTENT_TYPES.get(inner, "application/octet-stream")
        return CONTENT_TYPES.get(ext) or super().guess_type(path)


def main() -> int:
    root = sys.argv[1] if len(sys.argv) > 1 else "Builds/Web"
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 8080

    if not os.path.isfile(os.path.join(root, "index.html")):
        print(f"error: no index.html in {root} - build first", file=sys.stderr)
        return 1

    handler = functools.partial(UnityHandler, directory=root)
    socketserver.TCPServer.allow_reuse_address = True

    with socketserver.TCPServer(("", port), handler) as httpd:
        print(f"serving {os.path.abspath(root)} on http://localhost:{port}")
        print("Ctrl+C to stop")
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nstopped")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
