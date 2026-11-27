# UI rules

Shush is used by lab operators who want to deploy and get on with their day. Every screen should answer "what is
this, and what do I do here?" at a glance. When in doubt, remove.

## Rules

1. **One primary action per page.** The page header holds at most one primary button and one plain button. Everything
   else goes in the `⋯` menu (`<Menu>`, items are `<button class="menu__item">` / `<a class="menu__item">`, a
   destructive one adds `menu__item--danger`, separate groups with `<div class="menu__sep"></div>`).
2. **No per-row buttons in lists.** A row is a link: `<tr class="is-link">` with the main cell's anchor as
   `<a class="row-link" href="…">`; clicking anywhere on the row follows it. Row actions live on the item's own page.
   Exception: editing lists (removing a member from a group) may keep one small icon button per row.
3. **No paragraphs of explanation.** A page may have one short lede sentence, only when the page's purpose isn't obvious.
   Panels don't get a subtitle (`panel__sub`) unless it carries information the user needs right now. Help text under a
   form field: one short line, or none. Put longer explanations in `title=` tooltips or the docs.
4. **One line of context under a title**, not a grid of label/value facts: `<p class="meta"><span>…</span><span>…</span></p>`
   (items are separated by `·` automatically).
5. **Don't show what isn't known.** No columns or rows full of "Unknown"/"—": hide the row, or fold optional facts
   into one muted line that only lists what is known.
6. **Quiet empty states.** `<EmptyState Title="No runs yet." />`: one short sentence, an optional short hint, an
   optional single action. Hide whole sections that would only say "nothing here" when the page has better content.
7. **Flat, not nested.** No panel inside a panel, no toolbar panel above a content panel. One panel per topic.
   Prefer a plain heading + table over a card containing a card.
8. **Colour means something.** Lime = selected/current, teal = done/ok, blue = running, crimson = failed/danger,
   purple = changed for this deployment. Don't use filled colour pills for plain labels (e.g. group names): use text or a
   plain link.
9. **Fewer words on buttons.** "Deploy", "Dry run", "Save", "Duplicate". Not "Create the copy", "Use the recipe's".
