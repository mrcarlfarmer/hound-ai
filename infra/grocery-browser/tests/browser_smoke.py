"""Explicit real-driver smoke: python -m pytest tests/browser_smoke.py.

Uses only a loopback fixture and an ephemeral browser profile. Kept outside the
test_*.py suite so fast unit tests do not require a browser installation.
"""

from __future__ import annotations

import asyncio
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from threading import Thread

from app.browser import BrowserSession
from app.config import Settings
from app.safety import SafetyViolation


def test_real_browser_lifecycle_and_guards(tmp_path: Path) -> None:
    page = tmp_path / "index.html"
    page.write_text(
        '<!doctype html><title>Driver smoke</title>'
        '<button data-testid="consent">Accept</button>'
        '<h1 data-testid="fixture">Local driver fixture</h1>',
        encoding="utf-8",
    )
    handler = partial(SimpleHTTPRequestHandler, directory=str(tmp_path))
    server = ThreadingHTTPServer(("127.0.0.1", 0), handler)
    thread = Thread(target=server.serve_forever, daemon=True)
    thread.start()

    async def exercise() -> None:
        session = BrowserSession(
            settings=Settings(
                profile_dir=str(tmp_path / "profile"),
                headless=True,
                dry_run=True,
                nav_min_delay_ms=0,
                nav_max_delay_ms=0,
            ),
            selectors={"consent": {"acceptButton": "consent"}},
        )
        try:
            await session.start()
            await session._navigate(f"http://127.0.0.1:{server.server_port}/index.html")
            element = await session._wait_ready('[data-testid="fixture"]', timeout=10)
            assert element.text == "Local driver fixture"
            for testid in ("gw-book-slot", "order-summary-book-slot-button"):
                try:
                    await session._safe_click(element, testid)
                except SafetyViolation:
                    pass
                else:
                    raise AssertionError(f"Forbidden click allowed: {testid}")
            try:
                await session._navigate(f"http://127.0.0.1:{server.server_port}/checkout")
            except SafetyViolation:
                pass
            else:
                raise AssertionError("Checkout navigation allowed")
            screenshot = tmp_path / "smoke.png"
            await session._tab.save_screenshot(str(screenshot), format="png")
            assert screenshot.read_bytes().startswith(b"\x89PNG\r\n\x1a\n")
        finally:
            await session.close()
        assert session._browser is None
        assert session._tab is None

    try:
        asyncio.run(asyncio.wait_for(exercise(), timeout=60))
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=5)
