"""Regression tests for the zendriver screenshot format contract."""

import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from app.browser import BrowserSession


def test_screenshot_explicitly_requests_png() -> None:
    session = BrowserSession()
    session._tab = AsyncMock()
    with patch("app.browser.Path.mkdir") as mkdir:
        path = asyncio.run(session.screenshot())
    assert path == "/data/grocery/screenshots/page.png"
    mkdir.assert_called_once_with(parents=True, exist_ok=True)
    session._tab.save_screenshot.assert_awaited_once_with(path, format="png")


def test_screenshot_failure_is_not_reported_as_success() -> None:
    session = BrowserSession()
    session._tab = AsyncMock()
    session._tab.save_screenshot.side_effect = OSError("capture failed")
    with patch("app.browser.Path.mkdir"), pytest.raises(OSError, match="capture failed"):
        asyncio.run(session.screenshot())
