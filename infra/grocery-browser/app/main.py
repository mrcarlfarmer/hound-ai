"""grocery-browser sidecar — FastAPI app (spec 11).

Exposes the RPC surface from spec 11.3 backed by the real zendriver
:class:`~app.browser.BrowserSession`. The sidecar owns the DOM; the .NET
ShopperHound only ranks/selects.

Hard safety rule (R1): the sidecar MUST NEVER select a delivery slot and MUST
NEVER check out or pay. The :mod:`app.safety` guards (URL denylist + testid
denylist + action allowlist) enforce this on every navigation/click/action and
any breach is mapped to HTTP 403 so callers can never bypass it.

The browser is provided via the :func:`get_session` dependency so tests can
override it with a fake session — no Chrome required to exercise the RPC layer.
"""

from __future__ import annotations

from fastapi import Depends, FastAPI, Request
from fastapi.responses import JSONResponse

from app.browser import BrowserSession
from app.config import get_settings
from app.models import (
    AddToBasketRequest,
    BasketSnapshot,
    FavouritesResult,
    LoginResult,
    OrderHistoryResult,
    ScreenshotResult,
    SearchRequest,
    SearchResult,
    SetQuantityRequest,
)
from app.safety import (
    SafetyViolation,
    assert_action_allowed,
    assert_url_allowed,
)

app = FastAPI(title="grocery-browser", version="1.0.0")

_session: BrowserSession | None = None


async def get_session() -> BrowserSession:
    """Return the process-wide browser session, starting Chrome on first use.

    Overridden in tests via ``app.dependency_overrides`` with a fake session.
    """
    global _session
    if _session is None:
        _session = BrowserSession()
        await _session.start()
    return _session


@app.get("/health")
def health() -> dict[str, str]:
    """Liveness probe for docker-compose health checks (no browser needed)."""
    return {"status": "ok", "service": "grocery-browser"}


@app.post("/login", response_model=LoginResult)
async def login(session: BrowserSession = Depends(get_session)) -> LoginResult:
    """Ensure an authenticated Sainsbury's session (reuses the saved profile)."""
    assert_action_allowed("login")
    return await session.ensure_login()


@app.post("/search", response_model=SearchResult)
async def search(
    request: SearchRequest, session: BrowserSession = Depends(get_session)
) -> SearchResult:
    """Return candidate products for a search term (NEW /groceries/ stack)."""
    assert_action_allowed("search")
    candidates = await session.search(request.term)
    return SearchResult(term=request.term, candidates=candidates)


@app.post("/add", response_model=BasketSnapshot)
async def add(
    request: AddToBasketRequest, session: BrowserSession = Depends(get_session)
) -> BasketSnapshot:
    """Add a product to the basket and return the updated subtotal + lines.

    Honours the DRY_RUN kill switch (spec 11.4): when set, the live basket is
    never mutated — we just read it back.
    """
    assert_action_allowed("add_to_basket")
    if get_settings().dry_run:
        return await session.read_basket()
    return await session.add_to_basket(request.productId, request.quantity)


@app.post("/set-quantity", response_model=BasketSnapshot)
async def set_quantity(
    request: SetQuantityRequest, session: BrowserSession = Depends(get_session)
) -> BasketSnapshot:
    """Set a product's basket quantity via the +/- counter (never to zero)."""
    assert_action_allowed("set_quantity")
    if get_settings().dry_run:
        return await session.read_basket()
    return await session.set_quantity(request.productId, request.quantity)


@app.get("/basket", response_model=BasketSnapshot)
async def basket(session: BrowserSession = Depends(get_session)) -> BasketSnapshot:
    """Return the current basket lines + subtotal (OLD gol-ui trolley, read-only)."""
    assert_action_allowed("read_basket")
    return await session.read_basket()


@app.get("/favourites", response_model=FavouritesResult)
async def favourites(session: BrowserSession = Depends(get_session)) -> FavouritesResult:
    """Return the user's favourites (NEW /groceries/favourites grid)."""
    assert_action_allowed("read_favourites")
    candidates = await session.read_favourites()
    return FavouritesResult(candidates=candidates)


@app.get("/order-history", response_model=OrderHistoryResult)
async def order_history() -> OrderHistoryResult:
    """Return recent orders for learning (placeholder until §10 history scrape)."""
    assert_action_allowed("read_order_history")
    return OrderHistoryResult(orders=[])


@app.get("/screenshot", response_model=ScreenshotResult)
async def screenshot(
    region: str | None = None, session: BrowserSession = Depends(get_session)
) -> ScreenshotResult:
    """Return a reference to a captured PNG for vision grounding / trace evidence."""
    assert_action_allowed("screenshot")
    img_ref = await session.screenshot(region)
    return ScreenshotResult(region=region, imgRef=img_ref)


@app.exception_handler(SafetyViolation)
def _safety_violation_handler(_request: Request, exc: SafetyViolation) -> JSONResponse:
    """Map a safety breach to HTTP 403 so callers can never bypass R1."""
    return JSONResponse(status_code=403, content={"detail": str(exc)})


def navigate(url: str) -> None:
    """Guarded navigation entry point — routes every navigation through the
    URL denylist. Exposed at module level so the guard is independently tested.
    """
    assert_url_allowed(url)
