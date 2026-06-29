"""Endpoint tests for the FastAPI RPC surface (spec 11.3).

The browser is replaced with a :class:`FakeSession` via FastAPI dependency
overrides, so the RPC layer (and the R1 403 mapping) is exercised without
launching Chrome.
"""

from __future__ import annotations

import app.main as main
from app.browser import BrowserSession
from app.config import Settings
from app.main import _safety_violation_handler, app, get_session, navigate
from app.models import (
    BasketLine,
    BasketSnapshot,
    LoginResult,
    ProductCandidate,
)
from app.safety import SafetyViolation
from fastapi.testclient import TestClient


class FakeSession:
    """In-memory stand-in for BrowserSession — records calls, returns fixtures."""

    def __init__(self) -> None:
        self.added: list[tuple[str, float]] = []

    async def ensure_login(self) -> LoginResult:
        return LoginResult(authenticated=True, message="fake")

    async def search(self, term: str) -> list[ProductCandidate]:
        return [
            ProductCandidate(
                productId="sainsburys-milk-1l",
                name=f"{term} product",
                price=1.45,
                nectarPrice=1.25,
                isNectarPrice=True,
                isFavourite=True,
                inStock=True,
                url="/groceries/product/sainsburys-milk-1l",
            )
        ]

    async def add_to_basket(self, product_id: str, quantity: float) -> BasketSnapshot:
        self.added.append((product_id, quantity))
        return BasketSnapshot(
            lines=[BasketLine(productId=product_id, productName="X", quantity=quantity, price=1.45)],
            subtotal=1.45,
        )

    async def set_quantity(self, product_id: str, quantity: float) -> BasketSnapshot:
        return await self.add_to_basket(product_id, quantity)

    async def read_basket(self) -> BasketSnapshot:
        return BasketSnapshot(lines=[], subtotal=0.0)

    async def read_favourites(self) -> list[ProductCandidate]:
        return await self.search("fav")

    async def screenshot(self, region: str | None = None) -> str:
        return f"/data/grocery/screenshots/{region or 'page'}.png"


def _client(session: FakeSession, *, dry_run: bool = False) -> TestClient:
    app.dependency_overrides[get_session] = lambda: session
    main.get_settings = lambda: Settings(dry_run=dry_run)  # type: ignore[assignment]
    return TestClient(app)


def _reset() -> None:
    app.dependency_overrides.clear()


def test_health_ok() -> None:
    client = TestClient(app)
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["status"] == "ok"


def test_login_uses_session() -> None:
    client = _client(FakeSession())
    try:
        response = client.post("/login")
        assert response.status_code == 200
        assert response.json()["authenticated"] is True
    finally:
        _reset()


def test_search_returns_candidates() -> None:
    client = _client(FakeSession())
    try:
        response = client.post("/search", json={"term": "milk"})
        assert response.status_code == 200
        body = response.json()
        assert body["term"] == "milk"
        assert body["candidates"][0]["isNectarPrice"] is True
        assert body["candidates"][0]["isFavourite"] is True
    finally:
        _reset()


def test_add_mutates_when_not_dry_run() -> None:
    session = FakeSession()
    client = _client(session, dry_run=False)
    try:
        response = client.post("/add", json={"productId": "p1", "quantity": 2})
        assert response.status_code == 200
        assert session.added == [("p1", 2.0)]
    finally:
        _reset()


def test_add_does_not_mutate_in_dry_run() -> None:
    session = FakeSession()
    client = _client(session, dry_run=True)
    try:
        response = client.post("/add", json={"productId": "p1", "quantity": 2})
        assert response.status_code == 200
        assert session.added == []  # kill switch: live basket untouched
        assert response.json()["subtotal"] == 0.0
    finally:
        _reset()


def test_favourites_returns_candidates() -> None:
    client = _client(FakeSession())
    try:
        response = client.get("/favourites")
        assert response.status_code == 200
        assert len(response.json()["candidates"]) == 1
    finally:
        _reset()


def test_order_history_returns_empty() -> None:
    client = _client(FakeSession())
    try:
        response = client.get("/order-history")
        assert response.status_code == 200
        assert response.json()["orders"] == []
    finally:
        _reset()


class _ExplodingSession(FakeSession):
    async def search(self, term: str) -> list[ProductCandidate]:
        # Simulate the driver tripping the R1 URL guard mid-operation.
        raise SafetyViolation("refusing checkout navigation")


def test_safety_violation_from_endpoint_maps_to_403() -> None:
    client = _client(_ExplodingSession())
    try:
        response = client.post("/search", json={"term": "milk"})
        assert response.status_code == 403
        assert "refusing" in response.json()["detail"].lower()
    finally:
        _reset()


def test_navigate_blocks_checkout_route() -> None:
    try:
        navigate("https://www.sainsburys.co.uk/gol-ui/checkout")
        raise AssertionError("navigate should have refused a checkout URL")
    except SafetyViolation:
        pass


def test_safety_violation_maps_to_403_direct() -> None:
    response = _safety_violation_handler(None, SafetyViolation("nope"))
    assert response.status_code == 403


def test_browser_session_imports_without_chrome() -> None:
    # BrowserSession must construct (and resolve selectors) without zendriver.
    session = BrowserSession()
    assert session._sel("search", "productCard") == '[data-testid="gw-product-card"]'
    assert session._sel("trolley", "subtotal") == '[data-testid="order-summary-trolley-subtotal"]'
    assert session._css("css:#onetrust-accept-btn-handler") == "#onetrust-accept-btn-handler"
