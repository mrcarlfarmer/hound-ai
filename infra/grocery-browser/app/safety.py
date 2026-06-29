"""Safety interlocks for the grocery-browser sidecar (spec 11.4).

These enforce hard requirement **R1**: the system MUST NEVER select a delivery
slot and MUST NEVER check out or pay. Three independent guards are provided:

* A **URL denylist** — refuses navigation to any path that looks like a
  checkout / payment / slot / book-delivery route.
* A **testid denylist** — refuses to click any element whose ``data-testid``
  is a known "book slot" / "book delivery" control (from the Phase 6 recon).
* An **action allowlist** — only a small set of read/basket-building actions is
  permitted; "proceed to checkout", "book slot" and "pay" are never allowed.

The danger lists are sourced from the externalised ``selectors.json`` (so they
track the live site), with a hardcoded fallback baked in here so the guard can
never be silently disabled by a missing/corrupt config.
"""

from __future__ import annotations

from urllib.parse import urlparse

from app.config import danger_testids, danger_url_patterns

# Hardcoded fallback so the guard survives a missing/corrupt selectors.json.
_FALLBACK_URL_DENYLIST: tuple[str, ...] = (
    "checkout",
    "payment",
    "pay",
    "slot",
    "book-delivery",
    "bookdelivery",
    "delivery-slot",
    "deliveryslot",
    "book-slot",
    "slot-booking",
    "place-order",
    "placeorder",
    "order-confirmation",
    "complete-order",
)

_FALLBACK_DANGER_TESTIDS: tuple[str, ...] = (
    "gw-book-slot",
    "order-summary-book-slot-button",
    "book-delivery-button",
    "book-delivery",
)


def _url_denylist() -> tuple[str, ...]:
    """Merge config-driven denylist with the hardcoded fallback (lowercased)."""
    merged = set(_FALLBACK_URL_DENYLIST)
    try:
        merged.update(danger_url_patterns())
    except Exception:  # pragma: no cover - defensive: never disable the guard
        pass
    return tuple(token.lower() for token in merged if token)


def _danger_testids() -> tuple[str, ...]:
    """Merge config-driven danger testids with the hardcoded fallback."""
    merged = set(_FALLBACK_DANGER_TESTIDS)
    try:
        merged.update(danger_testids())
    except Exception:  # pragma: no cover - defensive: never disable the guard
        pass
    return tuple(token.lower() for token in merged if token)


# Snapshot at import for the module-level constants other code/tests reference.
URL_DENYLIST: tuple[str, ...] = _url_denylist()
DANGER_TESTIDS: tuple[str, ...] = _danger_testids()

# The only browser actions the sidecar is permitted to perform. Anything else
# (notably "proceed to checkout", "book slot", "pay") is rejected.
ACTION_ALLOWLIST: frozenset[str] = frozenset(
    {
        "search",
        "open_product",
        "set_quantity",
        "add_to_basket",
        "read_basket",
        "read_order_history",
        "read_favourites",
        "screenshot",
        "login",
    }
)


class SafetyViolation(Exception):
    """Raised when a navigation, click or action would breach the R1 safety rule."""


def is_url_allowed(url: str) -> bool:
    """Return ``True`` if the URL does not match any denylisted route."""
    if not url:
        return False
    parsed = urlparse(url)
    haystack = f"{parsed.path}?{parsed.query}".lower()
    return not any(token in haystack for token in URL_DENYLIST)


def assert_url_allowed(url: str) -> None:
    """Raise :class:`SafetyViolation` if the URL is denylisted."""
    if not is_url_allowed(url):
        raise SafetyViolation(
            f"Refusing to navigate to '{url}': matches checkout/slot/payment denylist."
        )


def is_testid_allowed(testid: str) -> bool:
    """Return ``True`` if the testid is NOT a denylisted slot/checkout control."""
    if not testid:
        return True
    return testid.strip().lower() not in DANGER_TESTIDS


def assert_testid_allowed(testid: str) -> None:
    """Raise :class:`SafetyViolation` if the testid is a slot/checkout control."""
    if not is_testid_allowed(testid):
        raise SafetyViolation(
            f"Refusing to click '{testid}': denylisted slot/checkout control (R1)."
        )


def is_action_allowed(action: str) -> bool:
    """Return ``True`` if the action is on the allowlist."""
    return action in ACTION_ALLOWLIST


def assert_action_allowed(action: str) -> None:
    """Raise :class:`SafetyViolation` if the action is not allowlisted."""
    if not is_action_allowed(action):
        raise SafetyViolation(
            f"Refusing action '{action}': not in the grocery-browser action allowlist."
        )
