"""Safety interlocks for the grocery-browser sidecar (spec 11.4).

These enforce hard requirement R1: the system MUST NEVER select a delivery slot
and MUST NEVER check out or pay. Two independent guards are provided:

* A **URL denylist** — refuses navigation to any path that looks like a
  checkout / payment / slot / book-delivery route.
* An **action allowlist** — only a small set of read/basket-building actions is
  permitted; "proceed to checkout", "book slot" and "pay" are never allowed.

Both guards are pure functions so they are trivially unit-testable without a
real browser.
"""

from __future__ import annotations

from urllib.parse import urlparse

# Substrings that, if present anywhere in a URL path/query, indicate a
# checkout / payment / delivery-slot route. Matching is case-insensitive.
URL_DENYLIST: tuple[str, ...] = (
    "checkout",
    "payment",
    "pay",
    "slot",
    "book-delivery",
    "bookdelivery",
    "delivery-slot",
    "deliveryslot",
    "place-order",
    "placeorder",
    "order-confirmation",
    "complete-order",
)

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
        "screenshot",
        "login",
    }
)


class SafetyViolation(Exception):
    """Raised when a navigation or action would breach the R1 safety rule."""


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


def is_action_allowed(action: str) -> bool:
    """Return ``True`` if the action is on the allowlist."""
    return action in ACTION_ALLOWLIST


def assert_action_allowed(action: str) -> None:
    """Raise :class:`SafetyViolation` if the action is not allowlisted."""
    if not is_action_allowed(action):
        raise SafetyViolation(
            f"Refusing action '{action}': not in the grocery-browser action allowlist."
        )
