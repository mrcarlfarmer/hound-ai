"""Unit tests for the pure DOM-parsing helpers (no browser required)."""

from __future__ import annotations

import pytest

from app.parsing import (
    build_basket_line,
    build_candidate,
    parse_price,
    product_id_from_href,
)


@pytest.mark.parametrize(
    "text,expected",
    [
        ("£1.30", 1.30),
        ("Now £2", 2.0),
        ("£1.30 / ltr", 1.30),
        ("£12.50", 12.50),
        ("£1,234.50", 1234.50),
        ("Add", None),
        ("", None),
        (None, None),
    ],
)
def test_parse_price(text, expected) -> None:
    assert parse_price(text) == expected


@pytest.mark.parametrize(
    "href,expected",
    [
        ("/groceries/product/sainsburys-milk-1l", "sainsburys-milk-1l"),
        ("/groceries/product/sainsburys-milk-1l?foo=bar", "sainsburys-milk-1l"),
        ("https://www.sainsburys.co.uk/groceries/product/abc-123", "abc-123"),
        ("/groceries/search?searchTerm=milk", None),
        (None, None),
    ],
)
def test_product_id_from_href(href, expected) -> None:
    assert product_id_from_href(href) == expected


def test_build_candidate_full() -> None:
    card = {
        "name": "Sainsbury's British Semi Skimmed Milk 2.27L",
        "href": "/groceries/product/sainsburys-milk-2-27l",
        "retailPriceText": "£1.45",
        "nectarPresent": True,
        "nectarPriceText": "£1.25",
        "favouriteFull": True,
        "perUnitText": "55p / ltr",
        "addPresent": True,
    }
    c = build_candidate(card)
    assert c is not None
    assert c.productId == "sainsburys-milk-2-27l"
    assert c.price == 1.45
    assert c.nectarPrice == 1.25
    assert c.isNectarPrice is True
    assert c.isFavourite is True
    assert c.inStock is True
    assert c.perUnitPrice == "55p / ltr"


def test_build_candidate_out_of_stock_no_add_button() -> None:
    card = {
        "name": "Out Of Stock Item",
        "href": "/groceries/product/oos-item",
        "retailPriceText": "£3.00",
        "addPresent": False,
    }
    c = build_candidate(card)
    assert c is not None
    assert c.inStock is False
    assert c.isNectarPrice is False
    assert c.nectarPrice is None


def test_build_candidate_requires_id_and_name() -> None:
    assert build_candidate({"name": "No link", "href": None}) is None
    assert build_candidate({"name": "", "href": "/groceries/product/x"}) is None


def test_build_basket_line() -> None:
    line = build_basket_line(
        {
            "productName": "Sainsbury's Milk",
            "quantityText": "2 Sainsbury's Milk in trolley. Update quantity",
            "unitPriceText": "£1.45",
            "subtotalText": "£2.90",
            "offer": True,
        }
    )
    assert line is not None
    assert line.quantity == 2.0
    assert line.price == 2.90
    assert line.isNectarPrice is True


def test_build_basket_line_requires_name() -> None:
    assert build_basket_line({"productName": "", "subtotalText": "£1"}) is None
