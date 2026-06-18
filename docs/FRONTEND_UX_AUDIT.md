# HaulScout Frontend UX Audit

## Major UX Problems Found

### Navigation
- **Stack-only navigation**: No tab bar. Every screen change required bouncing through dashboard. Users in the field couldn't quickly switch between Scout, Inventory, and Trips.
- **Scout buried**: The highest-value action (scouting) was a small nav tile equal in weight to Settings.
- **No persistent nav context**: No way to tell "where am I in the app" at a glance.

### Dashboard
- **Wall of numbers**: Four metric boxes + an offline info card + a flat nav grid. No action orientation.
- **No "what should I do next"**: Dashboard didn't surface items needing attention, pending sync, or workflow bottlenecks.

### Scout Flow
- **Form-heavy, not command-center**: All inputs visible at once. No progressive disclosure. Scan/Search actions were small secondary buttons.
- **No inline deal check**: Users had to navigate away to run analysis, breaking the scout flow.

### Deal Analyzer
- **10+ inputs visible at once**: Overwhelming. Platform fees, risk reserve, and pricing basis options all shown in one long form.
- **Developer-facing labels**: "ManualEstimate", "LiveValidated", "CachedStale" — not user-friendly.
- **Plain text result**: Buy/Pass decision was a StatusPill + plain text lines. No visual impact. No highlighted max buy price.
- **No confidence visualization**: Confidence score was "62/100" in plain text.

### Inventory
- **No filters or search**: Flat list of every item. No way to find specific items quickly.
- **No status grouping**: All statuses mixed together. No way to see "what needs work."
- **Workflow buttons**: Status changes shown as 9 small buttons in a wrap. Not a workflow visualization.

### Listing Queue
- **Flat list**: No grouping by status. No workflow stages visible.
- **MVP disclaimer as first content**: User saw an implementation note before any listings.

### Tasks
- **4 status buttons per task**: Every task card had Open/InProgress/Blocked/Completed buttons. Noisy. The primary action (mark next status) was not differentiated.

### Budget
- **No progress visualization**: Just text numbers. No budget bar showing how much was spent.
- **No overspending warnings**: No visual indicator when approaching or exceeding budget.

### Visual & Interaction
- **Flat cards with 1px borders**: No shadows, no depth. Everything looked the same weight.
- **No loading states**: Blank screens until data arrived.
- **Minimal empty states**: Just caption text like "No inventory yet." No guidance on what to do.
- **No error recovery**: Error messages were plain text in cards. No retry buttons.
- **No offline banners**: Offline state shown as a small StatusPill. No explanation of what works offline.
- **No pull-to-refresh**: Standard mobile interaction missing.
- **Currency inputs without $ prefix**: Users typed bare numbers for prices.
- **Spacing scale too small**: Max spacing was 24px. Screens felt cramped.

## Changes Made

### Design System
- **Expanded theme.ts**: Added shadows (sm/md/lg), radius scale, typography scale with 10 variants (hero, title, subtitle, body, bodyBold, caption, label, metric, metricSmall), tone system with 6 tones.
- **Extended spacing**: Added xxs (2), xxl (32), xxxl (48).
- **Added decision colors**: buyGreen, negotiateAmber, passRed, researchBlue, unavailableGray — each with background variant.

### New Components (13)
- `EmptyState`: Icon + title + message + optional CTA. Used on every list screen.
- `LoadingState`: Centered spinner + message. Used on every data-loading screen.
- `ErrorBanner`: Red banner with icon + message + retry button.
- `OfflineBanner`: Amber banner explaining offline limitations.
- `ProgressBar`: Configurable color/height progress bar. Used in Budget and Trip Detail.
- `SectionHeader`: Uppercase label with optional trailing element.
- `ActionCard`: Icon + title + subtitle + chevron. Used on Dashboard and More.
- `DecisionCard`: Full deal recommendation visualization with decision header (color-coded), metrics grid, pricing basis, and warnings.
- `MetricCard`: Label + large value + icon + optional tone. Used on Dashboard.
- `FilterChips`: Horizontal scrollable filter chips. Used on Inventory, Listing Queue, Tasks.
- `Divider`: Simple horizontal line separator.
- `ConfidenceRing`: Circular score indicator with color-coded label.
- `IconButton`: Compact icon-only pressable.

### Updated Components (6)
- `Card`: Added shadow variants (default/flat/elevated), noPadding option, rounded corner increase.
- `Button`: Added sizes (sm/md/lg), ghost variant, fullWidth option, IconButton export.
- `Text`: Added hero, bodyBold, label, metricSmall variants. Added color prop.
- `TextField`: Added $ prefix support, hint text, error border color.
- `StatusPill`: Added sm size. Uses tone system from theme.
- `Screen`: Added pull-to-refresh, configurable SafeArea edges, increased bottom padding.

