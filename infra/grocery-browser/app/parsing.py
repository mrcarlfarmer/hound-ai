"""Pure parsing helpers for the grocery-browser sidecar.

The :mod:`app.browser` driver extracts raw strings/flags from each
``gw-product-card`` (or ``trolley-item``) via the CDP DOM; these helpers turn
that raw extraction into typed values. Keeping the transformation pure means the
ranking-relevant logic (price parsing, Nectar flag, favourite flag, product id)
is unit-tested with synthetic data — no Chrome required.
"""

from __future__ import annotations

import re

from app.models import BasketLine, ProductCandidate

_PRICE_RE = re.compile(r"-?\d+(?:\.\d+)?")


def parse_price(text: str | None) -> float | None:
    """Parse a money string like ``"£1.30"`` or ``"Now £2"`` into a float.

    Returns ``None`` when no numeric amount is present (e.g. empty/"Add").
    """
    if not text:
        return None
    match = _PRICE_RE.search(text.replace(",", ""))
    if not match:
        return None
    try:
        return float(match.group())
    except ValueError:  # pragma: no cover - regex guarantees a number
        return None


def product_id_from_href(href: str | None) -> str | None:
    """Extract the product slug (used as the id) from a product link href.

    ``/groceries/product/sainsburys-british-semi-skimmed-milk`` ->
    ``sainsburys-british-semi-skimmed-milk``. Query strings are stripped.
    """
    if not href:
        return None
    path = href.split("?", 1)[0].rstrip("/")
    marker = "/product/"
    idx = path.find(marker)
    if idx == -1:
        return None
    slug = path[idx + len(marker):]
    return slug or None


def build_candidate(card: dict) -> ProductCandidate | None:
    """Build a :class:`ProductCandidate` from a raw extracted card dict.

    Expected keys (all optional except a resolvable id+name):
    ``name``, ``href``, ``retailPriceText``, ``nectarPresent`` (bool),
    ``nectarPriceText``, ``favouriteFull`` (bool), ``perUnitText``,
    ``addPresent`` (bool — the "Add" button shows only for in-stock items),
    ``imgRef``.
    """
    name = (card.get("name") or "").strip()
    href = card.get("href")
    product_id = card.get("productId") or product_id_from_href(href)
    if not product_id or not name:
        return None

    retail = parse_price(card.get("retailPriceText"))
    nectar_present = bool(card.get("nectarPresent"))
    nectar_price = parse_price(card.get("nectarPriceText")) if nectar_present else None
    # An item exposing a Nectar badge but no separate price still flags loyalty.
    in_stock = bool(card.get("addPresent", True))

    return ProductCandidate(
        productId=product_id,
        name=name,
        price=retail if retail is not None else 0.0,
        nectarPrice=nectar_price,
        isNectarPrice=nectar_present,
        isFavourite=bool(card.get("favouriteFull")),
        inStock=in_stock,
        perUnitPrice=(card.get("perUnitText") or None),
        url=(href or ""),
        imgRef=(card.get("imgRef") or None),
    )


def build_basket_line(line: dict) -> BasketLine | None:
    """Build a :class:`BasketLine` from a raw extracted trolley-item dict.

    Expected keys: ``productId``, ``productName``, ``quantityText``,
    ``unitPriceText``/``subtotalText``, ``offer`` (bool).
    """
    name = (line.get("productName") or "").strip()
    product_id = line.get("productId") or ""
    if not name:
        return None

    quantity = _parse_quantity(line.get("quantityText"))
    price = parse_price(line.get("subtotalText"))
    if price is None:
        price = parse_price(line.get("unitPriceText")) or 0.0

    return BasketLine(
        productId=product_id,
        productName=name,
        quantity=quantity,
        price=price,
        isNectarPrice=bool(line.get("offer")),
        isFavourite=bool(line.get("favourite")),
        substitutionReason=line.get("substitutionReason"),
    )


_QTY_RE = re.compile(r"\d+(?:\.\d+)?")


def _parse_quantity(text: str | None) -> float:
    """Parse a trolley quantity (e.g. aria ``"2 Milk in trolley..."``) -> float."""
    if not text:
        return 1.0
    match = _QTY_RE.search(text)
    return float(match.group()) if match else 1.0
