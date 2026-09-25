# Pathwise — UI Direction

## 1. Purpose

This document defines the initial visual and interaction direction for Pathwise.

It is intentionally lightweight.

The goal is not to create a complete design system before the product exists. The goal is to give future UI work a consistent visual language and prevent arbitrary styling decisions from accumulating as features are added.

---

# 2. Product Feeling

Pathwise should feel like a focused post-game analysis workspace.

Desired qualities:

* calm,
* precise,
* analytical,
* modern,
* slightly competitive,
* understated,
* trustworthy.

The interface should feel appropriate for reviewing League games after playing, without resembling a loud esports website.

---

# 3. Avoid

Pathwise should avoid:

* neon-heavy esports styling,
* excessive gradients,
* glowing borders,
* gaming clichés,
* overly dramatic animations,
* cluttered stat dashboards,
* generic enterprise-dashboard styling,
* excessive card nesting,
* rainbow-colored analytics,
* unnecessary visual noise.

The product should feel confident without being loud.

---

# 4. Theme

Pathwise is **dark-first**.

The primary experience should use:

* near-black or charcoal backgrounds,
* slightly lighter neutral surfaces,
* off-white primary text,
* muted gray secondary text,
* restrained borders.

Avoid pure black where a softer dark neutral provides better depth.

A light theme may be added later, but it is not an MVP requirement.

---

# 5. Accent Direction

The primary accent family should be:

> **Emerald / teal**

The accent should communicate:

* clarity,
* direction,
* progression,
* active state,
* Pathwise identity.

It should be used selectively.

The interface should not become predominantly green.

Accent color should primarily appear in:

* active controls,
* important highlights,
* selected states,
* key positive emphasis,
* brand identity,
* meaningful chart emphasis.

---

# 6. Semantic Colors

Semantic colors should remain distinct from general brand styling.

Use approximately:

* emerald/teal — primary Pathwise accent,
* green — positive/success,
* amber — warning/attention,
* muted red — negative/failure,
* neutral gray — supporting information.

Avoid overly saturated warning/error colors unless urgency genuinely requires them.

A loss should not turn the entire screen red.

---

# 7. Typography

Typography should be clean, modern, and highly readable.

Prefer the standard Next.js font-loading approach.

Initial preference:

> **Geist**

Avoid introducing decorative or gaming-specific typefaces.

Hierarchy should come primarily from:

* size,
* weight,
* spacing,
* contrast,

rather than excessive font variation.

---

# 8. Information Hierarchy

Pathwise follows one central visual principle:

> **The explanation gets the emphasis. The numbers prove it.**

On a match-review screen, priority should generally be:

1. overall takeaway,
2. important review periods,
3. recommended alternatives,
4. supporting evidence,
5. detailed statistics and charts.

Raw statistics should not visually overpower the interpretation they support.

---

# 9. Density

Pathwise should use **moderate information density**.

The interface should feel spacious enough to read comfortably, while still supporting data-heavy match review.

Avoid both extremes:

* huge marketing-style empty spaces,
* cramped analytics dashboards.

Match lists should be relatively compact.

Game-review explanations should have more breathing room.

---

# 10. Surfaces

Use restrained surface hierarchy.

Typical hierarchy:

```text
Application background
    ↓
Primary content surface
    ↓
Optional nested evidence/detail surface
```

Avoid deeply nested cards.

Cards should exist because content represents a meaningful unit, not because every section needs a rectangle.

---

# 11. Borders and Shadows

Borders should be:

* subtle,
* neutral,
* low contrast.

Shadows should be minimal.

Depth should primarily come from:

* surface tone,
* spacing,
* hierarchy,
* borders.

Avoid floating-card-heavy design.

---

# 12. Corners

Use moderately rounded corners.

The interface should feel modern but not playful or bubbly.

Large content containers may use slightly stronger rounding than compact controls.

---

# 13. Motion

Motion should be subtle and purposeful.

Appropriate examples:

* small hover transitions,
* loading-state transitions,
* expandable detail sections,
* chart transitions where useful.

Avoid decorative animation.

Movement should never distract from analysis.

---

# 14. Match Result Presentation

Wins and losses should be clearly recognizable but visually restrained.

Do not make:

* an entire win page green,
* an entire loss page red.

Result color is supporting context, not the primary visual identity of the review.

---

# 15. Review Windows

Review windows are one of Pathwise's most important UI concepts.

They should visually communicate:

* when the period occurred,
* why it matters,
* what changed,
* what evidence supports the conclusion.

A review window should feel more like an analytical narrative than a stat card.

The recommended structure is:

```text
Review period
13:10–17:45

What changed
Short human-readable explanation.

Evidence
Supporting metrics/events.

What could have been different
Alternative action or decision.
```

---

# 16. Charts

Charts should use restrained color.

Prefer:

* neutral secondary series,
* one or two emphasized meaningful series,
* semantic colors only when their meaning is clear.

Avoid rainbow palettes.

Charts exist to support a specific analytical claim.

Do not add a chart merely because data is available.

---

# 17. Match List

The match list should prioritize fast scanning.

Important information may include:

* champion,
* result,
* role,
* game duration,
* played date/time,
* analysis/support status.

Do not overload each row with every available League statistic.

Detailed information belongs on the match page.

---

# 18. Interaction Style

Interactions should be predictable and understated.

Prefer:

* clear buttons,
* obvious clickable rows,
* explicit loading states,
* understandable error messages,
* progressive disclosure for detail.

Avoid hidden interactions that require discovery.

---

# 19. Responsive Behavior

Desktop is the primary MVP environment.

The UI should still remain reasonably responsive and usable at smaller widths.

Do not invest heavily in mobile-specific interaction design during MVP unless a feature requires it.

---

# 20. shadcn/ui

shadcn/ui is the initial component foundation.

Use it as a starting point rather than as a visual identity.

Components may be adapted to fit Pathwise.

Do not allow default shadcn styling to determine the entire appearance of the product.

---

# 21. Design Tokens

Prefer defining reusable theme values through the existing Tailwind/shadcn token system instead of scattering literal colors throughout components.

Examples include:

* background,
* foreground,
* card,
* muted,
* border,
* primary,
* destructive.

Product-specific tokens may be added later when a concrete need appears.

Avoid prematurely creating a large custom token taxonomy.

---

# 22. Visual Decision Rule

When deciding between two visual approaches, prefer the one that makes the game easier to understand.

Ask:

> Does this make the analysis clearer, or merely make the interface busier?

Clarity wins.

---

# 23. Combat Encounters in Review Periods

Show Combat encounters between period observations and spatial evidence. The default Whole review period view includes every encounter's recorded champion kills. Selecting an encounter narrows combat and nearby objective events to its exact membership and limits existing player and enemy frame samples to its recorded kill range plus 30 seconds, within the review bounds. Frame samples remain nearby context, not evidence of participation. Keep map layers and their toggle choices intact, and reset encounter selection when the review period changes. Use “Single recorded kill” for a singleton and explain that first and last recorded kill times do not establish full fight duration.