### Navigation
- **Added bottom tab bar** with 5 tabs: Home, Scout, Inventory, Trips, More.
- **Scout as hero tab**: Elevated circular icon, visually prominent center position.
- **Tab bar styling**: Clean white bar with subtle shadow, 84px height for comfortable touch targets.
- **Stack screens**: Detail views, modals, and secondary screens push over tabs (hiding tab bar). Scanner is fullScreenModal.

### Screen Improvements

**Dashboard**: Action-oriented with MetricCards (Inventory count, Net Profit, Cash Deployed, Active Listings), attention items (ActionCards for unlisted items, stale items, pending sync, drafts), quick actions, and "all clear" state.

**Scout**: Command-center layout with 4 large quick-action tiles (Scan, Search, Market Search, Add Comp), progressive disclosure form (condition/source/notes hidden by default), inline success messages, prominent "Run Deal Check" and "Save Draft" buttons.

**Deal Analyzer**: Two-field primary form (expected sale price + asking price), collapsible advanced fee section, human-readable pricing basis labels, full DecisionCard result with color-coded decision header, highlighted max buy price, profit coloring, confidence badge, and expandable warnings.

**Inventory**: Search field, horizontal filter chips (All/Active/Needs Work/Listed/Sold), pressable list cards with status pills, human-readable status labels ("Needs Cleaning" not "NeedsCleaning"), item count.

**Inventory Detail**: Detail grid with labeled rows, projected profit calculation, visual workflow steps (icons + color for completed/current/future stages), action buttons, comps section with empty state.

**Inventory Edit**: Sectioned form (Item Info, Pricing), side-by-side currency fields, saving state.

**Budget**: Hero remaining amount with color warnings, progress bar, over-budget alert, summary cards row, inline expense form.

**Listing Queue**: Filter chips, grouped by status with section headers and status pills, improved card layout.

**Tasks**: Inline quick-create with disabled state, filter chips (Active/All/Done), one-tap next-action button per task (circular icon button: Start/Done/Unblock/Reopen), strikethrough for completed.

**Trips**: Show/hide create form, trip cards with metadata icons (items, budget, date), active badge.

**Trip Detail**: Budget progress bar, summary grid, scout/inventory shortcuts.

**Scanner**: Crosshair overlay, "Continue to Scout" label, elevated bottom panel.

**Manual Search**: Loading/searched/empty states, pressable product cards, secondary action row.

**Marketplace Links**: Provider icons, data boundary notice, pressable link cards with external icon.

**Comp Capture**: Visual comp type selector (cards instead of buttons), active listing warning, progressive disclosure for optional fields, success/queued messages.

**Settings**: Organized sections, connection status with icon, sync queue with count badge, draft list with status pills, about section.

## Design System Summary

| Token | Values |
|-------|--------|
| Colors | 30+ semantic tokens covering surfaces, text, semantic, and decision colors |
| Spacing | xxs(2) xs(4) sm(8) md(12) lg(16) xl(24) xxl(32) xxxl(48) |
| Radius | sm(6) md(10) lg(14) xl(20) full(999) |
| Shadows | sm, md, lg with iOS + Android elevation |
| Typography | 10 variants: hero, title, subtitle, body, bodyBold, caption, label, metric, metricSmall |
| Tones | neutral, success, warning, danger, info, primary — each with bg/text/border |

## Remaining UX Debt

1. **No haptic feedback**: Taps on Scout quick-actions and task completion should have light haptics.
2. **No skeleton loading**: LoadingState uses ActivityIndicator. Skeleton placeholders for cards would feel faster.
3. **No swipe gestures**: Swipe-to-complete on tasks, swipe-to-archive on inventory items.
4. **No search on dashboard**: Global search across inventory, tasks, and trips.
5. **No photos in inventory cards**: Item thumbnails would help identification.
6. **No route/sourcing active mode**: Driver-mode experience for active sourcing trips with stop progress.
7. **No keyboard avoidance**: Forms may need KeyboardAvoidingView wrapping on some devices.
8. **No animation**: Screen transitions and card appearances would benefit from subtle entrance animations.

## Suggested Future Improvements

1. **Bottom sheet modals** for quick-add comp, quick-add expense, and status changes.
2. **Batch actions** on inventory list (select multiple items, change status).
3. **Dashboard customization**: Let users reorder or hide dashboard sections.
4. **Profit analytics**: Weekly/monthly profit trends, best-performing categories.
5. **Barcode-to-product matching**: Auto-fill item details from scanned barcode.
6. **Push notifications**: Alert when items sell, when sync completes, when budget is low.
7. **Dark mode**: Theme tokens are structured for easy dark mode addition.
8. **Accessibility audit**: VoiceOver/TalkBack testing, dynamic type support.
