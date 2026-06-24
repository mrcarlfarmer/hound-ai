"""grocery-browser sidecar — FastAPI app (spec 11).

Phase 1 scaffold: this exposes the RPC surface from spec 11.3 returning typed
placeholder responses, plus the spec 11.4 safety interlocks. It does NOT yet
drive a real zendriver/Chrome session — that arrives in Phase 3.

Hard safety rule (R1): the sidecar MUST NEVER select a delivery slot and MUST
NEVER check out or pay. The :mod:`app.safety` guards (URL denylist + action
allowlist) enforce this and are unit-tested independently of the browser.
"""

from __future__ import annotations

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from app.models import (
    AddToBasketRequest,
    BasketSnapshot,
    LoginResult,
    OrderHistoryResult,
    ScreenshotResult,
    SearchRequest,
    SearchResult,
)
from app.safety import SafetyViolation, assert_action_allowed, assert_url_allowed

app = FastAPI(title="grocery-browser", version="0.1.0")


@app.get("/health")
def health() -> dict[str, str]:
    """Liveness probe for docker-compose health checks."""
    return {"status": "ok", "service": "grocery-browser"}


@app.post("/login", response_model=LoginResult)
def login() -> LoginResult:
    """Ensure an authenticated Sainsbury's session (reuses the saved profile).

    Phase 1 scaffold: returns a placeholder. No real login is performed.
    """
    assert_action_allowed("login")
    return LoginResult(authenticated=False, message="scaffold: no browser session yet")


@app.post("/search", response_model=SearchResult)
def search(request: SearchRequest) -> SearchResult:
    """Return ranked candidate products for a search term.

    Phase 1 scaffold: returns an empty candidate list.
    """
    assert_action_allowed("search")
    return SearchResult(term=request.term, candidates=[])


@app.post("/add", response_model=BasketSnapshot)
def add(request: AddToBasketRequest) -> BasketSnapshot:
    """Add a product to the basket and return the updated subtotal + lines.

    Phase 1 scaffold: returns an empty basket. Real add-to-basket arrives later.
    """
    assert_action_allowed("add_to_basket")
    # The .NET side passes a product id, never a checkout/slot control.
    _ = request
    return BasketSnapshot(lines=[], subtotal=0.0)


@app.get("/basket", response_model=BasketSnapshot)
def basket() -> BasketSnapshot:
    """Return the current basket lines + subtotal."""
    assert_action_allowed("read_basket")
    return BasketSnapshot(lines=[], subtotal=0.0)


@app.get("/order-history", response_model=OrderHistoryResult)
def order_history() -> OrderHistoryResult:
    """Return recent orders for learning."""
    assert_action_allowed("read_order_history")
    return OrderHistoryResult(orders=[])


@app.get("/screenshot", response_model=ScreenshotResult)
def screenshot(region: str | None = None) -> ScreenshotResult:
    """Return a reference to a captured PNG for vision grounding / trace evidence."""
    assert_action_allowed("screenshot")
    return ScreenshotResult(region=region, imgRef="scaffold-placeholder.png")


@app.exception_handler(SafetyViolation)
def _safety_violation_handler(_request: Request, exc: SafetyViolation) -> JSONResponse:
    """Map a safety breach to HTTP 403 so callers can never bypass R1."""
    return JSONResponse(status_code=403, content={"detail": str(exc)})


def navigate(url: str) -> None:
    """Guarded navigation entry point used by the (future) browser driver.

    Exposed at module level so Phase 3 browser code routes every navigation
    through the URL denylist. Phase 1 scaffold: it only enforces the guard.
    """
    assert_url_allowed(url)
