"""Typed request/response models for the grocery-browser RPC surface (spec 11.3).

Phase 1 scaffold: these mirror the .NET ``IBrowserWorkerClient`` DTOs so the two
sides agree on the wire shape. Endpoints return typed placeholder data.
"""

from __future__ import annotations

from pydantic import BaseModel


class LoginResult(BaseModel):
    authenticated: bool
    message: str | None = None


class SearchRequest(BaseModel):
    term: str


class ProductCandidate(BaseModel):
    productId: str
    name: str
    price: float
    nectarPrice: float | None = None
    isFavourite: bool = False
    inStock: bool = True
    url: str
    imgRef: str | None = None


class SearchResult(BaseModel):
    term: str
    candidates: list[ProductCandidate] = []


class AddToBasketRequest(BaseModel):
    productId: str
    quantity: float


class BasketLine(BaseModel):
    productId: str
    productName: str
    quantity: float
    price: float
    isNectarPrice: bool = False
    isFavourite: bool = False
    substitutionReason: str | None = None


class BasketSnapshot(BaseModel):
    lines: list[BasketLine] = []
    subtotal: float = 0.0


class OrderHistoryEntry(BaseModel):
    orderId: str
    placedAt: str
    total: float


class OrderHistoryResult(BaseModel):
    orders: list[OrderHistoryEntry] = []


class ScreenshotResult(BaseModel):
    """Placeholder: a reference to a captured PNG (path or id), not the bytes."""

    region: str | None = None
    imgRef: str
