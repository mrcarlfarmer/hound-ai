"""Smoke tests for the FastAPI endpoints (spec 11.3) — confirm they import and
return the typed placeholder shapes. Requires fastapi + httpx (test deps).
"""

from __future__ import annotations

from fastapi.testclient import TestClient

from app.main import _safety_violation_handler, app, navigate
from app.safety import SafetyViolation


client = TestClient(app)


def test_health_ok() -> None:
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["status"] == "ok"


def test_login_returns_placeholder() -> None:
    response = client.post("/login")
    assert response.status_code == 200
    assert response.json()["authenticated"] is False


def test_search_returns_empty_candidates() -> None:
    response = client.post("/search", json={"term": "milk"})
    assert response.status_code == 200
    body = response.json()
    assert body["term"] == "milk"
    assert body["candidates"] == []


def test_basket_returns_empty_snapshot() -> None:
    response = client.get("/basket")
    assert response.status_code == 200
    assert response.json()["subtotal"] == 0.0


def test_order_history_returns_empty() -> None:
    response = client.get("/order-history")
    assert response.status_code == 200
    assert response.json()["orders"] == []


def test_navigate_blocks_checkout_route() -> None:
    try:
        navigate("https://www.sainsburys.co.uk/gol-ui/checkout")
        raise AssertionError("navigate should have refused a checkout URL")
    except SafetyViolation:
        pass


def test_safety_violation_maps_to_403() -> None:
    response = _safety_violation_handler(None, SafetyViolation("nope"))
    assert response.status_code == 403
