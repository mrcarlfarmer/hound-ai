"""Runtime configuration for the grocery-browser sidecar (spec 11).

Two concerns live here:

* :func:`load_selectors` — reads the externalised ``selectors.json`` so DOM
  selectors are never hardcoded in driver code (the Sainsbury's site is
  mid-migration and selectors churn; R1 danger lists also live there).
* :class:`Settings` — runtime knobs sourced from the environment: Sainsbury's
  credentials (from the git-ignored ``secrets.grocery.json`` / env), the
  persisted Chrome profile directory, headless mode and the ``DRY_RUN`` kill
  switch (spec 11.4) that lets the stack run end-to-end without mutating the
  live basket.
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from functools import lru_cache
from pathlib import Path
from typing import Any

_SELECTORS_PATH = Path(__file__).with_name("selectors.json")


@lru_cache(maxsize=1)
def load_selectors(path: str | None = None) -> dict[str, Any]:
    """Load and cache the externalised selectors map."""
    target = Path(path) if path else _SELECTORS_PATH
    with target.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def danger_testids(selectors: dict[str, Any] | None = None) -> tuple[str, ...]:
    """Return the R1 denylisted testids from the selectors config."""
    selectors = selectors or load_selectors()
    return tuple(selectors.get("danger", {}).get("denyTestIds", ()))


def danger_url_patterns(selectors: dict[str, Any] | None = None) -> tuple[str, ...]:
    """Return the R1 denylisted URL substrings from the selectors config."""
    selectors = selectors or load_selectors()
    return tuple(selectors.get("danger", {}).get("denyUrlPatterns", ()))


def _read_secret(name: str, *file_keys: str) -> str | None:
    """Resolve a secret from an env var, falling back to secrets.grocery.json."""
    value = os.environ.get(name)
    if value:
        return value
    secrets_path = os.environ.get("GROCERY_SECRETS_PATH", "/run/secrets/secrets.grocery.json")
    try:
        with open(secrets_path, "r", encoding="utf-8") as handle:
            data = json.load(handle)
    except (OSError, json.JSONDecodeError):
        return None
    node: Any = data
    for key in file_keys:
        if not isinstance(node, dict):
            return None
        node = node.get(key)
    return node if isinstance(node, str) and node else None


@dataclass(frozen=True)
class Settings:
    """Sidecar runtime settings (creds never logged)."""

    username: str | None = None
    password: str | None = None
    profile_dir: str = "/data/grocery/chrome-profile"
    headless: bool = True
    dry_run: bool = True
    nav_min_delay_ms: int = 350
    nav_max_delay_ms: int = 1200

    @property
    def has_credentials(self) -> bool:
        return bool(self.username and self.password)


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    """Build settings from the environment (cached for the process)."""
    return Settings(
        username=_read_secret("SAINSBURYS_USERNAME", "sainsburys", "username"),
        password=_read_secret("SAINSBURYS_PASSWORD", "sainsburys", "password"),
        profile_dir=os.environ.get("GROCERY_PROFILE_DIR", "/data/grocery/chrome-profile"),
        headless=os.environ.get("GROCERY_HEADLESS", "true").lower() != "false",
        dry_run=os.environ.get("GROCERY_DRY_RUN", "true").lower() != "false",
    )
