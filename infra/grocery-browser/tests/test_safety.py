"""Unit tests for the safety interlocks (spec 11.4) — enforce R1.

These run without a real browser: the guards are pure functions.
"""

from __future__ import annotations

import pytest

from app.safety import (
    SafetyViolation,
    assert_action_allowed,
    assert_url_allowed,
    is_action_allowed,
    is_url_allowed,
)

ALLOWED_URLS = [
    "https://www.sainsburys.co.uk/gol-ui/groceries",
    "https://www.sainsburys.co.uk/gol-ui/product/sainsburys-british-semi-skimmed-milk",
    "https://www.sainsburys.co.uk/gol-ui/SearchResults?searchTerm=milk",
    "https://www.sainsburys.co.uk/gol-ui/trolley",
]

DENIED_URLS = [
    "https://www.sainsburys.co.uk/gol-ui/checkout",
    "https://www.sainsburys.co.uk/gol-ui/checkout/payment",
    "https://www.sainsburys.co.uk/gol-ui/delivery-slot",
    "https://www.sainsburys.co.uk/gol-ui/book-delivery",
    "https://www.sainsburys.co.uk/gol-ui/slot/book",
    "https://www.sainsburys.co.uk/gol-ui/place-order",
    "https://www.sainsburys.co.uk/gol-ui/order-confirmation",
    "https://www.sainsburys.co.uk/payment/pay",
]


@pytest.mark.parametrize("url", ALLOWED_URLS)
def test_allowed_urls_pass(url: str) -> None:
    assert is_url_allowed(url) is True
    assert_url_allowed(url)  # does not raise


@pytest.mark.parametrize("url", DENIED_URLS)
def test_denied_urls_are_refused(url: str) -> None:
    assert is_url_allowed(url) is False
    with pytest.raises(SafetyViolation):
        assert_url_allowed(url)


def test_empty_url_is_refused() -> None:
    assert is_url_allowed("") is False
    with pytest.raises(SafetyViolation):
        assert_url_allowed("")


@pytest.mark.parametrize(
    "action",
    [
        "search",
        "open_product",
        "set_quantity",
        "add_to_basket",
        "read_basket",
        "read_order_history",
        "screenshot",
        "login",
    ],
)
def test_allowlisted_actions_pass(action: str) -> None:
    assert is_action_allowed(action) is True
    assert_action_allowed(action)  # does not raise


@pytest.mark.parametrize(
    "action",
    ["proceed_to_checkout", "book_slot", "pay", "place_order", "confirm_order", "anything_else"],
)
def test_non_allowlisted_actions_are_refused(action: str) -> None:
    assert is_action_allowed(action) is False
    with pytest.raises(SafetyViolation):
        assert_action_allowed(action)
