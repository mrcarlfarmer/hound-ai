"""Real Sainsbury's browser automation via zendriver (spec 11, Phase 6).

:class:`BrowserSession` drives an undetected Chrome (zendriver / CDP) against the
live Sainsbury's site, using the externalised ``selectors.json`` map. The site
spans two stacks mid-migration (NEW React ``/groceries/`` with ``gw-*`` testids;
OLD Angular ``/gol-ui/`` trolley with ``pt-*``/``order-summary-*``), so we bind
to ``data-testid`` only and **wait on a selector, never on network idle** (the
new stack streams ads forever and never settles).

Every navigation runs through the R1 URL denylist and every click through the
R1 testid denylist (``app.safety``). The session NEVER selects a slot and NEVER
checks out — those controls are denylisted and there is no code path to them.

``zendriver`` is imported lazily inside :meth:`start` so this module imports
(and the pure parsing/safety logic is testable) without Chrome installed.
"""

from __future__ import annotations

import asyncio
import random
from typing import Any

from app.config import Settings, get_settings, load_selectors
from app.models import (
    BasketLine,
    BasketSnapshot,
    LoginResult,
    ProductCandidate,
)
from app.parsing import build_candidate, parse_price
from app.safety import assert_testid_allowed, assert_url_allowed


class BrowserSession:
    """Owns the Chrome tab and exposes mid-level grocery operations."""

    def __init__(
        self,
        settings: Settings | None = None,
        selectors: dict[str, Any] | None = None,
    ) -> None:
        self._settings = settings or get_settings()
        self._selectors = selectors or load_selectors()
        self._browser: Any = None
        self._tab: Any = None
        self._authenticated = False

    # ── selector helpers ────────────────────────────────────────────────────
    def _group(self, name: str) -> dict[str, str]:
        return self._selectors.get(name, {})

    @staticmethod
    def _css(value: str) -> str:
        """Resolve a selectors.json value to a CSS selector.

        ``"css:..."`` is treated as a raw CSS selector; anything else is a
        ``data-testid`` name and becomes ``[data-testid="name"]``.
        """
        if value.startswith("css:"):
            return value[len("css:"):]
        return f'[data-testid="{value}"]'

    def _sel(self, group: str, key: str) -> str:
        return self._css(self._group(group)[key])

    def _url(self, key: str, **fmt: str) -> str:
        urls = self._selectors["urls"]
        return urls["base"] + urls[key].format(**fmt)

    # ── lifecycle ───────────────────────────────────────────────────────────
    async def start(self) -> None:
        """Launch Chrome with the persisted profile (lazy zendriver import)."""
        if self._browser is not None:
            return
        import zendriver as zd  # lazy: keeps module importable without Chrome

        self._browser = await zd.start(
            headless=self._settings.headless,
            user_data_dir=self._settings.profile_dir,
        )
        self._tab = await self._browser.get("about:blank")

    async def close(self) -> None:
        if self._browser is not None:
            await self._browser.stop()
            self._browser = None
            self._tab = None

    # ── guarded primitives ──────────────────────────────────────────────────
    async def _navigate(self, url: str) -> None:
        """Navigate through the R1 URL denylist, then settle on a selector."""
        assert_url_allowed(url)
        await self._tab.get(url)
        await self._dismiss_consent()

    async def _human_pause(self) -> None:
        lo = self._settings.nav_min_delay_ms
        hi = max(lo, self._settings.nav_max_delay_ms)
        await asyncio.sleep(random.uniform(lo, hi) / 1000.0)

    async def _dismiss_consent(self) -> None:
        selector = self._css(self._group("consent")["acceptButton"])
        try:
            button = await self._tab.select(selector, timeout=5)
            if button is not None:
                await button.click()
        except Exception:
            # Consent already accepted (persisted profile) or not shown.
            pass

    async def _wait_ready(self, selector: str, timeout: int = 30) -> Any:
        """Wait on a DOM selector (NOT network idle — new stack never settles)."""
        return await self._tab.select(selector, timeout=timeout)

    async def _safe_click(self, element: Any, testid: str) -> None:
        """Click an element only if its testid is not R1-denylisted."""
        assert_testid_allowed(testid)
        await element.click()
        await self._human_pause()

    # ── operations ──────────────────────────────────────────────────────────
    async def ensure_login(self) -> LoginResult:
        """Reuse the persisted profile; only sign in if not already authed."""
        if self._tab is None:
            await self.start()
        if self._authenticated:
            return LoginResult(authenticated=True, message="session reused")
        if not self._settings.has_credentials:
            return LoginResult(authenticated=False, message="no credentials configured")

        await self._navigate(self._url("search", term="milk"))
        # If a product grid renders, the persisted profile is already signed in.
        try:
            await self._wait_ready(self._sel("search", "productCard"), timeout=15)
            self._authenticated = True
            return LoginResult(authenticated=True, message="profile session valid")
        except Exception:
            return await self._perform_login()

    async def _perform_login(self) -> LoginResult:
        """Drive the username/password form (creds never logged)."""
        await self._navigate(self._selectors["urls"]["base"] + "/gol-ui/login")
        try:
            user = await self._wait_ready('input[name="username"]', timeout=20)
            await user.send_keys(self._settings.username or "")
            pw = await self._tab.select('input[name="password"]')
            await pw.send_keys(self._settings.password or "")
            submit = await self._tab.find("Log in", best_match=True)
            await submit.click()
            await self._wait_ready(self._sel("search", "productCard"), timeout=30)
            self._authenticated = True
            return LoginResult(authenticated=True, message="logged in")
        except Exception as exc:  # pragma: no cover - needs live site
            return LoginResult(authenticated=False, message=f"login failed: {type(exc).__name__}")

    async def search(self, term: str) -> list[ProductCandidate]:
        """Search the NEW stack and parse ranked candidates from the grid."""
        await self._navigate(self._url("search", term=term))
        await self._wait_ready(self._sel("search", "productCard"), timeout=30)
        cards = await self._tab.select_all(self._sel("search", "productCard"))
        candidates: list[ProductCandidate] = []
        for card in cards:
            raw = await self._extract_card(card)
            candidate = build_candidate(raw)
            if candidate is not None:
                candidates.append(candidate)
        return candidates

    async def _extract_card(self, card: Any) -> dict:
        """Pull raw strings/flags from one gw-product-card (scoped queries)."""
        g = self._group("search")

        async def text_of(key: str) -> str | None:
            try:
                el = await card.query_selector(self._css(g[key]))
                return el.text if el is not None else None
            except Exception:
                return None

        async def present(key: str) -> bool:
            try:
                el = await card.query_selector(self._css(g[key]))
                return el is not None
            except Exception:
                return False

        href = None
        try:
            link = await card.query_selector(self._css(g["link"]))
            if link is not None:
                href = link.attrs.get("href")
        except Exception:
            href = None

        return {
            "name": await text_of("name"),
            "href": href,
            "retailPriceText": await text_of("retailPrice"),
            "nectarPresent": await present("nectarBadge"),
            "nectarPriceText": await text_of("nectarBadge"),
            "favouriteFull": await present("favouriteFull"),
            "perUnitText": await text_of("perUnitPrice"),
            "addPresent": await present("addToBasket"),
        }

    async def add_to_basket(self, product_id: str, quantity: float) -> BasketSnapshot:
        """Add a product (by slug/id) to the basket via its card's Add button."""
        await self._navigate(self._url("product", slug=product_id))
        add_testid = self._group("product")["addToBasket"]
        button = await self._wait_ready(self._sel("product", "addToBasket"), timeout=30)
        await self._safe_click(button, add_testid)
        if quantity and quantity > 1:
            await self.set_quantity(product_id, quantity)
        return await self.read_basket()

    async def set_quantity(self, product_id: str, quantity: float) -> BasketSnapshot:
        """Step a product's quantity up to the target via the +/- counter.

        Decrementing the LAST unit triggers a remove-confirm modal, so we only
        ever increment here (never drive a line to zero).
        """
        await self._navigate(self._url("product", slug=product_id))
        inc = await self._wait_ready(self._sel("search", "quantityIncrement"), timeout=20)
        target = int(max(1, round(quantity)))
        for _ in range(target - 1):
            await inc.click()
            await self._human_pause()
        return await self.read_basket()

    async def read_basket(self) -> BasketSnapshot:
        """Read the OLD gol-ui trolley: lines + subtotal (read-only)."""
        await self._navigate(self._url("trolley"))
        await asyncio.sleep(self._selectors["waits"]["golUiHydrationSeconds"])
        g = self._group("trolley")
        lines: list[BasketLine] = []
        try:
            items = await self._tab.select_all(self._css(g["lineItem"]))
        except Exception:
            items = []
        for item in items:
            line = await self._extract_trolley_line(item)
            if line is not None:
                lines.append(line)
        subtotal = await self._read_subtotal()
        return BasketSnapshot(lines=lines, subtotal=subtotal)

    async def _read_subtotal(self) -> float:
        try:
            el = await self._tab.select(self._sel("trolley", "subtotal"), timeout=10)
            return parse_price(el.text) or 0.0
        except Exception:
            return 0.0

    async def _extract_trolley_line(self, item: Any) -> BasketLine | None:
        g = self._group("trolley")

        async def text_of(key: str) -> str | None:
            try:
                el = await item.query_selector(self._css(g[key]))
                return el.text if el is not None else None
            except Exception:
                return None

        name = await text_of("lineDescription")
        if not name:
            return None
        from app.parsing import build_basket_line

        return build_basket_line(
            {
                "productName": name,
                "quantityText": await text_of("lineQuantity"),
                "unitPriceText": await text_of("lineUnitPrice"),
                "subtotalText": await text_of("lineSubtotal"),
                "offer": (await text_of("lineOfferFlag")) is not None,
            }
        )

    async def read_favourites(self) -> list[ProductCandidate]:
        """Scrape the NEW /groceries/favourites grid (lazy-scroll to load all)."""
        await self._navigate(self._url("favourites"))
        await self._wait_ready(self._sel("favourites", "productCard"), timeout=30)
        await self._lazy_scroll(self._sel("favourites", "productCard"))
        cards = await self._tab.select_all(self._sel("favourites", "productCard"))
        out: list[ProductCandidate] = []
        for card in cards:
            raw = await self._extract_card(card)
            raw["favouriteFull"] = True  # every card on this page is a favourite
            candidate = build_candidate(raw)
            if candidate is not None:
                out.append(candidate)
        return out

    async def _lazy_scroll(self, card_selector: str, max_rounds: int = 25) -> None:
        """Scroll until the card count stops growing (favourites lazy-load)."""
        previous = -1
        for _ in range(max_rounds):
            cards = await self._tab.select_all(card_selector)
            count = len(cards)
            if count == previous:
                break
            previous = count
            await self._tab.scroll_down(800)
            await self._human_pause()

    async def screenshot(self, region: str | None = None) -> str:
        path = f"/data/grocery/screenshots/{region or 'page'}.png"
        try:
            await self._tab.save_screenshot(path)
        except Exception:  # pragma: no cover - needs live tab
            pass
        return path
