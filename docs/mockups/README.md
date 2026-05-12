# UI Mockups

Visual reference for implementation. **Open `index.html` to browse all mockups.**

## Structure

```
mockups/
├── index.html              # Gallery — start here
├── shared/
│   └── design-tokens.css   # Single source of truth: colors, typography, spacing, shadows
├── desktop/                # Avalonia screens — full-window mockups (link to shared CSS)
│   ├── dashboard.html
│   └── transactions.html
├── mobile/                 # MAUI screens — shown in iPhone frame (link to shared CSS)
│   └── receipt-capture.html
└── standalone/             # Same mockups but with CSS inlined (single-file, easy to share)
    ├── desktop-dashboard.html
    ├── desktop-transactions.html
    └── mobile-receipt-capture.html
```

## How to use

**Browsing:** open `index.html` in any browser. Light/dark theme toggle is in the top-right corner; the choice persists across mockup files via localStorage.

**Implementing:** when building a screen in Avalonia or MAUI, open the matching mockup and match the spirit — spacing rhythm, color usage, typography hierarchy, component behavior. Don't try to reproduce exact pixel values from the HTML; instead, port the design tokens (in `shared/design-tokens.css`) to the framework's resource system:

- **Avalonia:** map CSS custom properties to `App.axaml` `<ResourceDictionary>` entries
- **MAUI:** map them to `Resources/Styles/Colors.xaml` and `Resources/Styles/Styles.xaml`

The token names are kept consistent across the mockups and the architecture docs.

**Sharing a single mockup:** use the `standalone/` versions (CSS inlined, no external dependencies).

## Status

| Mockup | Status |
|---|---|
| Desktop / Dashboard | ✅ Ready |
| Desktop / Transactions | ✅ Ready |
| Desktop / Import | ⏳ Planned |
| Desktop / Categories & Rules | ⏳ Planned |
| Desktop / Receipts list | ⏳ Planned |
| Desktop / Advisor & Goals | ⏳ Planned |
| Desktop / Chat with data | ⏳ Planned |
| Desktop / Anomalies & Alerts | ⏳ Planned |
| Desktop / Settings | ⏳ Planned |
| Desktop / Setup wizard | ⏳ Planned |
| Mobile / Receipt capture | ✅ Ready |
| Mobile / Home | ⏳ Planned |
| Mobile / Receipts list | ⏳ Planned |
| Mobile / Alerts | ⏳ Planned |
| Mobile / Goals | ⏳ Planned |

## Editing

If you change a design token, edit `shared/design-tokens.css` — all desktop and mobile mockups using `<link rel="stylesheet">` will update. Then regenerate the standalone versions:

```bash
# from repo root
python3 tools/inline-mockup-css.py    # script TBD when needed
```

For now, the standalone files in `standalone/` are point-in-time snapshots.

## Notes on data

All amounts, merchant names, and account numbers shown in mockups are fictional. Polish merchants (Lidl, Orlen, Biedronka, Allegro, PKO BP, mBank, ING) are used for realistic context. No real transaction data appears anywhere.
